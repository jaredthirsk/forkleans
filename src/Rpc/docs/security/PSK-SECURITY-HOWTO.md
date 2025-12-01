# Granville RPC PSK Security - How-To Guide

**Last Updated**: 2024-11-30
**Package**: `Granville.Rpc.Security`

This guide explains how to use the Pre-Shared Key (PSK) security features in Granville RPC.

## Overview

Granville RPC Security provides transport-layer encryption using AES-256-GCM with Pre-Shared Keys. This protects all UDP traffic between clients and servers.

**Security Flow**:
1. Client authenticates via HTTP (gets session key from Orleans grain)
2. Client connects to ActionServer via UDP
3. Challenge-response handshake establishes encrypted channel
4. All subsequent traffic is AES-256-GCM encrypted

## Installation

Add the `Granville.Rpc.Security` NuGet package to your project:

```xml
<PackageReference Include="Granville.Rpc.Security" />
```

Add the using statement:

```csharp
using Granville.Rpc.Security;
```

## Quick Start

### Option 1: Development Mode (No Security)

For local development, explicitly disable security with a logged warning:

```csharp
// Server (ActionServer)
builder.Host.UseOrleansRpc(rpcBuilder =>
{
    rpcBuilder.UseLiteNetLib();  // or UseRuffles()
    rpcBuilder.UseNoSecurity();  // Logs warning at startup
});

// Client
hostBuilder.UseOrleansRpcClient(rpcBuilder =>
{
    rpcBuilder.ConnectTo(host, port);
    rpcBuilder.UseLiteNetLib();
    rpcBuilder.UseNoSecurity();
});
```

This will log:
```
[SECURITY] Transport security is DISABLED. Traffic will be unencrypted. Do NOT use in production or over untrusted networks.
```

### Option 2: Production Mode (PSK Encryption)

For production, use PSK encryption:

**Server-side** (ActionServer):

```csharp
builder.Host.UseOrleansRpc(rpcBuilder =>
{
    rpcBuilder.UseLiteNetLib();

    rpcBuilder.UsePskEncryption(options =>
    {
        options.IsServer = true;
        options.PskLookup = async (playerId, cancellationToken) =>
        {
            // Look up the session key from Orleans grain
            var grainFactory = builder.Services.BuildServiceProvider()
                .GetRequiredService<Orleans.IGrainFactory>();
            var sessionGrain = grainFactory.GetGrain<IPlayerSessionGrain>(playerId);
            var session = await sessionGrain.GetSessionAsync();
            return session?.GetSessionKeyBytes();
        };
    });
});
```

**Client-side**:

```csharp
// After HTTP registration, you have SessionKey and PlayerId
var sessionKeyBytes = Convert.FromBase64String(sessionKey);

hostBuilder.UseOrleansRpcClient(rpcBuilder =>
{
    rpcBuilder.ConnectTo(host, port);
    rpcBuilder.UseLiteNetLib();

    rpcBuilder.UsePskEncryption(options =>
    {
        options.IsServer = false;
        options.PskIdentity = playerId;
        options.PskKey = sessionKeyBytes;
    });
});
```

## Complete Example: Shooter Game Integration

### Step 1: Create Session Grain Interface

```csharp
// Shooter.Shared/GrainInterfaces/IPlayerSessionGrain.cs
using Orleans;

namespace Shooter.Shared.GrainInterfaces;

public interface IPlayerSessionGrain : IGrainWithStringKey
{
    Task<PlayerSession> CreateSessionAsync(CreateSessionRequest request);
    Task<PlayerSession?> GetSessionAsync();
    Task<bool> ValidateSessionKeyAsync(byte[] providedKey);
    Task RevokeSessionAsync();
}

public record PlayerSession(
    string PlayerId,
    string PlayerName,
    UserRole Role,
    string SessionKey,      // Base64 encoded
    DateTime CreatedAt,
    DateTime ExpiresAt)
{
    public byte[] GetSessionKeyBytes() => Convert.FromBase64String(SessionKey);
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
}

public record CreateSessionRequest(string PlayerName, UserRole Role = UserRole.User);

public enum UserRole : byte { Guest = 0, User = 1, Server = 2, Admin = 3 }
```

### Step 2: Implement Session Grain

