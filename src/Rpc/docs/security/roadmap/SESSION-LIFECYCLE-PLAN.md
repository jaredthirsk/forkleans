# Granville RPC Session Lifecycle Management Plan

**Document Version**: 1.0
**Created**: 2025-11-30
**Status**: Planning
**Priority**: HIGH (Security-Critical)

## Executive Summary

This document specifies the complete lifecycle of RPC sessions from creation (HTTP authentication) through validation, expiry, revocation, and cleanup. Proper session management prevents authentication bypass, session hijacking, and resource leaks.

### Current State

- **Challenge**: Sessions bridge HTTP authentication (Silo) with UDP game communication (ActionServers)
- **Multi-Server Problem**: Multiple ActionServers must validate the same session_key via Orleans
- **Expiry Handling**: Sessions must expire and be cleaned up to prevent unbounded memory growth
- **Key Reuse**: Same session_key works across zone transitions (ActionServer changes)

### Target State

- HTTP endpoint creates sessions with random 256-bit keys
- Orleans grains store sessions with expiry tracking
- ActionServers validate keys via Orleans before accepting connections
- Expired/revoked sessions immediately become invalid
- Resource cleanup on expiry to prevent memory leaks

---

## Table of Contents

1. [Session Lifecycle Stages](#1-session-lifecycle-stages)
2. [Session Storage Model](#2-session-storage-model)
3. [Session Validation Flow](#3-session-validation-flow)
4. [Expiry and Cleanup](#4-expiry-and-cleanup)
5. [Implementation](#5-implementation)
6. [Integration](#6-integration)
7. [Security Considerations](#7-security-considerations)
8. [Testing Strategy](#8-testing-strategy)
9. [Rollout Plan](#9-rollout-plan)

---

## 1. Session Lifecycle Stages

```
┌─────────────────────────────────────────────────────────────────────┐
│                        SESSION LIFECYCLE                            │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  STAGE 1: CREATION (HTTP Auth)                                     │
│  ├─ Client: POST /api/world/players/register                       │
│  ├─ Silo: Generate random 32-byte SessionKey                       │
│  ├─ Silo: Create PlayerSession(PlayerId, SessionKey, expiresAt)   │
│  ├─ Silo: Store in IPlayerSessionGrain                             │
│  └─ Silo: Return { SessionKey, ActionServer endpoint, PlayerId }   │
│                                                                     │
│  STAGE 2: UDP CONNECTION (DTLS-PSK Handshake)                      │
│  ├─ Client: Connect to ActionServer with SessionKey                │
│  ├─ ActionServer: Extract PlayerId from ClientHello                │
│  ├─ ActionServer: Call IPlayerSessionGrain.ValidateSessionKey()   │
│  ├─ Silo: Compare provided key with stored key (timing-safe)      │
│  ├─ ActionServer: Establish DTLS connection if key matches         │
│  ├─ ActionServer: Create RpcSecurityContext from session           │
│  └─ Client: Ready to send game RPC calls                           │
│                                                                     │
│  STAGE 3: ACTIVE SESSION (Game Execution)                          │
│  ├─ Client: Send encrypted RPC calls (movement, spells, etc.)      │
│  ├─ ActionServer: Deserialize with security context                │
│  ├─ ActionServer: Verify RpcSecurityContext.PlayerId matches       │
│  ├─ Grain: Process game logic with authorization checks            │
│  └─ [Zone Transition: See STAGE 4]                                 │
│                                                                     │
│  STAGE 4: ZONE TRANSITION (Multi-ActionServer)                     │
│  ├─ Client: Detect zone boundary                                   │
│  ├─ Client: Query Silo for new ActionServer endpoint               │
│  ├─ Client: Close connection to old ActionServer                   │
│  ├─ ActionServer(OLD): Detect disconnection, release resources     │
│  ├─ Client: Connect to new ActionServer with SAME SessionKey       │
│  ├─ ActionServer(NEW): Validate SAME SessionKey via Orleans        │
│  ├─ ActionServer(NEW): Session valid across all servers            │
│  └─ Client: Resume game with new ActionServer                      │
│                                                                     │
│  STAGE 5A: LOGOUT (Explicit)                                       │
│  ├─ Client: POST /api/world/players/{playerId}/logout              │
│  ├─ Silo: Call IPlayerSessionGrain.RevokeSession()                │
│  ├─ Silo: Clear session from Orleans grain                         │
│  ├─ ActionServer: Next RPC call fails auth (session gone)          │
│  └─ Client: Disconnect and cleanup                                 │
│                                                                     │
│  STAGE 5B: EXPIRY (Automatic)                                      │
│  ├─ Time elapses (default: 4 hours)                                │
│  ├─ ActionServer: RPC call triggers validation                     │
│  ├─ Silo: Check session.ExpiresAt < DateTime.UtcNow                │
│  ├─ Silo: Return null (session expired)                            │
│  ├─ ActionServer: Treat as invalid session, disconnect client      │
│  └─ [Cleanup: See STAGE 6]                                         │
│                                                                     │
│  STAGE 6: CLEANUP                                                  │
│  ├─ Delete session from IPlayerSessionGrain                        │
│  ├─ Clean up connection state (UDP endpoint → PlayerId mapping)    │
│  ├─ Release any associated resources (memory, file handles)        │
│  ├─ Emit audit event (player session ended)                        │
│  └─ Session no longer validatable                                  │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 2. Session Storage Model

### 2.1 PlayerSession Record (Stored in Orleans)

```csharp
/// <summary>
/// Session state stored in Orleans grain.
/// Key: PlayerId
/// Availability: Global cluster-wide, accessible from any ActionServer
/// </summary>
[GenerateSerializer]
public record PlayerSession
{
    /// <summary>
    /// Unique player identifier. Also grain key.
    /// </summary>
    [Id(0)]
    public required string PlayerId { get; init; }

    /// <summary>
    /// Random 32-byte session key (256 bits).
    /// Generated at authentication time.
    /// Used as PSK for DTLS handshake.
    /// </summary>
    [Id(1)]
    public required byte[] SessionKey { get; init; }

    /// <summary>
    /// Display name (non-security-critical).
    /// </summary>
    [Id(2)]
    public required string PlayerName { get; init; }

    /// <summary>
    /// User role (Guest, User, Admin).
    /// Determines authorization level.
    /// </summary>
    [Id(3)]
    public required UserRole Role { get; init; }

    /// <summary>
    /// When session was created (UTC).
    /// </summary>
    [Id(4)]
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// When session expires (UTC).
    /// Default: Now + 4 hours.
    /// Sessions are invalid after this time.
    /// </summary>
    [Id(5)]
    public required DateTime ExpiresAt { get; init; }

    /// <summary>
    /// Optional: IP address of client when authenticated.
    /// Can be checked for IP-based anomaly detection.
    /// </summary>
    [Id(6)]
    public string? AuthenticatedIp { get; init; }

    /// <summary>
    /// Optional: Last activity timestamp.
    /// Can trigger earlier expiry if idle for too long.
    /// </summary>
    [Id(7)]
    public DateTime? LastActivityAt { get; init; }

    /// <summary>
    /// Session state (Active, Inactive, Revoked).
    /// </summary>
    [Id(8)]
    public SessionState State { get; init; } = SessionState.Active;

    /// <summary>
    /// Reason for revocation (if applicable).
    /// </summary>
    [Id(9)]
    public string? RevocationReason { get; init; }
}

/// <summary>
/// Session lifecycle state.
/// </summary>
public enum SessionState : byte
{
    /// <summary>Session is valid and can be used.</summary>
    Active = 0,

    /// <summary>Session exists but is temporarily inactive (soft revoke).</summary>
    Inactive = 1,

    /// <summary>Session has been explicitly revoked and cannot be used.</summary>
    Revoked = 2,

    /// <summary>Session has expired based on timestamp.</summary>
    Expired = 3
}
```

### 2.2 IPlayerSessionGrain Interface

```csharp
/// <summary>
/// Orleans grain managing session lifecycle for a single player.
/// Key: PlayerId (string)
/// Scoped: Per player
/// Availability: Cluster-wide
/// </summary>
public interface IPlayerSessionGrain : IGrainWithStringKey
{
    /// <summary>
    /// Create a new session (called at HTTP auth time).
    /// </summary>
    Task CreateSession(PlayerSession session, CancellationToken ct = default);

    /// <summary>
    /// Retrieve current session (called at UDP validation time).
    /// Returns null if session expired or doesn't exist.
    /// </summary>
    Task<PlayerSession?> GetSession(CancellationToken ct = default);

    /// <summary>
    /// Validate session key matches stored key (timing-safe comparison).
    /// Called during DTLS handshake to verify PSK.
    /// </summary>
    Task<bool> ValidateSessionKey(byte[] providedKey, CancellationToken ct = default);

    /// <summary>
    /// Check if session is still valid (not expired, not revoked).
    /// </summary>
    Task<bool> IsSessionValid(CancellationToken ct = default);

    /// <summary>
    /// Explicitly revoke session (logout).
    /// </summary>
    Task RevokeSession(string reason = "", CancellationToken ct = default);

    /// <summary>
    /// Update last activity timestamp (for idle timeout tracking).
    /// </summary>
    Task UpdateLastActivityAsync(CancellationToken ct = default);

    /// <summary>
    /// Extend session expiry (if refresh token provided).
    /// </summary>
    Task<bool> ExtendSessionAsync(TimeSpan extension, CancellationToken ct = default);

    /// <summary>
    /// Get session for security context creation.
    /// Used by RPC handler to populate RpcSecurityContext.
    /// </summary>
    Task<PlayerSession?> GetSessionForContext(CancellationToken ct = default);
}
```

### 2.3 Session Grain Implementation

```csharp
/// <summary>
/// Orleans grain implementation for session management.
/// Stores session state in memory (with persistence if needed).
/// </summary>
public class PlayerSessionGrain : Grain, IPlayerSessionGrain
{
    private PlayerSession? _session;
    private readonly ILogger _logger;

    public PlayerSessionGrain(ILogger<PlayerSessionGrain> logger)
    {
        _logger = logger;
    }

    public Task CreateSession(PlayerSession session, CancellationToken ct = default)
    {
        _session = session;

        _logger.LogInformation(
            "Session created for player {PlayerId}: expires at {ExpiresAt}",
            session.PlayerId,
            session.ExpiresAt);

        // Optional: Persist to storage
        // await _statePersistence.SaveSessionAsync(_session);

        return Task.CompletedTask;
    }

    public Task<PlayerSession?> GetSession(CancellationToken ct = default)
    {
        if (_session == null)
            return Task.FromResult<PlayerSession?>(null);

        // Check expiry
        if (DateTime.UtcNow > _session.ExpiresAt)
        {
            _logger.LogInformation(
                "Session expired for player {PlayerId}: {Age} hours old",
                _session.PlayerId,
                (DateTime.UtcNow - _session.CreatedAt).TotalHours);

            _session = null;  // Clear expired session
            return Task.FromResult<PlayerSession?>(null);
        }

        // Check revocation
        if (_session.State == SessionState.Revoked)
        {
            _logger.LogInformation(
                "Session revoked for player {PlayerId}: {Reason}",
                _session.PlayerId,
                _session.RevocationReason ?? "no reason provided");

            return Task.FromResult<PlayerSession?>(null);
        }

        // Check inactive timeout (optional: 30 min idle)
        if (_session.LastActivityAt.HasValue)
        {
            var idleDuration = DateTime.UtcNow - _session.LastActivityAt.Value;
            if (idleDuration > TimeSpan.FromMinutes(30))
            {
                _logger.LogInformation(
                    "Session idle timeout for player {PlayerId}: {IdleMinutes} minutes",
                    _session.PlayerId,
                    idleDuration.TotalMinutes);

                _session = _session with { State = SessionState.Inactive };
                return Task.FromResult<PlayerSession?>(null);
            }
        }

        return Task.FromResult<PlayerSession?>(_session);
    }

    public async Task<bool> ValidateSessionKey(
        byte[] providedKey,
        CancellationToken ct = default)
    {
        var session = await GetSession(ct);
        if (session == null)
            return false;

        // Timing-safe comparison (prevents timing attacks)
        var isValid = CryptographicOperations.FixedTimeEquals(
            session.SessionKey,
            providedKey);

        if (!isValid)
        {
            _logger.LogWarning(
                "Session key validation failed for player {PlayerId}",
                session.PlayerId);
        }

        return isValid;
    }

    public async Task<bool> IsSessionValid(CancellationToken ct = default)
    {
        var session = await GetSession(ct);
        return session != null;
    }

    public Task RevokeSession(string reason = "", CancellationToken ct = default)
    {
        if (_session == null)
            return Task.CompletedTask;

        _logger.LogInformation(
            "Session revoked for player {PlayerId}: {Reason}",
            _session.PlayerId,
            reason);

        _session = _session with
        {
            State = SessionState.Revoked,
            RevocationReason = reason
        };

        // Optional: Persist revocation
        // await _statePersistence.SaveSessionAsync(_session);

        return Task.CompletedTask;
    }

    public Task UpdateLastActivityAsync(CancellationToken ct = default)
    {
        if (_session != null)
        {
            _session = _session with { LastActivityAt = DateTime.UtcNow };
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExtendSessionAsync(
        TimeSpan extension,
        CancellationToken ct = default)
    {
        if (_session == null)
            return Task.FromResult(false);

        var newExpiry = _session.ExpiresAt.Add(extension);

        _logger.LogInformation(
            "Session extended for player {PlayerId}: " +
            "old expiry {OldExpiry} → new expiry {NewExpiry}",
            _session.PlayerId,
            _session.ExpiresAt,
            newExpiry);

        _session = _session with { ExpiresAt = newExpiry };

        return Task.FromResult(true);
    }

    public Task<PlayerSession?> GetSessionForContext(CancellationToken ct = default)
    {
        return GetSession(ct);
    }

    // Cleanup when grain becomes inactive
    public override Task OnDeactivateAsync(
        DeactivationReason reason,
        CancellationToken cancellationToken)
    {
        if (_session != null)
        {
            _logger.LogDebug(
                "Session grain deactivated for player {PlayerId}",
                _session.PlayerId);
        }

        return base.OnDeactivateAsync(reason, cancellationToken);
    }
}
```

---

## 3. Session Validation Flow

### 3.1 HTTP Authentication (Silo)

**Endpoint**: `POST /api/world/players/register`

```csharp
[ApiController]
[Route("api/world/players")]
public class PlayerAuthController : ControllerBase
{
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger _logger;

    /// <summary>
    /// Authenticate player and create session.
    /// Called once per player (via HTTPS).
    /// </summary>
    [HttpPost("register")]
    public async Task<IActionResult> RegisterPlayerAsync(
        [FromBody] PlayerRegistrationRequest request,
        CancellationToken ct)
    {
        // 1. Validate request
        if (string.IsNullOrEmpty(request.Name))
            return BadRequest("Player name required");

        // 2. Create player ID (or use provided)
        var playerId = request.PlayerId ?? Guid.NewGuid().ToString();

        _logger.LogInformation("Player registration: {PlayerId}", playerId);

        try
        {
            // 3. Generate session key (32 random bytes = 256 bits)
            using var rng = new RNGCryptoServiceProvider();
            var sessionKey = new byte[32];
            rng.GetBytes(sessionKey);

            // 4. Create session
            var session = new PlayerSession
            {
                PlayerId = playerId,
                PlayerName = request.Name,
                SessionKey = sessionKey,
                Role = UserRole.Guest,  // Default: guest
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(4),  // 4-hour session
                AuthenticatedIp = HttpContext.Connection.RemoteIpAddress?.ToString()
            };

            // 5. Store in Orleans grain
            var sessionGrain = _grainFactory.GetGrain<IPlayerSessionGrain>(playerId);
            await sessionGrain.CreateSession(session, ct);

            // 6. Assign to ActionServer (existing logic)
            var actionServer = await AssignActionServerAsync(playerId, ct);

            // 7. Return response (with SessionKey in Base64)
            return Ok(new PlayerRegistrationResponse
            {
                PlayerInfo = new PlayerInfo
                {
                    PlayerId = playerId,
                    Name = request.Name
                },
                ActionServer = actionServer,
                SessionKey = Convert.ToBase64String(sessionKey),
                ExpiresIn = (int)(session.ExpiresAt - DateTime.UtcNow).TotalSeconds
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registration failed for {PlayerId}", playerId);
            return StatusCode(500, "Registration failed");
        }
    }

    /// <summary>
    /// Explicit logout endpoint.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]  // Requires authentication
    public async Task<IActionResult> LogoutAsync(
        [FromQuery] string playerId,
        CancellationToken ct)
    {
        _logger.LogInformation("Player logout: {PlayerId}", playerId);

        try
        {
            var sessionGrain = _grainFactory.GetGrain<IPlayerSessionGrain>(playerId);
            await sessionGrain.RevokeSession("User logout", ct);

            return Ok(new { message = "Logged out successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Logout failed for {PlayerId}", playerId);
            return StatusCode(500, "Logout failed");
        }
    }

    private async Task<ActionServerInfo> AssignActionServerAsync(
        string playerId,
        CancellationToken ct)
    {
        // Existing logic to find least-loaded ActionServer
        // Returns { Host, Port, ServerId }
        var server = await _serverManager.GetLeastLoadedServerAsync(ct);
        return new ActionServerInfo
        {
            Host = server.Host,
            Port = server.Port,
            ServerId = server.ServerId
        };
    }
}

[GenerateSerializer]
public record PlayerRegistrationRequest
{
    [Id(0)] public string? PlayerId { get; init; }
    [Id(1)] public required string Name { get; init; }
}

[GenerateSerializer]
public record PlayerRegistrationResponse
{
    [Id(0)] public required PlayerInfo PlayerInfo { get; init; }
    [Id(1)] public required ActionServerInfo ActionServer { get; init; }
    [Id(2)] public required string SessionKey { get; init; }  // Base64
    [Id(3)] public int ExpiresIn { get; init; }  // Seconds
}

[GenerateSerializer]
public record ActionServerInfo
{
    [Id(0)] public required string Host { get; init; }
    [Id(1)] public required int Port { get; init; }
    [Id(2)] public required string ServerId { get; init; }
}
```

### 3.2 UDP DTLS-PSK Handshake (ActionServer)

**Location**: `DtlsPskTransport.ProcessHandshake()` (from PSK-ARCHITECTURE-PLAN.md)

When UDP client initiates DTLS handshake:

```csharp
private async Task ProcessHandshakeAsync(
    DtlsPskSession session,
    ReadOnlyMemory<byte> clientHello,
    CancellationToken ct)
{
    // 1. Extract PlayerId from ClientHello
    // (DTLS-PSK sends PSK identity in ClientHello)
    var playerId = ExtractPlayerIdFromClientHello(clientHello);

    if (string.IsNullOrEmpty(playerId))
    {
        _logger.LogWarning("ClientHello missing PSK identity");
        session.RejectHandshake();
        return;
    }

    // 2. Look up session in Orleans
    var sessionGrain = _grainFactory.GetGrain<IPlayerSessionGrain>(playerId);
    var playerSession = await sessionGrain.GetSession(ct);

    if (playerSession == null)
    {
        _logger.LogWarning(
            "Session not found or expired for player {PlayerId}",
            playerId);

        session.RejectHandshake();
        return;
    }

    // 3. Set PSK for DTLS handshake
    // (BouncyCastle will use this to complete handshake)
    session.SetPsk(playerSession.SessionKey);

    // 4. Verify DTLS handshake (BouncyCastle validates PSK match)
    if (!await session.CompleteHandshakeAsync(ct))
    {
        _logger.LogWarning(
            "DTLS handshake failed for player {PlayerId}",
            playerId);

        session.RejectHandshake();
        return;
    }

    // 5. Create RPC security context
    var context = new RpcSecurityContext
    {
        PlayerId = playerSession.PlayerId,
        PlayerName = playerSession.PlayerName,
        Role = playerSession.Role,
        RequestId = Guid.NewGuid().ToString("N"),
        RemoteEndpoint = session.RemoteEndPoint.ToString(),
        ConnectionId = session.ConnectionId,
        CreatedAt = DateTime.UtcNow,
        AuthenticationMethod = "DTLS-PSK",
        SessionKeyId = GetKeyFingerprint(playerSession.SessionKey)
    };

    session.SetSecurityContext(context);

    _logger.LogInformation(
        "DTLS handshake completed for player {PlayerId} from {Endpoint}",
        playerId,
        session.RemoteEndPoint);

    // 6. Update last activity (activity tracking for idle timeout)
    _ = sessionGrain.UpdateLastActivityAsync(ct);
}
```

---

## 4. Expiry and Cleanup

### 4.1 Session Expiry Check

Sessions are validated on every RPC call. Expiry is checked in `IPlayerSessionGrain.GetSession()`:

```csharp
public Task<PlayerSession?> GetSession(CancellationToken ct = default)
{
    if (_session == null)
        return Task.FromResult<PlayerSession?>(null);

    // Check expiry
    if (DateTime.UtcNow > _session.ExpiresAt)
    {
        _logger.LogInformation(
            "Session expired for player {PlayerId}",
            _session.PlayerId);

        _session = null;  // Clear from memory
        return Task.FromResult<PlayerSession?>(null);
    }

    return Task.FromResult<PlayerSession?>(_session);
}
```

**Benefits of checking at access time**:
- ✅ No background cleanup job needed
- ✅ Immediate expiry (not delayed)
- ✅ Minimal memory overhead
- ✅ Scalable (no cluster-wide cleanup)

### 4.2 Resource Cleanup on Session End

When session ends (expiry, revocation, or idle timeout):

```csharp
public class SessionCleanupService : IHostedService
{
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger _logger;

    /// <summary>
    /// Background job to clean up old session grains.
    /// Runs periodically to remove inactive grains from memory.
    /// </summary>
    public async Task StartAsync(CancellationToken ct)
    {
        // Note: Orleans handles grain deactivation automatically
        // after grain becomes inactive for a period (~60 minutes default).
        // This service can optionally force cleanup for very old sessions.

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(1), ct);

                // Optional: Force deactivate grains older than 5 hours
                // This is cleanup, not critical path
                _logger.LogDebug("Running session grain cleanup");

                // Note: In real implementation, would iterate over grain references
                // This is pseudo-code
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during session cleanup");
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

### 4.3 Connection State Cleanup

When a UDP connection drops, clean up endpoint → PlayerId mapping:

```csharp
public class ConnectionStateManager
{
    private readonly Dictionary<string, string> _connectionToPlayerId = new();
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger _logger;

    /// <summary>
    /// Register new connection.
    /// </summary>
    public void RegisterConnection(string connectionId, string playerId)
    {
        _connectionToPlayerId[connectionId] = playerId;

        _logger.LogDebug(
            "Connection registered: {ConnectionId} → {PlayerId}",
            connectionId,
            playerId);
    }

    /// <summary>
    /// Handle connection drop.
    /// </summary>
    public async Task HandleConnectionDropAsync(
        string connectionId,
        CancellationToken ct)
    {
        if (!_connectionToPlayerId.TryRemove(connectionId, out var playerId))
            return;

        _logger.LogInformation(
            "Connection dropped: {ConnectionId} → {PlayerId}",
            connectionId,
            playerId);

        // Note: Don't revoke session on disconnect
        // Player might reconnect soon (lag, network hiccup)
        // Session expires automatically after inactivity
    }

    /// <summary>
    /// Get PlayerId for connection (for validation).
    /// </summary>
    public string? GetPlayerIdForConnection(string connectionId)
    {
        _connectionToPlayerId.TryGetValue(connectionId, out var playerId);
        return playerId;
    }
}
```

---

## 5. Implementation

### 5.1 PlayerSessionGrain Implementation

**File**: `/granville/samples/Rpc/Shooter.Silo/Grains/PlayerSessionGrain.cs` (NEW)

See Section 2.3 above for full implementation.

### 5.2 PlayerAuthController Implementation

**File**: `/granville/samples/Rpc/Shooter.Silo/Controllers/PlayerAuthController.cs` (NEW)

See Section 3.1 above for full implementation.

### 5.3 Session Models

**File**: `/granville/samples/Rpc/Shooter.Shared/Models/PlayerSession.cs` (NEW)

```csharp
using System;
using Orleans;

namespace Shooter.Shared.Models;

/// <summary>
/// Session state stored in Orleans grain.
/// </summary>
[GenerateSerializer]
public record PlayerSession
{
    [Id(0)] public required string PlayerId { get; init; }
    [Id(1)] public required byte[] SessionKey { get; init; }
    [Id(2)] public required string PlayerName { get; init; }
    [Id(3)] public required UserRole Role { get; init; }
    [Id(4)] public required DateTime CreatedAt { get; init; }
    [Id(5)] public required DateTime ExpiresAt { get; init; }
    [Id(6)] public string? AuthenticatedIp { get; init; }
    [Id(7)] public DateTime? LastActivityAt { get; init; }
    [Id(8)] public SessionState State { get; init; } = SessionState.Active;
    [Id(9)] public string? RevocationReason { get; init; }
}

public enum SessionState : byte
{
    Active = 0,
    Inactive = 1,
    Revoked = 2,
    Expired = 3
}
```

**File**: `/granville/samples/Rpc/Shooter.Shared/Grains/IPlayerSessionGrain.cs` (NEW)

See Section 2.2 above for full interface.

---

## 6. Integration

### 6.1 With PSK-ARCHITECTURE-PLAN

Session storage and validation is the PSK component:

```
HTTP Register → Create Session → Store in Orleans grain
             ↓
UDP Handshake → Validate SessionKey → DTLS handshake → RPC calls
             ↓
RPC Execution → Context from session
```

### 6.2 With RPC-CONTEXT-FLOW-PLAN

Session is source of truth for RpcSecurityContext:

```csharp
// In DtlsPskTransport.ProcessHandshake()
var playerSession = await sessionGrain.GetSession(ct);
var context = new RpcSecurityContext
{
    PlayerId = playerSession.PlayerId,
    Role = playerSession.Role,
    // ...
};
```

### 6.3 With AUTHORIZATION-FILTER-PLAN

Authorization filters verify resource ownership using context PlayerId:

```csharp
// In grain method
var context = RpcSecurityContext.GetCurrentOrThrow();
if (!context.OwnsResource(this.GetPrimaryKeyString()))
{
    throw new RpcAuthorizationException("Ownership mismatch");
}
```

### 6.4 With DDoS-RESOURCE-EXHAUSTION-PLAN

Session is tracked for per-user rate limiting:

```csharp
// In DDoS layer
var context = RpcSecurityContext.Current;
var rateLimiter = _rateLimiters.GetOrCreate(context.PlayerId);
if (!rateLimiter.AllowRequest())
{
    throw new RpcRateLimitException();
}
```

---

## 7. Security Considerations

### 7.1 Session Key Generation

```csharp
// ✅ CORRECT: 32 random bytes
using var rng = new RNGCryptoServiceProvider();
var sessionKey = new byte[32];
rng.GetBytes(sessionKey);

// ❌ WRONG: Weak entropy
var sessionKey = Guid.NewGuid().ToByteArray();  // Only 16 bytes, sequential
```

### 7.2 Session Key Validation (Timing-Safe)

```csharp
// ✅ CORRECT: Timing-safe comparison
return CryptographicOperations.FixedTimeEquals(
    session.SessionKey,
    providedKey);

// ❌ WRONG: Vulnerable to timing attacks
return session.SessionKey.SequenceEqual(providedKey);  // Returns early if mismatch
```

### 7.3 Session Expiry

- **4-hour default**: Reasonable for game sessions
- **Idle timeout**: 30 minutes (optional) prevents long-lived connections
- **No refresh tokens**: Session expires, player re-authenticates
- **No extension via client**: Only server can extend (no protocol yet)

### 7.4 Session Revocation

Revoked sessions cannot be reused:

```csharp
if (_session.State == SessionState.Revoked)
    return null;  // Session invalid
```

### 7.5 Connection Security

- **No endpoint reuse**: Each client → server connection is unique (UDP stateless)
- **No session ID prediction**: 32-byte random key (2^256 possible values)
- **No session ID transmission in responses**: Only returned over HTTPS once
- **No session ID in logs** (unless debug mode): Prevent log exfiltration attacks

---

## 8. Testing Strategy

### 8.1 Unit Tests

**File**: `/granville/test/Rpc.Security.Tests/SessionLifecycleTests.cs` (NEW)

```csharp
public class PlayerSessionGrainTests
{
    [Fact]
    public async Task CreateSession_StoresSessionAsync()
    {
        // Arrange
        var grain = new PlayerSessionGrain(/* logger */);
        var session = new PlayerSession
        {
            PlayerId = "player1",
            SessionKey = new byte[32],
            PlayerName = "Alice",
            Role = UserRole.Guest,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(4)
        };

        // Act
        await grain.CreateSession(session);
        var retrieved = await grain.GetSession();

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("player1", retrieved.PlayerId);
    }

    [Fact]
    public async Task GetSession_ReturnsNullWhenExpiredAsync()
    {
        // Arrange
        var grain = new PlayerSessionGrain(/* logger */);
        var session = new PlayerSession
        {
            PlayerId = "player1",
            SessionKey = new byte[32],
            ExpiresAt = DateTime.UtcNow.AddSeconds(-1)  // Already expired
        };
        await grain.CreateSession(session);

        // Act
        var retrieved = await grain.GetSession();

        // Assert
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task ValidateSessionKey_ComparesTimingSafeAsync()
    {
        // Arrange
        var sessionKey = new byte[32];
        new RNGCryptoServiceProvider().GetBytes(sessionKey);

        var grain = new PlayerSessionGrain(/* logger */);
        var session = new PlayerSession
        {
            PlayerId = "player1",
            SessionKey = sessionKey,
            ExpiresAt = DateTime.UtcNow.AddHours(4)
        };
        await grain.CreateSession(session);

        // Act
        var valid = await grain.ValidateSessionKey(sessionKey);
        var invalid = await grain.ValidateSessionKey(new byte[32]);

        // Assert
        Assert.True(valid);
        Assert.False(invalid);
    }

    [Fact]
    public async Task RevokeSession_PreventsReuseAsync()
    {
        // Arrange
        var grain = new PlayerSessionGrain(/* logger */);
        var session = new PlayerSession
        {
            PlayerId = "player1",
            SessionKey = new byte[32],
            ExpiresAt = DateTime.UtcNow.AddHours(4)
        };
        await grain.CreateSession(session);

        // Act
        await grain.RevokeSession("logout");
        var retrieved = await grain.GetSession();

        // Assert
        Assert.Null(retrieved);
    }
}
```

### 8.2 Integration Tests

Test HTTP auth → Orleans → UDP validation flow:

```csharp
public class SessionLifecycleIntegrationTests : RpcTestBase
{
    [Fact]
    public async Task FullSessionLifecycle_HttpAuthToGamePlayAsync()
    {
        // 1. HTTP registration
        var authResponse = await _httpClient.PostAsJsonAsync(
            "/api/world/players/register",
            new { name = "Alice" });

        var regResponse = await authResponse.Content
            .ReadAsAsync<PlayerRegistrationResponse>();

        Assert.NotNull(regResponse.SessionKey);

        // 2. UDP DTLS handshake
        var sessionKey = Convert.FromBase64String(regResponse.SessionKey);
        var dtlsSession = new DtlsPskClient(regResponse.ActionServer, sessionKey);
        await dtlsSession.HandshakeAsync();

        // 3. Send RPC call
        var moveResult = await dtlsSession.InvokeAsync<MoveResult>(
            "game:player:move",
            new { x = 100, y = 200 });

        Assert.True(moveResult.Success);

        // 4. Logout
        var logoutResponse = await _httpClient.PostAsync(
            $"/api/world/players/{regResponse.PlayerInfo.PlayerId}/logout",
            null);

        Assert.True(logoutResponse.IsSuccessStatusCode);

        // 5. Verify session is revoked
        var moveAfterLogout = await Assert.ThrowsAsync<RpcException>(
            () => dtlsSession.InvokeAsync<MoveResult>(
                "game:player:move",
                new { x = 150, y = 250 }));

        Assert.Contains("session", moveAfterLogout.Message.ToLower());
    }
}
```

### 8.3 Security Tests

Test session expiry and revocation:

```csharp
public class SessionSecurityTests
{
    [Fact]
    public async Task ExpiredSession_CannotBeUsedAsync()
    {
        // Create session that expires immediately
        var expiredSession = new PlayerSession
        {
            PlayerId = "player1",
            SessionKey = new byte[32],
            ExpiresAt = DateTime.UtcNow.AddSeconds(-1)
        };

        var grain = new PlayerSessionGrain(/* logger */);
        await grain.CreateSession(expiredSession);

        // Attempt to validate
        var isValid = await grain.IsSessionValid();

        Assert.False(isValid);
    }

    [Fact]
    public async Task RevokedSession_CannotBeValidatedAsync()
    {
        var grain = new PlayerSessionGrain(/* logger */);
        var session = new PlayerSession
        {
            PlayerId = "player1",
            SessionKey = new byte[32],
            ExpiresAt = DateTime.UtcNow.AddHours(4)
        };
        await grain.CreateSession(session);

        // Revoke session
        await grain.RevokeSession("test revocation");

        // Attempt to validate
        var isValid = await grain.IsSessionValid();

        Assert.False(isValid);
    }
}
```

---

## 9. Rollout Plan

### Phase 1: Core Session Infrastructure (Week 1-2)
- [ ] Implement PlayerSession record
- [ ] Implement IPlayerSessionGrain interface
- [ ] Implement PlayerSessionGrain class
- [ ] Wire up Orleans grain registration
- [ ] Unit tests for session operations

### Phase 2: HTTP Authentication Endpoint (Week 2)
- [ ] Implement PlayerAuthController
- [ ] Generate session keys
- [ ] Handle registration flow
- [ ] Handle logout endpoint
- [ ] Integration tests

### Phase 3: ActionServer Session Validation (Week 3)
- [ ] Implement session validation in DTLS handshake
- [ ] Create RpcSecurityContext from session
- [ ] Test UDP connection establishment
- [ ] Test session key validation

### Phase 4: Session Expiry and Cleanup (Week 3-4)
- [ ] Implement expiry checking
- [ ] Implement idle timeout (optional)
- [ ] Test expired session rejection
- [ ] Test revocation flow

### Phase 5: Integration Testing (Week 4)
- [ ] Full HTTP → UDP flow
- [ ] Zone transitions with session reuse
- [ ] Session timeout scenarios
- [ ] Performance under load

### Phase 6: Production Deployment (Week 5)
- [ ] Deploy to staging
- [ ] Verify session expiry behavior
- [ ] Monitor session grain memory
- [ ] Deploy to production

---

## Summary

**Session Lifecycle** provides:
- ✅ HTTP authentication with random session keys
- ✅ Multi-ActionServer validation via Orleans grains
- ✅ Automatic expiry to prevent long-lived sessions
- ✅ Explicit revocation for logout
- ✅ Timing-safe key comparison
- ✅ Integration with RPC context flow and authorization

**Dependencies**: Requires Phase 1 of PSK-ARCHITECTURE-PLAN, RPC-CONTEXT-FLOW-PLAN, and integration with AUTHORIZATION-FILTER-PLAN
