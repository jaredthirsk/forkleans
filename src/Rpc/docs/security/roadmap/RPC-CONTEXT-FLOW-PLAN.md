# Granville RPC Security Context Flow Plan

**Document Version**: 1.0
**Created**: 2025-11-30
**Status**: Planning
**Priority**: HIGH (Foundation for Auth & Logging)

## Executive Summary

This document specifies how security context (user identity, roles, authorization state) propagates through Granville RPC call chains. Proper context flow is essential for enforcing authorization policies, logging security events, and preventing privilege escalation.

### Current State

- **Challenge**: Security context must flow from network inbound → RPC handlers → Orleans grain calls → outbound RPC calls
- **Async Complexity**: .NET async/await makes context tracking difficult
- **Multiple Auth Sources**: HTTPS (Silo) and UDP (ActionServers) require different context sources
- **Documentation**: Context flow is not currently formalized

### Target State

- AsyncLocal<T> context propagates through entire RPC call chain
- Context available in handlers, grains, and nested RPC calls
- Automatic enforcement at handler entry points
- Clean context lifecycle (setup, use, cleanup, no leaks)

---

## Table of Contents

1. [Architecture](#1-architecture)
2. [Context Capture Points](#2-context-capture-points)
3. [Context Propagation](#3-context-propagation)
4. [Implementation](#4-implementation)
5. [Integration](#5-integration)
6. [Testing Strategy](#6-testing-strategy)
7. [Rollout Plan](#7-rollout-plan)

---

## 1. Architecture

### 1.1 Context Flow Diagram

```
┌────────────────────────────────────────────────────────────────────┐
│                    GRANVILLE RPC CALL CHAIN                        │
├────────────────────────────────────────────────────────────────────┤
│                                                                    │
│  1. Network Inbound (UDP/TCP)                                     │
│     │                                                              │
│     ├─→ Frame Deserialization                                     │
│     │   └─→ RPC Method Identification                             │
│     │       ├─→ PlayerId extraction (from PSK handshake)          │
│     │       └─→ SessionKey validation (Orleans grain)             │
│     │                                                              │
│     ├─→ ┌──────────────────────────────────────────────┐          │
│     │   │ RpcSecurityContext.SetCurrent()              │          │
│     │   │ - PlayerId                                   │          │
│     │   │ - UserRole                                   │          │
│     │   │ - ConnectionId (UDP endpoint)                │          │
│     │   │ - Timestamp                                  │          │
│     │   │ - RequestId                                  │          │
│     │   └──────────────────────────────────────────────┘          │
│     │       (AsyncLocal<RpcSecurityContext>)                      │
│     │                                                              │
│     ├─→ RPC Handler Invocation                                    │
│     │   │                                                          │
│     │   ├─→ [Authorize] Attribute Enforcement                     │
│     │   │   └─→ RpcSecurityContext.Current retrieved              │
│     │   │       └─→ Role/Permission check                         │
│     │   │           ├─→ Allowed: Handler executes                 │
│     │   │           └─→ Denied: RpcAuthorizationException         │
│     │   │                                                          │
│     │   └─→ Handler Code Executes                                 │
│     │       │                                                      │
│     │       ├─→ Access RpcSecurityContext.Current for PlayerId   │
│     │       ├─→ Call Orleans Grains                               │
│     │       │   └─→ Context propagates via AsyncLocal             │
│     │       │       ├─→ IPlayerGrain.GetState()                  │
│     │       │       │   └─→ AuthorizationFilter intercepts        │
│     │       │       │       └─→ Verifies PlayerId owns resource   │
│     │       │       │           (context.PlayerId == grain.key)   │
│     │       │       │                                              │
│     │       │       └─→ Nested Grain Calls                        │
│     │       │           └─→ Context still available               │
│     │       │                                                      │
│     │       └─→ Return Result                                     │
│     │                                                              │
│     └─→ Response Serialization                                    │
│         └─→ Response.Serialize(result)                            │
│             └─→ ┌──────────────────────────────────────────┐      │
│                 │ Security Event Logging                   │      │
│                 │ - Handler name                           │      │
│                 │ - PlayerId (from context)                │      │
│                 │ - Elapsed time                           │      │
│                 │ - Success/Failure                        │      │
│                 │ - RequestId for correlation              │      │
│                 └──────────────────────────────────────────┘      │
│                                                                    │
└────────────────────────────────────────────────────────────────────┘
```

### 1.2 RpcSecurityContext Structure

```csharp
/// <summary>
/// Security context propagated through RPC call chain via AsyncLocal<T>.
/// Enables handlers, grains, and logging to access user identity without passing through parameters.
/// </summary>
[GenerateSerializer]
public record RpcSecurityContext
{
    /// <summary>
    /// Player ID from authenticated session. Unique per client connection.
    /// CRITICAL: All resource ownership checks must verify this value.
    /// </summary>
    public required string PlayerId { get; init; }

    /// <summary>
    /// Player display name (from session token).
    /// </summary>
    public string PlayerName { get; init; } = string.Empty;

    /// <summary>
    /// User role in the system (Guest, User, Admin, Server).
    /// Determines which handlers/methods are accessible.
    /// </summary>
    public required UserRole Role { get; init; }

    /// <summary>
    /// Unique request identifier for distributed tracing.
    /// Correlates logs across multiple services.
    /// Format: "req-{Guid}" or similar
    /// </summary>
    public required string RequestId { get; init; }

    /// <summary>
    /// UDP endpoint of the client (IPAddress:Port).
    /// Used for logging and potential IP-based blocking.
    /// </summary>
    public string? RemoteEndpoint { get; init; }

    /// <summary>
    /// Unique connection identifier (e.g., session ID or connection hash).
    /// Persists across multiple RPC calls from same client.
    /// </summary>
    public string? ConnectionId { get; init; }

    /// <summary>
    /// UTC timestamp when context was created (RPC handler entry).
    /// Used to calculate request latency.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// Authentication method used (HTTPS, DTLS-PSK, Anonymous, etc.).
    /// </summary>
    public string? AuthenticationMethod { get; init; }

    /// <summary>
    /// Session key type (if from PSK).
    /// Used for re-authentication validation.
    /// </summary>
    public string? SessionKeyId { get; init; }

    // ========== ACCESS METHODS ==========

    /// <summary>
    /// Retrieve current RPC security context (if in RPC call chain).
    /// Returns null if called outside RPC handler.
    /// </summary>
    public static RpcSecurityContext? Current =>
        _current.Value;

    /// <summary>
    /// Set the current security context.
    /// CRITICAL: Call within try-finally to ensure cleanup.
    /// </summary>
    public static IDisposable SetCurrent(RpcSecurityContext context)
    {
        var previous = _current.Value;
        _current.Value = context;
        return new ContextCleanup(previous);
    }

    /// <summary>
    /// Ensure a valid context exists. Throw if not.
    /// Use in handlers that require authentication.
    /// </summary>
    public static RpcSecurityContext GetCurrentOrThrow() =>
        _current.Value ?? throw new RpcAuthenticationRequiredException(
            "RPC security context not found. This handler requires authentication.");

    /// <summary>
    /// Verify user is authenticated (not Anonymous role).
    /// </summary>
    public bool IsAuthenticated => Role != UserRole.Anonymous;

    /// <summary>
    /// Verify user has minimum required role.
    /// </summary>
    public bool HasRole(UserRole minimumRole) =>
        (int)Role >= (int)minimumRole;

    /// <summary>
    /// Verify user is authorized to access a specific grain (data ownership).
    /// </summary>
    public bool OwnsResource(string resourcePlayerId) =>
        PlayerId == resourcePlayerId;

    // ========== PRIVATE IMPLEMENTATION ==========

    private static readonly AsyncLocal<RpcSecurityContext?> _current = new();

    private class ContextCleanup : IDisposable
    {
        private readonly RpcSecurityContext? _previous;
        private bool _disposed;

        public ContextCleanup(RpcSecurityContext? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _current.Value = _previous;
            _disposed = true;
        }
    }
}

/// <summary>
/// User role hierarchy for authorization.
/// Higher values have more permissions.
/// </summary>
public enum UserRole : byte
{
    /// <summary>
    /// No authentication required. Limited access.
    /// Allowed: public game info, version endpoints
    /// </summary>
    Anonymous = 0,

    /// <summary>
    /// Authenticated guest player. Normal gameplay.
    /// Allowed: movement, chat, spell casting
    /// </summary>
    Guest = 1,

    /// <summary>
    /// Registered user (future: account system).
    /// Allowed: all guest + premium features
    /// </summary>
    User = 2,

    /// <summary>
    /// Server-to-server communication (ActionServer ↔ ActionServer).
    /// Allowed: internal state sync, player migration
    /// </summary>
    Server = 3,

    /// <summary>
    /// Administrative access (dev only).
    /// Allowed: all + admin commands
    /// </summary>
    Admin = 4
}
```

---

## 2. Context Capture Points

### 2.1 UDP Inbound (ActionServer)

**Location**: `/src/Rpc/Orleans.Rpc.Server/RpcServerTransport.cs` (or packet handler)

When UDP packet arrives from client:

```csharp
private async Task HandleIncomingPacketAsync(
    byte[] packet,
    IPEndPoint remoteEndpoint,
    CancellationToken ct)
{
    // 1. Deserialize with security validation
    var message = await DeserializePacketAsync(packet, ct);

    // 2. Extract PlayerId from PSK handshake/connection state
    var connectionId = GetOrCreateConnectionId(remoteEndpoint);
    var (playerId, role) = await ValidateSessionKeyAsync(connectionId, ct);

    // 3. Generate RequestId for distributed tracing
    var requestId = $"req-{Guid.NewGuid():N}";

    // 4. Create security context
    var context = new RpcSecurityContext
    {
        PlayerId = playerId,
        Role = role,
        RequestId = requestId,
        RemoteEndpoint = remoteEndpoint.ToString(),
        ConnectionId = connectionId,
        CreatedAt = DateTime.UtcNow,
        AuthenticationMethod = "DTLS-PSK",
        SessionKeyId = GetSessionKeyId(connectionId)
    };

    // 5. Set context and process RPC call
    using (RpcSecurityContext.SetCurrent(context))
    {
        try
        {
            await InvokeRpcHandlerAsync(message, ct);
        }
        catch (RpcAuthorizationException ex)
        {
            // Log authorization failure with context
            _securityLogger.LogAuthorizationFailure(context, ex);
            // Send error response to client
        }
        finally
        {
            // Context automatically cleaned up by IDisposable
        }
    }
}
```

**Key Details**:
- ✅ Context created BEFORE handler invocation
- ✅ PlayerId extracted from validated session (Orleans grain lookup)
- ✅ RequestId created for request tracing
- ✅ Context cleaned up in finally block
- ✅ Endpoint captured for logging/blocking

### 2.2 HTTPS Inbound (Silo HTTP API)

**Location**: `/granville/samples/Rpc/Shooter.Silo/Controllers/PlayerAuthController.cs` (existing)

When HTTP authentication request arrives:

```csharp
[HttpPost("register")]
public async Task<PlayerRegistrationResponse> RegisterPlayer(
    [FromBody] PlayerRegistrationRequest request,
    CancellationToken ct)
{
    // 1. Authenticate guest user (or validate credentials)
    var playerId = request.PlayerId ?? Guid.NewGuid().ToString();

    // 2. Generate session key
    var sessionKey = RandomNumberGenerator.GetBytes(32);

    // 3. Store in Orleans grain (validates playerId ownership later)
    var sessionGrain = _grainFactory.GetGrain<IPlayerSessionGrain>(playerId);
    await sessionGrain.CreateSession(new PlayerSession
    {
        SessionKey = sessionKey,
        PlayerId = playerId,
        PlayerName = request.Name,
        Role = UserRole.Guest,  // Or User/Admin based on auth
        CreatedAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.AddHours(4)
    }, ct);

    // 4. Return session key + ActionServer endpoint
    return new PlayerRegistrationResponse
    {
        PlayerInfo = new PlayerInfo { PlayerId = playerId, Name = request.Name },
        ActionServer = await AssignActionServerAsync(playerId, ct),
        SessionKey = Convert.ToBase64String(sessionKey)
    };
}
```

**Note**: HTTP authentication doesn't use RpcSecurityContext (it uses ASP.NET Core identity). Context is created when client connects via UDP to ActionServer.

### 2.3 Grain Entry Points

**Location**: Orleans grain methods (e.g., `IPlayerGrain.MoveAsync()`)

When RPC handler calls Orleans grain:

```csharp
[RpcHandler("game:player:move")]
public async Task<MoveResult> MoveAsync(int newX, int newY, CancellationToken ct)
{
    // RpcSecurityContext.Current is available here (via AsyncLocal)
    var context = RpcSecurityContext.GetCurrentOrThrow();

    // Get the player grain using PlayerId from context
    var playerGrain = _grainFactory.GetGrain<IPlayerGrain>(context.PlayerId);

    // Call grain method
    // Authorization intercepts this call and verifies ownership
    var result = await playerGrain.MoveAsync(newX, newY, ct);

    return result;
}
```

**How Context Flows to Grain**:
- RPC handler has context (from AsyncLocal)
- Handler calls grain method
- Grain entry point runs in same async context
- Grain's authorization filter intercepts
- Filter accesses RpcSecurityContext.Current
- Filter verifies grain.PlayerId == context.PlayerId

### 2.4 Nested Grain Calls

**Location**: Grain calling another grain

```csharp
public class PlayerGrain : Grain, IPlayerGrain
{
    public async Task<bool> TakeDamageAsync(int damage, string damagerId, CancellationToken ct)
    {
        // Context still available (AsyncLocal)
        var context = RpcSecurityContext.Current;
        _logger.LogInformation(
            "[{RequestId}] Player {PlayerId} taking {Damage} damage",
            context?.RequestId ?? "unknown",
            this.GetPrimaryKeyString(),
            damage);

        // Nested grain call (call another grain)
        var damagerGrain = GrainFactory.GetGrain<IPlayerGrain>(damagerId);

        // Context flows through this call too
        var canDealDamage = await damagerGrain.CanDealDamageToAsync(
            this.GetPrimaryKeyString(), ct);

        if (!canDealDamage)
            return false;

        // Apply damage...
        return true;
    }
}
```

---

## 3. Context Propagation

### 3.1 AsyncLocal<T> Mechanics

**Why AsyncLocal<T>?**

| Mechanism | Thread-Local | AsyncLocal | Context | Best For |
|-----------|--------------|-----------|---------|----------|
| ThreadLocal | ✅ Thread bound | ❌ No async | ❌ No | Single-threaded code |
| AsyncLocal | ⚠️ Thread leak | ✅ Async-aware | ❌ Partial | RPC contexts |
| ExecutionContext | ⚠️ Expensive | ✅ Full flow | ✅ Yes | Thread, async, Tasks |
| Context Propagation | ❌ No | ❌ No | ✅ Perfect | Distributed systems |

**Why AsyncLocal for Granville RPC**:
- ✅ Works with async/await naturally
- ✅ Scoped to logical async call chain
- ✅ No manual propagation needed
- ✅ Automatic cleanup (no leaks)
- ❌ Limited to local machine (can't flow over network)

```csharp
// How AsyncLocal flows through async operations:

public async Task MainHandler()
{
    var context = new RpcSecurityContext { PlayerId = "player1", ... };

    using (RpcSecurityContext.SetCurrent(context))
    {
        // Context set in AsyncLocal
        Console.WriteLine(RpcSecurityContext.Current.PlayerId);  // "player1"

        // Call async method
        await SomeAsyncMethod();
    }
}

public async Task SomeAsyncMethod()
{
    // Context still available (same logical async chain)
    Console.WriteLine(RpcSecurityContext.Current?.PlayerId);  // "player1"

    // Nested async
    await AnotherAsyncMethod();
}

public async Task AnotherAsyncMethod()
{
    // Context STILL available
    Console.WriteLine(RpcSecurityContext.Current?.PlayerId);  // "player1"
}

// After using() block exits:
Console.WriteLine(RpcSecurityContext.Current);  // null (cleaned up)
```

### 3.2 Context Flow Through Task Boundaries

**Correct Pattern**:

```csharp
// ✅ CORRECT: Use async/await (preserves AsyncLocal)
public async Task GoodHandler()
{
    var context = new RpcSecurityContext { PlayerId = "player1", ... };
    using (RpcSecurityContext.SetCurrent(context))
    {
        // Direct await: AsyncLocal preserved
        await _grainFactory.GetGrain<IPlayerGrain>("player1")
            .MoveAsync(x, y, ct);

        // ConfigureAwait doesn't break AsyncLocal in modern .NET
        var state = await GetStateAsync().ConfigureAwait(false);
    }
}

// ❌ WRONG: Fire-and-forget tasks lose context
public async Task BadHandler()
{
    var context = new RpcSecurityContext { PlayerId = "player1", ... };
    using (RpcSecurityContext.SetCurrent(context))
    {
        // Dangerous: Task.Run creates new logical async chain
        var task = Task.Run(async () =>
        {
            // Context is NULL here!
            var ctx = RpcSecurityContext.Current;  // null
        });

        await task;
    }
}

// ⚠️ CAREFUL: Task.WhenAll preserves context for direct tasks
public async Task DelicateHandler()
{
    var context = new RpcSecurityContext { PlayerId = "player1", ... };
    using (RpcSecurityContext.SetCurrent(context))
    {
        // ✅ Context flows through Task.WhenAll
        var tasks = new[]
        {
            Task1_Async(),  // Context available
            Task2_Async(),  // Context available
        };

        await Task.WhenAll(tasks);
    }
}
```

**Key Rule**: If you use `async/await`, context flows. If you use `Task.Run`, `new Thread`, or thread pools without awaiting, context is lost.

### 3.3 Request-Scoped State

Some context needs to be request-scoped but not per-handler. Use a companion RequestContext:

```csharp
/// <summary>
/// Per-request state that accumulates during RPC execution.
/// Used for metrics, audit trails, decision tracking.
/// </summary>
public class RpcRequestContext
{
    /// <summary>
    /// Performance metrics accumulated during request.
    /// </summary>
    public Dictionary<string, long> Metrics { get; } = new();

    /// <summary>
    /// Audit trail of authorization checks performed.
    /// </summary>
    public List<AuthorizationCheckLog> AuthorizationChecks { get; } = new();

    /// <summary>
    /// List of resources accessed (for audit trail).
    /// </summary>
    public List<string> ResourcesAccessed { get; } = new();

    /// <summary>
    /// Current request security context.
    /// </summary>
    public RpcSecurityContext? SecurityContext { get; set; }

    /// <summary>
    /// Retrieve or create current request context.
    /// </summary>
    public static RpcRequestContext Current
    {
        get
        {
            if (_current.Value == null)
            {
                _current.Value = new RpcRequestContext();
            }
            return _current.Value;
        }
    }

    private static readonly AsyncLocal<RpcRequestContext?> _current = new();

    public static IDisposable SetCurrent(RpcRequestContext context)
    {
        var previous = _current.Value;
        _current.Value = context;
        return new ContextCleanup(previous);
    }

    public void RecordMetric(string name, long value)
    {
        Metrics[name] = value;
    }

    public void RecordAuthorizationCheck(string resource, bool allowed, string reason)
    {
        AuthorizationChecks.Add(new AuthorizationCheckLog
        {
            Resource = resource,
            Allowed = allowed,
            Reason = reason,
            Timestamp = DateTime.UtcNow
        });
    }

    private class ContextCleanup : IDisposable
    {
        private readonly RpcRequestContext? _previous;
        private bool _disposed;

        public ContextCleanup(RpcRequestContext? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            _current.Value = _previous;
            _disposed = true;
        }
    }
}

public record AuthorizationCheckLog
{
    public required string Resource { get; init; }
    public required bool Allowed { get; init; }
    public required string Reason { get; init; }
    public required DateTime Timestamp { get; init; }
}
```

---

## 4. Implementation

### 4.1 RpcSecurityContext - Core Implementation

**File**: `/src/Rpc/Orleans.Rpc.Security/Context/RpcSecurityContext.cs` (NEW)

```csharp
using System;
using System.Diagnostics;
using System.Threading;
using Orleans;

namespace Granville.Rpc.Security.Context;

/// <summary>
/// Security context propagated through RPC call chains via AsyncLocal.
/// Provides user identity, role, and request tracking for authorization and logging.
/// </summary>
[GenerateSerializer]
public record RpcSecurityContext
{
    /// <summary>
    /// Unique player identifier from authenticated session.
    /// </summary>
    [Id(0)]
    public required string PlayerId { get; init; }

    /// <summary>
    /// Player display name.
    /// </summary>
    [Id(1)]
    public string PlayerName { get; init; } = string.Empty;

    /// <summary>
    /// User role for authorization.
    /// </summary>
    [Id(2)]
    public required UserRole Role { get; init; }

    /// <summary>
    /// Unique request identifier for tracing.
    /// </summary>
    [Id(3)]
    public required string RequestId { get; init; }

    /// <summary>
    /// Remote endpoint (IP:port) of the client.
    /// </summary>
    [Id(4)]
    public string? RemoteEndpoint { get; init; }

    /// <summary>
    /// Connection identifier (e.g., session ID hash).
    /// </summary>
    [Id(5)]
    public string? ConnectionId { get; init; }

    /// <summary>
    /// When this context was created (handler entry).
    /// </summary>
    [Id(6)]
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// How user was authenticated.
    /// </summary>
    [Id(7)]
    public string? AuthenticationMethod { get; init; }

    /// <summary>
    /// Session key identifier (for reauth validation).
    /// </summary>
    [Id(8)]
    public string? SessionKeyId { get; init; }

    // === ACCESS METHODS ===

    /// <summary>
    /// Current security context or null if outside RPC call chain.
    /// </summary>
    public static RpcSecurityContext? Current => _current.Value;

    /// <summary>
    /// Set security context. Dispose to restore previous context.
    /// </summary>
    public static IDisposable SetCurrent(RpcSecurityContext context)
    {
        var previous = _current.Value;
        _current.Value = context;
        return new ContextCleanup(previous);
    }

    /// <summary>
    /// Get current context or throw if not authenticated.
    /// </summary>
    public static RpcSecurityContext GetCurrentOrThrow() =>
        _current.Value ?? throw new RpcAuthenticationRequiredException(
            "No RPC security context. This handler requires authentication.");

    /// <summary>
    /// Check if user is authenticated.
    /// </summary>
    public bool IsAuthenticated => Role != UserRole.Anonymous;

    /// <summary>
    /// Check if user has minimum required role.
    /// </summary>
    public bool HasRole(UserRole minimumRole) =>
        (int)Role >= (int)minimumRole;

    /// <summary>
    /// Check if user owns this resource (PlayerId match).
    /// </summary>
    public bool OwnsResource(string resourcePlayerId) =>
        PlayerId == resourcePlayerId;

    /// <summary>
    /// Elapsed time since context creation.
    /// </summary>
    public TimeSpan Elapsed => DateTime.UtcNow - CreatedAt;

    // === PRIVATE ===

    private static readonly AsyncLocal<RpcSecurityContext?> _current = new();

    private class ContextCleanup : IDisposable
    {
        private readonly RpcSecurityContext? _previous;
        private bool _disposed;

        public ContextCleanup(RpcSecurityContext? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            _current.Value = _previous;
            _disposed = true;
        }
    }
}

/// <summary>
/// User role hierarchy. Higher values = more permissions.
/// </summary>
public enum UserRole : byte
{
    /// <summary>No authentication. Limited access.</summary>
    Anonymous = 0,

    /// <summary>Guest player. Normal gameplay.</summary>
    Guest = 1,

    /// <summary>Registered user (future).</summary>
    User = 2,

    /// <summary>Server-to-server (ActionServer ↔ ActionServer).</summary>
    Server = 3,

    /// <summary>Administrative (dev only).</summary>
    Admin = 4
}
```

### 4.2 Context Capture in RPC Handler

**File**: `/src/Rpc/Orleans.Rpc.Server/RpcServerHandler.cs` (MODIFY)

```csharp
public class RpcServerHandler
{
    private readonly IGrainFactory _grainFactory;
    private readonly IPlayerSessionGrain _sessionValidator;
    private readonly IRpcSecurityLogger _securityLogger;
    private readonly ILogger _logger;

    /// <summary>
    /// Process incoming RPC message with security context.
    /// </summary>
    public async Task<RpcResponse> HandleMessageAsync(
        RpcRequest request,
        IPEndPoint remoteEndpoint,
        CancellationToken ct)
    {
        // 1. Extract PlayerId and validate session
        var (playerId, role) = await ValidateSessionAsync(
            request.ConnectionId,
            remoteEndpoint,
            ct);

        if (playerId == null)
        {
            return new RpcResponse
            {
                RequestId = request.RequestId,
                Error = "Authentication failed"
            };
        }

        // 2. Create security context
        var context = new RpcSecurityContext
        {
            PlayerId = playerId,
            PlayerName = GetPlayerName(playerId),  // From session
            Role = role,
            RequestId = request.RequestId,
            RemoteEndpoint = remoteEndpoint.ToString(),
            ConnectionId = request.ConnectionId,
            CreatedAt = DateTime.UtcNow,
            AuthenticationMethod = "DTLS-PSK"
        };

        // 3. Execute handler with context
        using (RpcSecurityContext.SetCurrent(context))
        using (RpcRequestContext.SetCurrent(new RpcRequestContext()))
        {
            try
            {
                _logger.LogInformation(
                    "[{RequestId}] RPC handler starting: {Handler}",
                    context.RequestId,
                    request.HandlerName);

                // Invoke the actual RPC handler
                var result = await InvokeHandlerAsync(request, ct);

                // Log success
                _logger.LogInformation(
                    "[{RequestId}] RPC handler completed: {Handler} ({Elapsed}ms)",
                    context.RequestId,
                    request.HandlerName,
                    context.Elapsed.TotalMilliseconds);

                return result;
            }
            catch (RpcAuthorizationException authEx)
            {
                _securityLogger.LogAuthorizationFailure(context, authEx, request.HandlerName);
                return new RpcResponse
                {
                    RequestId = request.RequestId,
                    Error = "Authorization denied"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[{RequestId}] RPC handler error: {Handler}",
                    context.RequestId,
                    request.HandlerName);

                return new RpcResponse
                {
                    RequestId = request.RequestId,
                    Error = "Internal error"
                };
            }
        }
    }

    private async Task<(string? PlayerId, UserRole Role)> ValidateSessionAsync(
        string connectionId,
        IPEndPoint remoteEndpoint,
        CancellationToken ct)
    {
        try
        {
            // Look up session in Orleans
            var playerId = GetPlayerIdFromConnection(connectionId);
            var sessionGrain = _grainFactory.GetGrain<IPlayerSessionGrain>(playerId);
            var session = await sessionGrain.GetSession();

            if (session == null || session.ExpiresAt < DateTime.UtcNow)
            {
                return (null, UserRole.Anonymous);
            }

            return (session.PlayerId, session.Role);
        }
        catch
        {
            return (null, UserRole.Anonymous);
        }
    }
}
```

### 4.3 Context Propagation in Grains

**File**: Example grain using context (e.g., `/granville/samples/Rpc/Shooter.Silo/Grains/PlayerGrain.cs`)

```csharp
public class PlayerGrain : Grain, IPlayerGrain
{
    private readonly ILogger _logger;

    public PlayerGrain(ILogger<PlayerGrain> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Move player to new location.
    /// Context ensures only the player themselves can move themselves.
    /// </summary>
    public async Task<MoveResult> MoveAsync(int x, int y, CancellationToken ct)
    {
        // Context from RPC handler is available here
        var context = RpcSecurityContext.Current;
        var playerId = this.GetPrimaryKeyString();

        // Verify ownership
        if (context?.PlayerId != playerId)
        {
            throw new RpcAuthorizationException(
                $"Player {context?.PlayerId} cannot move player {playerId}");
        }

        _logger.LogInformation(
            "[{RequestId}] Player {PlayerId} moving to ({X}, {Y})",
            context?.RequestId,
            playerId,
            x, y);

        // Actual move logic...
        var newState = new PlayerState
        {
            PlayerId = playerId,
            X = x,
            Y = y,
            Timestamp = DateTime.UtcNow
        };

        // Nested grain call - context flows through
        var zoneGrain = GrainFactory.GetGrain<IZoneGrain>(GetZoneId(x, y));
        await zoneGrain.AddPlayerAsync(playerId, newState, ct);

        return new MoveResult { Success = true, NewState = newState };
    }
}
```

---

## 5. Integration

### 5.1 With Authorization Filters

Context is used by authorization filters (from AUTHORIZATION-FILTER-PLAN.md):

```csharp
public class RpcAuthorizationFilter : IRpcInvokeFilter
{
    public async Task OnInvokeAsync(
        IInvokeContext context,
        Func<IInvokeContext, Task> next)
    {
        var securityContext = RpcSecurityContext.Current;

        if (securityContext == null)
        {
            throw new RpcAuthenticationRequiredException();
        }

        // Get target grain's authorization requirements
        var grain = context.Grain;
        var method = context.Method;
        var auth = GetAuthorizationAttribute(method);

        // Check authorization
        if (!auth.IsAuthorized(securityContext))
        {
            throw new RpcAuthorizationException(
                $"User {securityContext.PlayerId} not authorized for {method.Name}");
        }

        // Proceed with invocation
        await next(context);
    }
}
```

### 5.2 With Security Logging

Context is used by security event logger (from SECURITY-LOGGING-PLAN.md):

```csharp
public class RpcSecurityEventLogger : IRpcSecurityLogger
{
    public void LogHandlerInvocation(string handlerName)
    {
        var context = RpcSecurityContext.Current;

        _logger.LogInformation(
            "[{RequestId}] INVOKE handler={Handler} player={PlayerId} role={Role}",
            context?.RequestId ?? "unknown",
            handlerName,
            context?.PlayerId ?? "anonymous",
            context?.Role ?? UserRole.Anonymous);
    }

    public void LogAuthorizationFailure(
        string handlerName,
        string reason)
    {
        var context = RpcSecurityContext.Current;

        _logger.LogWarning(
            "[{RequestId}] AUTHZ_DENIED handler={Handler} " +
            "player={PlayerId} reason={Reason}",
            context?.RequestId ?? "unknown",
            handlerName,
            context?.PlayerId ?? "anonymous",
            reason);
    }

    public void LogDataAccess(string resourceId)
    {
        var context = RpcSecurityContext.Current;
        RpcRequestContext.Current.ResourcesAccessed.Add(resourceId);

        _logger.LogDebug(
            "[{RequestId}] ACCESS resource={ResourceId} player={PlayerId}",
            context?.RequestId ?? "unknown",
            resourceId,
            context?.PlayerId ?? "anonymous");
    }
}
```

### 5.3 With Session Lifecycle

Context bridges session state to RPC execution (from SESSION-LIFECYCLE-PLAN.md):

```csharp
public class SessionLifecycleManager
{
    /// <summary>
    /// When session established, create initial context.
    /// </summary>
    public RpcSecurityContext CreateContextFromSession(PlayerSession session)
    {
        return new RpcSecurityContext
        {
            PlayerId = session.PlayerId,
            PlayerName = session.PlayerName,
            Role = session.Role,
            RequestId = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
            AuthenticationMethod = "DTLS-PSK",
            SessionKeyId = GetKeyId(session.SessionKey)
        };
    }

    /// <summary>
    /// When session expires, context is automatically invalid.
    /// </summary>
    public async Task ValidateContextAsync(RpcSecurityContext context)
    {
        var session = await _grainFactory
            .GetGrain<IPlayerSessionGrain>(context.PlayerId)
            .GetSession();

        if (session == null || session.ExpiresAt < DateTime.UtcNow)
        {
            throw new RpcSessionExpiredException(context.PlayerId);
        }
    }
}
```

---

## 6. Testing Strategy

### 6.1 Unit Tests

**File**: `/granville/test/Rpc.Security.Tests/ContextFlowTests.cs` (NEW)

```csharp
public class RpcSecurityContextTests
{
    [Fact]
    public void Current_WhenNotSet_ReturnsNull()
    {
        // Arrange & Act
        var context = RpcSecurityContext.Current;

        // Assert
        Assert.Null(context);
    }

    [Fact]
    public void SetCurrent_FlowsContextThroughAsyncChain()
    {
        // Arrange
        var original = new RpcSecurityContext
        {
            PlayerId = "player1",
            Role = UserRole.Guest,
            RequestId = "req-1"
        };

        // Act & Assert
        using (RpcSecurityContext.SetCurrent(original))
        {
            Assert.NotNull(RpcSecurityContext.Current);
            Assert.Equal("player1", RpcSecurityContext.Current.PlayerId);

            // Async call should preserve context
            var task = VerifyContextInAsyncMethodAsync();
            task.Wait();
        }

        // Context cleared after Dispose
        Assert.Null(RpcSecurityContext.Current);
    }

    private async Task VerifyContextInAsyncMethodAsync()
    {
        // This should see the context
        Assert.NotNull(RpcSecurityContext.Current);
        Assert.Equal("player1", RpcSecurityContext.Current.PlayerId);

        await Task.Delay(10);

        // Context still available
        Assert.NotNull(RpcSecurityContext.Current);
    }

    [Fact]
    public void OwnsResource_VerifiesPlayerId()
    {
        // Arrange
        var context = new RpcSecurityContext
        {
            PlayerId = "player1",
            Role = UserRole.Guest,
            RequestId = "req-1"
        };

        // Act & Assert
        Assert.True(context.OwnsResource("player1"));
        Assert.False(context.OwnsResource("player2"));
    }

    [Fact]
    public void HasRole_VerifiesRoleHierarchy()
    {
        // Arrange
        var guestContext = new RpcSecurityContext
        {
            PlayerId = "player1",
            Role = UserRole.Guest,
            RequestId = "req-1"
        };

        var adminContext = new RpcSecurityContext
        {
            PlayerId = "admin1",
            Role = UserRole.Admin,
            RequestId = "req-2"
        };

        // Act & Assert
        Assert.True(guestContext.HasRole(UserRole.Guest));
        Assert.False(guestContext.HasRole(UserRole.User));
        Assert.True(adminContext.HasRole(UserRole.Guest));  // Admin >= Guest
    }
}
```

### 6.2 Integration Tests

**File**: `/granville/test/Rpc.Integration.Tests/ContextPropagationTests.cs` (NEW)

Test that context flows through RPC handler → grain → nested grain:

```csharp
public class ContextPropagationIntegrationTests : RpcTestBase
{
    [Fact]
    public async Task ContextFlowsFromHandlerToGrainAsync()
    {
        // Arrange
        var playerId = "player1";
        var context = new RpcSecurityContext
        {
            PlayerId = playerId,
            Role = UserRole.Guest,
            RequestId = "req-1"
        };

        // Act: Invoke handler with context
        RpcSecurityContext? contextInGrain = null;
        using (RpcSecurityContext.SetCurrent(context))
        {
            var grain = GrainFactory.GetGrain<ITestGrain>(playerId);
            await grain.CaptureContextAsync();
        }

        // Assert (would need a way to retrieve captured context)
        // This is tricky in real Orleans - need test doubles
    }
}
```

### 6.3 Security Tests

Test context cleanup prevents leaks:

```csharp
public class ContextLeakTests
{
    [Fact]
    public async Task ContextCleanedUpAfterExceptionAsync()
    {
        // Arrange
        var context = new RpcSecurityContext
        {
            PlayerId = "player1",
            Role = UserRole.Guest,
            RequestId = "req-1"
        };

        // Act
        try
        {
            using (RpcSecurityContext.SetCurrent(context))
            {
                throw new InvalidOperationException("Test error");
            }
        }
        catch { }

        await Task.Delay(10);

        // Assert: Context is cleaned up
        Assert.Null(RpcSecurityContext.Current);
    }
}
```

---

## 7. Rollout Plan

### Phase 1: Core Implementation (Week 1)
- [ ] Implement RpcSecurityContext class
- [ ] Add AsyncLocal context propagation
- [ ] Create context cleanup mechanism
- [ ] Unit tests for context operations

### Phase 2: Handler Integration (Week 2)
- [ ] Modify RPC handler to capture context
- [ ] Extract PlayerId from session
- [ ] Wire up to RpcSecurityContext
- [ ] Test context flows to grains

### Phase 3: Grain Integration (Week 2-3)
- [ ] Add context usage to test grains
- [ ] Verify authorization filters see context
- [ ] Test nested grain calls

### Phase 4: Logging Integration (Week 3)
- [ ] Wire context to security logging
- [ ] Log RequestId for tracing
- [ ] Test request correlation

### Phase 5: Production Readiness (Week 4)
- [ ] Performance testing
- [ ] Memory leak detection (AsyncLocal)
- [ ] Documentation
- [ ] Deploy to dev environment

---

## Summary

**RpcSecurityContext** provides:
- ✅ Per-request user identity propagation
- ✅ Async-safe (AsyncLocal<T>)
- ✅ Automatic cleanup (no leaks)
- ✅ Integration with authorization filters
- ✅ Support for distributed tracing (RequestId)
- ✅ Thread-safe across async call chains

**Dependencies**: Implemented in Phase 1 of PSK-ARCHITECTURE-PLAN + AUTHORIZATION-FILTER-PLAN + SESSION-LIFECYCLE-PLAN