```csharp
// Shooter.Silo/Grains/PlayerSessionGrain.cs
using System.Security.Cryptography;
using Orleans;
using Shooter.Shared.GrainInterfaces;

namespace Shooter.Silo.Grains;

public class PlayerSessionGrain : Grain, IPlayerSessionGrain
{
    private PlayerSession? _session;
    private readonly ILogger<PlayerSessionGrain> _logger;

    public PlayerSessionGrain(ILogger<PlayerSessionGrain> logger)
    {
        _logger = logger;
    }

    public Task<PlayerSession> CreateSessionAsync(CreateSessionRequest request)
    {
        var playerId = this.GetPrimaryKeyString();

        // Generate 256-bit random key
        var keyBytes = RandomNumberGenerator.GetBytes(32);
        var sessionKey = Convert.ToBase64String(keyBytes);

        _session = new PlayerSession(
            PlayerId: playerId,
            PlayerName: request.PlayerName,
            Role: request.Role,
            SessionKey: sessionKey,
            CreatedAt: DateTime.UtcNow,
            ExpiresAt: DateTime.UtcNow.AddHours(4)
        );

        _logger.LogInformation("Created session for player {PlayerId}", playerId);
        return Task.FromResult(_session);
    }

    public Task<PlayerSession?> GetSessionAsync()
    {
        if (_session?.IsExpired == true)
        {
            _session = null;
        }
        return Task.FromResult(_session);
    }

    public Task<bool> ValidateSessionKeyAsync(byte[] providedKey)
    {
        if (_session == null || _session.IsExpired)
            return Task.FromResult(false);

        var storedKey = _session.GetSessionKeyBytes();
        return Task.FromResult(
            CryptographicOperations.FixedTimeEquals(storedKey, providedKey));
    }

    public Task RevokeSessionAsync()
    {
        _session = null;
        return Task.CompletedTask;
    }
}
```

### Step 3: HTTP Authentication Endpoint

```csharp
// In your HTTP controller or minimal API
app.MapPost("/api/world/players/register", async (
    PlayerRegistrationRequest request,
    IGrainFactory grainFactory) =>
{
    var playerId = Guid.NewGuid().ToString();

    // Create session with PSK
    var sessionGrain = grainFactory.GetGrain<IPlayerSessionGrain>(playerId);
    var session = await sessionGrain.CreateSessionAsync(new CreateSessionRequest
    {
        PlayerName = request.PlayerName,
        Role = UserRole.User
    });

    return new PlayerRegistrationResponse
    {
        PlayerId = playerId,
        PlayerName = request.PlayerName,
        SessionKey = session.SessionKey,
        SessionExpiresAt = session.ExpiresAt,
        // ... other fields
    };
});
```

### Step 4: ActionServer Configuration

```csharp
// Shooter.ActionServer/Program.cs
using Granville.Rpc.Security;

builder.Host.UseOrleansRpc(rpcBuilder =>
{
    rpcBuilder.ConfigureEndpoint(rpcPort);
    rpcBuilder.UseLiteNetLib();

    // Production: Enable PSK encryption
    rpcBuilder.UsePskEncryption(options =>
    {
        options.IsServer = true;
        options.PskLookup = async (playerId, ct) =>
        {
            var grainFactory = builder.Services.BuildServiceProvider()
                .GetRequiredService<Orleans.IGrainFactory>();
            var sessionGrain = grainFactory.GetGrain<IPlayerSessionGrain>(playerId);
            var session = await sessionGrain.GetSessionAsync();
            return session?.GetSessionKeyBytes();
        };
    });

    rpcBuilder.AddAssemblyContaining<GameGranule>();
});
```

### Step 5: Client Configuration

```csharp
// In your game client service
public async Task<bool> ConnectAsync(string playerName)
{
    // Step 1: HTTP registration
    var response = await _httpClient.PostAsJsonAsync(
        "/api/world/players/register",
        new { PlayerName = playerName });
    var registration = await response.Content.ReadFromJsonAsync<PlayerRegistrationResponse>();

    // Store session info
    _playerId = registration.PlayerId;
    _sessionKey = registration.SessionKey;
    _sessionExpiresAt = registration.SessionExpiresAt;

    // Step 2: Build RPC host with PSK
    var host = new HostBuilder()
        .UseOrleansRpcClient(rpcBuilder =>
        {
            rpcBuilder.ConnectTo(serverHost, serverPort);
            rpcBuilder.UseLiteNetLib();

            if (!string.IsNullOrEmpty(_sessionKey))
            {
                var keyBytes = Convert.FromBase64String(_sessionKey);
                rpcBuilder.UsePskEncryption(options =>
                {
                    options.IsServer = false;
                    options.PskIdentity = _playerId;
                    options.PskKey = keyBytes;
                });
            }
            else
            {
                // Fallback for development
                rpcBuilder.UseNoSecurity();
            }
        })
        .Build();

    await host.StartAsync();
    // ...
}
```

## Configuration Options

### DtlsPskOptions

| Property | Type | Description |
|----------|------|-------------|
| `IsServer` | `bool` | Whether this is server-side (true) or client-side (false) |
| `PskIdentity` | `string?` | Client only: Player ID sent during handshake |
| `PskKey` | `byte[]?` | Client only: Pre-shared key bytes (32 bytes recommended) |
| `PskLookup` | `Func<string, CancellationToken, Task<byte[]?>>?` | Server only: Callback to look up PSK by player ID |
| `HandshakeTimeout` | `TimeSpan` | Maximum time for handshake (default: 10 seconds) |

## Security Architecture

### Handshake Protocol

```
Client                              Server
  |                                   |
  |--- HELLO (identity) ------------>|
  |                                   | Look up PSK via Orleans grain
  |<-- CHALLENGE (16 random bytes) ---|
  |                                   |
  | Compute HMAC-SHA256(challenge, PSK)
  |--- RESPONSE (HMAC) ------------->|
  |                                   | Verify HMAC
  |                                   | Derive session keys via HKDF
  |<-- ENCRYPTED (ack) --------------|
  |                                   |
  | Session established, all traffic encrypted
  |<=== ENCRYPTED DATA ==============|
```

### Cryptographic Details

- **Key Exchange**: Challenge-response with HMAC-SHA256
- **Key Derivation**: HKDF-SHA256 with challenge as salt
- **Encryption**: AES-256-GCM with 16-byte authentication tag
- **Nonces**: 12-byte nonces (8-byte sequence + 4-byte random)
- **Replay Protection**: Sequence number tracking with sliding window

### Key Derivation

From the PSK and challenge, two session keys are derived:

```csharp
var serverToClientKey = HKDF.DeriveKey(
    HashAlgorithmName.SHA256,
    psk,
    32,  // 256 bits
    challenge,
    Encoding.UTF8.GetBytes("server_to_client"));

var clientToServerKey = HKDF.DeriveKey(
    HashAlgorithmName.SHA256,
    psk,
    32,
    challenge,
    Encoding.UTF8.GetBytes("client_to_server"));
```

## Troubleshooting

### Common Issues

**1. "No transport factory configured"**

You must configure a transport (UseLiteNetLib or UseRuffles) BEFORE calling UsePskEncryption or UseNoSecurity:

```csharp
// Correct order:
rpcBuilder.UseLiteNetLib();       // First: transport
rpcBuilder.UsePskEncryption(...); // Second: security

// Wrong order (will throw):
rpcBuilder.UsePskEncryption(...); // Error: no transport
rpcBuilder.UseLiteNetLib();
```

**2. Handshake Failures**

Check that:
- Client is sending the correct PlayerId
- Session grain has a valid, non-expired session
- PSK bytes match on both sides (no encoding issues)
- Clocks are reasonably synchronized (for session expiry)

Enable debug logging:
```csharp
builder.Logging.AddFilter("Granville.Rpc.Security", LogLevel.Debug);
```

**3. Session Expired**

Sessions expire after 4 hours by default. The client should:
- Check `SessionExpiresAt` before connecting
- Re-authenticate via HTTP if expired
- Handle `RpcStatus.Unauthenticated` errors

### Security Best Practices

1. **Always use PSK encryption in production** - UseNoSecurity is only for local development
2. **Generate cryptographically random keys** - Use `RandomNumberGenerator.GetBytes(32)`
3. **Use secure key storage** - Don't log session keys, store securely in Orleans grains
4. **Implement session expiry** - Default 4 hours, configurable per your needs
5. **Monitor failed handshakes** - Could indicate attack attempts
6. **Use constant-time comparison** - Use `CryptographicOperations.FixedTimeEquals` for key validation

## API Reference

### Extension Methods

```csharp
// Server builder
public static IRpcServerBuilder UsePskEncryption(
    this IRpcServerBuilder builder,
    Action<DtlsPskOptions> configureOptions);

public static IRpcServerBuilder UseNoSecurity(
    this IRpcServerBuilder builder);

// Client builder
public static IRpcClientBuilder UsePskEncryption(
    this IRpcClientBuilder builder,
    Action<DtlsPskOptions> configureOptions);

public static IRpcClientBuilder UseNoSecurity(
    this IRpcClientBuilder builder);
```

### Message Types (Internal)

| Type | Value | Description |
|------|-------|-------------|
| MSG_HELLO | 0x01 | Client sends identity |
| MSG_CHALLENGE | 0x02 | Server sends random challenge |
| MSG_RESPONSE | 0x03 | Client sends HMAC response |
| MSG_ENCRYPTED | 0x10 | Encrypted application data |

## Related Documentation

- [PSK Architecture Plan](roadmap/PSK-ARCHITECTURE-PLAN.md) - Detailed design document
- [Security Roadmap](roadmap/SECURITY-RECAP.md) - Implementation progress
- [Threat Model](THREAT-MODEL.md) - Security risk analysis
