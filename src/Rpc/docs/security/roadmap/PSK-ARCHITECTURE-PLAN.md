# Granville RPC Pluggable Security Implementation Plan

## Overview

Implement pluggable security for Granville RPC using **Pre-Shared Key (PSK)** architecture:
1. **None** - No security (development only)
2. **PSK-AES** - Direct AES-GCM encryption with session keys (custom, fast)
3. **DTLS-PSK** - Standard DTLS 1.2 with pre-shared keys via BouncyCastle.NET (recommended)

**Key Insight**: Authentication via HTTP → Orleans Silo → session_key distribution → DTLS-PSK encryption → **zero per-packet overhead**

## Architecture (How Shooter Actually Works)

```
┌─────────────── AUTHENTICATION (HTTPS) ────────────────┐
│                                                        │
│  Player (Blazor)                                      │
│      ↓ POST /api/world/players/register               │
│  Orleans Silo HTTP API                                │
│      ├─ Guest auth (zero friction for demo)          │
│      ├─ Generate session_key (32 random bytes)        │
│      ├─ Store in IPlayerSessionGrain                  │
│      └─ Return { session_key, player_id, server:port} │
│                                                        │
└────────────────────────────────────────────────────────┘

┌───────────── UDP GAME COMMUNICATION ──────────────────┐
│                                                        │
│  Player UDP Client (Granville RPC)                    │
│      ↓ DTLS-PSK handshake with session_key           │
│  ActionServer (Granville RPC)                         │
│      ├─ Validate session_key via IPlayerSessionGrain  │
│      ├─ DTLS handshake (~45-150ms, one-time)         │
│      ├─ Establish AES-GCM encrypted channel           │
│      └─ All game packets encrypted                    │
│                                                        │
│  [Player moves to different zone]                     │
│      ↓ Same session_key, different ActionServer       │
│  Another ActionServer                                 │
│      ├─ Validates SAME session_key via Orleans        │
│      └─ New DTLS handshake with same PSK              │
│                                                        │
└────────────────────────────────────────────────────────┘
```

**Benefits**:
- ✅ **Zero per-packet overhead** (no tokens after handshake)
- ✅ **Standard DTLS protocol** (well-tested, proven)
- ✅ **Works across zone transitions** (same key, all ActionServers validate via Orleans)
- ✅ **Guest-friendly** (HTTP auth can be zero-friction)
- ✅ **Encryption + Authentication** in one protocol

## Components to Implement

### 1. Silo HTTP API (Authentication Endpoint)

**File**: `/granville/samples/Rpc/Shooter.Silo/Controllers/PlayerAuthController.cs` (NEW)

```csharp
[ApiController]
[Route("api/world/players")]
public class PlayerAuthController : ControllerBase
{
    [HttpPost("register")]
    public async Task<PlayerRegistrationResponse> RegisterPlayer(
        [FromBody] PlayerRegistrationRequest request)
    {
        // Guest authentication (zero friction)
        string playerId = request.PlayerId ?? Guid.NewGuid().ToString();

        // Generate session key (32 random bytes)
        var sessionKey = RandomNumberGenerator.GetBytes(32);

        // Store in Orleans grain
        var sessionGrain = _grainFactory.GetGrain<IPlayerSessionGrain>(playerId);
        await sessionGrain.CreateSession(new PlayerSession
        {
            SessionKey = sessionKey,
            PlayerId = playerId,
            PlayerName = request.Name,
            Role = UserRole.Guest, // or User, Admin based on auth
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(4)
        });

        // Assign to ActionServer (existing logic)
        var actionServer = await AssignActionServer(playerId);

        return new PlayerRegistrationResponse
        {
            PlayerInfo = new PlayerInfo { PlayerId = playerId, Name = request.Name },
            ActionServer = actionServer,
            SessionKey = Convert.ToBase64String(sessionKey) // Return to client
        };
    }
}
```

### 2. Orleans Player Session Grain

**File**: `/granville/samples/Rpc/Shooter.Shared/Grains/IPlayerSessionGrain.cs` (NEW)

```csharp
public interface IPlayerSessionGrain : IGrainWithStringKey
{
    Task CreateSession(PlayerSession session);
    Task<PlayerSession?> GetSession();
    Task<bool> ValidateSessionKey(byte[] providedKey);
    Task RevokeSession();
}

[GenerateSerializer]
public record PlayerSession
{
    public byte[] SessionKey { get; init; }
    public string PlayerId { get; init; }
    public string PlayerName { get; init; }
    public UserRole Role { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
}

public enum UserRole : byte
{
    Guest = 0,
    User = 1,
    Server = 2,
    Admin = 3
}
```

**Implementation**: `/granville/samples/Rpc/Shooter.Silo/Grains/PlayerSessionGrain.cs` (NEW)

```csharp
public class PlayerSessionGrain : Grain, IPlayerSessionGrain
{
    private PlayerSession? _session;

    public Task CreateSession(PlayerSession session)
    {
        _session = session;
        return Task.CompletedTask;
    }

    public Task<PlayerSession?> GetSession()
    {
        if (_session != null && DateTime.UtcNow > _session.ExpiresAt)
        {
            _session = null; // Expired
        }
        return Task.FromResult(_session);
    }

    public async Task<bool> ValidateSessionKey(byte[] providedKey)
    {
        var session = await GetSession();
        if (session == null) return false;

        return CryptographicOperations.FixedTimeEquals(
            session.SessionKey,
            providedKey);
    }

    public Task RevokeSession()
    {
        _session = null;
        return Task.CompletedTask;
    }
}
```

### 3. ActionServer DTLS-PSK Transport

**File**: `/src/Rpc/Orleans.Rpc.Security/Transport/DtlsPskTransport.cs` (NEW)

```csharp
/// <summary>
/// DTLS 1.2 transport using Pre-Shared Keys from Orleans.
/// Wraps any IRpcTransport with encrypted channel.
/// </summary>
public class DtlsPskTransport : IRpcTransport
{
    private readonly IRpcTransport _innerTransport;
    private readonly IGrainFactory _grainFactory;
    private readonly ConcurrentDictionary<string, DtlsPskSession> _sessions = new();

    public event EventHandler<RpcDataReceivedEventArgs> DataReceived;

    public DtlsPskTransport(IRpcTransport innerTransport, IGrainFactory grainFactory)
    {
        _innerTransport = innerTransport;
        _grainFactory = grainFactory;
        _innerTransport.DataReceived += OnInnerDataReceived;
    }

    private async void OnInnerDataReceived(object sender, RpcDataReceivedEventArgs e)
    {
        var session = GetOrCreateSession(e.ConnectionId, e.RemoteEndPoint);

        if (!session.IsHandshakeComplete)
        {
            await ProcessHandshake(session, e.Data);
            return;
        }

        // Decrypt packet
        var decrypted = session.Decrypt(e.Data);
        DataReceived?.Invoke(this, new RpcDataReceivedEventArgs(
            e.RemoteEndPoint, decrypted, e.ConnectionId));
    }

    private async Task ProcessHandshake(DtlsPskSession session, ReadOnlyMemory<byte> data)
    {
        // DTLS handshake packet
        session.ProcessHandshakePacket(data);

        if (session.NeedsPskLookup)
        {
            // Extract player ID from ClientHello
            var playerId = ExtractPlayerIdFromClientHello(data);

            // Validate session key via Orleans grain
            var sessionGrain = _grainFactory.GetGrain<IPlayerSessionGrain>(playerId);
            var playerSession = await sessionGrain.GetSession();

            if (playerSession != null)
            {
                session.SetPsk(playerSession.SessionKey);
                session.SetUserContext(new RpcUserContext
                {
                    PlayerId = playerSession.PlayerId,
                    PlayerName = playerSession.PlayerName,
                    Role = playerSession.Role
                });
            }
            else
            {
                session.RejectHandshake();
            }
        }
    }

    public async Task SendAsync(IPEndPoint remote, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        var session = _sessions.Values.FirstOrDefault(s =>
            s.RemoteEndPoint.Equals(remote));

        if (session?.IsHandshakeComplete == true)
        {
            var encrypted = session.Encrypt(data);
            await _innerTransport.SendAsync(remote, encrypted, ct);
        }
        else
        {
            // During handshake, pass through
            await _innerTransport.SendAsync(remote, data, ct);
        }
    }
}
```

### 4. BouncyCastle DTLS-PSK Engine

**File**: `/src/Rpc/Orleans.Rpc.Security/Transport/BouncyCastle/DtlsPskSession.cs` (NEW)

```csharp
public class DtlsPskSession
{
    private readonly DtlsServerProtocol _dtlsServer;
    private readonly PskTls12Server _tlsServer;
    private DtlsTransport _dtlsTransport;
    private byte[]? _psk;
    private bool _handshakeComplete;

    public IPEndPoint RemoteEndPoint { get; }
    public bool IsHandshakeComplete => _handshakeComplete;
    public bool NeedsPskLookup { get; private set; }
    public RpcUserContext UserContext { get; private set; }

    public DtlsPskSession(IPEndPoint remoteEndPoint)
    {
        RemoteEndPoint = remoteEndPoint;
        _tlsServer = new PskTls12Server();
        _dtlsServer = new DtlsServerProtocol();
    }

    public void ProcessHandshakePacket(ReadOnlyMemory<byte> data)
    {
        // Feed data to BouncyCastle DTLS state machine
        // This is complex - BouncyCastle handles the state machine

        if (data.Span[0] == 0x16) // DTLS Handshake
        {
            if (data.Span[13] == 0x01) // ClientHello
            {
                NeedsPskLookup = true;
            }
        }

        // Process via BouncyCastle
        // ... (BC boilerplate)
    }

    public void SetPsk(byte[] psk)
    {
        _psk = psk;
        _tlsServer.SetPsk(psk);
        NeedsPskLookup = false;
    }

    public ReadOnlyMemory<byte> Encrypt(ReadOnlyMemory<byte> plaintext)
    {
        // Use DTLS record layer (AES-GCM)
        return _dtlsTransport.Send(plaintext);
    }

    public ReadOnlyMemory<byte> Decrypt(ReadOnlyMemory<byte> ciphertext)
    {
        // Use DTLS record layer
        return _dtlsTransport.Receive(ciphertext);
    }
}
```

### 5. Client-Side DTLS-PSK

**File**: `/granville/samples/Rpc/Shooter.Client.Common/GranvilleRpcGameClientService.cs` (MODIFY)

```csharp
private async Task<bool> ConnectAsync(string playerName)
{
    // 1. HTTP authentication
    var response = await RegisterWithHttpAsync(playerName);
    var sessionKey = Convert.FromBase64String(response.SessionKey);

    // 2. Create RPC client with DTLS-PSK
    var builder = new HostBuilder()
        .UseOrleansRpcClient(rpc =>
        {
            rpc.ConnectTo(response.ActionServer.Endpoint);
            rpc.UseLiteNetLib();

            // NEW: Enable DTLS-PSK with session key
            rpc.UseDtlsPsk(options =>
            {
                options.PskIdentity = response.PlayerInfo.PlayerId;
                options.PskKey = sessionKey;
            });
        });

    _rpcHost = builder.Build();
    await _rpcHost.StartAsync();

    _rpcClient = _rpcHost.Services.GetRequiredService<IRpcClient>();

    // 3. Get grain reference (now over encrypted DTLS)
    _gameGrain = _rpcClient.GetGrain<IGameGranule>(response.ActionServer.ServerId);

    return true;
}
```

## Configuration API

### Server Configuration

```csharp
// Mode 1: No Security
builder.Host.UseOrleansRpc(rpc => rpc
    .UseLiteNetLib()
    .UseNoSecurity() // No encryption
);

// Mode 2: DTLS-PSK (Recommended)
builder.Host.UseOrleansRpc(rpc => rpc
    .UseLiteNetLib()
    .UseDtlsPsk(options =>
    {
        options.PskLookup = async (playerId) =>
        {
            var grain = grainFactory.GetGrain<IPlayerSessionGrain>(playerId);
            var session = await grain.GetSession();
            return session?.SessionKey;
        };
    })
);

// Mode 3: Custom PSK (Faster, simpler)
builder.Host.UseOrleansRpc(rpc => rpc
    .UseLiteNetLib()
    .UsePskEncryption(options =>
    {
        options.PskLookup = /* same as above */;
        options.EncryptionAlgorithm = "AES-256-GCM";
    })
);
```

## Implementation Phases

### Phase 1: HTTP Authentication & Session Grains (Week 1)
**Goal**: HTTP guest auth + Orleans session storage

**Tasks**:
1. Create `IPlayerSessionGrain` interface and implementation
2. Create `PlayerAuthController` in Shooter.Silo
3. Modify HTTP `/api/world/players/register` to generate session_key
4. Return session_key in `PlayerRegistrationResponse`
5. Unit tests for session grain

**Success**: Guest users get session_key via HTTP

### Phase 2: DTLS-PSK Transport Layer (Week 2-4)
**Goal**: Encrypted UDP with BouncyCastle DTLS-PSK

**Tasks**:
1. Add BouncyCastle.Cryptography NuGet package
2. Implement `DtlsPskTransport` (decorator pattern)
3. Implement `DtlsPskSession` with BouncyCastle integration
4. Implement PSK lookup via Orleans grain call
5. Handle DTLS handshake state machine
6. Performance benchmarks

**Success**: Encrypted UDP communication with session key validation

### Phase 3: Client Integration (Week 4-5)
**Goal**: Client uses session_key from HTTP for DTLS-PSK

**Tasks**:
1. Modify `GranvilleRpcGameClientService.ConnectAsync`
2. Store session_key from HTTP response
3. Configure DTLS-PSK client with session_key
4. Handle DTLS handshake errors
5. Integration tests

**Success**: Shooter client connects with DTLS-PSK encryption

### Phase 4: Zone Transitions (Week 5-6)
**Goal**: Same session_key works across ActionServers

**Tasks**:
1. Test zone transitions with DTLS-PSK
2. Verify session_key reuse
3. Handle edge cases (expired sessions, revoked keys)
4. Performance testing

**Success**: Seamless encrypted zone transitions

### Phase 5: Alternative Mode (Week 6-7)
**Goal**: Custom PSK-AES for comparison

**Tasks**:
1. Implement `PskAesTransport` (simpler than DTLS)
2. Custom 2-packet challenge-response handshake
3. AES-256-GCM encryption
4. Performance comparison vs DTLS

**Success**: Two encryption modes available

## Performance Targets

| Metric | Target | Notes |
|--------|--------|-------|
| DTLS Handshake | < 150ms | One-time per connection |
| Session key validation | < 5ms | Orleans grain call |
| Encrypt/decrypt per packet | < 0.3ms | AES-GCM with hardware acceleration |
| Total overhead | < 1ms | P99 for game packets |
| Zone transition | < 200ms | Includes new DTLS handshake |

## Security Considerations

1. **Session Key Security**: 32 bytes random (256-bit), stored in Orleans grain memory
2. **Key Rotation**: Sessions expire after 4 hours, new key on re-auth
3. **Replay Protection**: DTLS sequence numbers prevent replay attacks
4. **Forward Secrecy**: Limited (PSK reuse), but acceptable for game sessions
5. **Orleans Grain Access**: Only ActionServers can call `IPlayerSessionGrain` (trust internal cluster)

## Critical Files

### Files to Create

1. `/granville/samples/Rpc/Shooter.Silo/Controllers/PlayerAuthController.cs` - HTTP auth endpoint
2. `/granville/samples/Rpc/Shooter.Shared/Grains/IPlayerSessionGrain.cs` - Session interface
3. `/granville/samples/Rpc/Shooter.Silo/Grains/PlayerSessionGrain.cs` - Session implementation
4. `/src/Rpc/Orleans.Rpc.Security/Transport/DtlsPskTransport.cs` - DTLS-PSK wrapper
5. `/src/Rpc/Orleans.Rpc.Security/Transport/BouncyCastle/DtlsPskSession.cs` - BC integration
6. `/src/Rpc/Orleans.Rpc.Security/Configuration/SecurityExtensions.cs` - Builder extensions

### Files to Modify

1. `/granville/samples/Rpc/Shooter.Client.Common/GranvilleRpcGameClientService.cs` - Use session_key for DTLS
2. `/granville/samples/Rpc/Shooter.Shared/Models/PlayerRegistrationResponse.cs` - Add SessionKey field
3. `/granville/samples/Rpc/Shooter.ActionServer/Program.cs` - Enable DTLS-PSK transport

## Success Criteria

- [ ] Guest users authenticate via HTTP
- [ ] Orleans grain stores and validates session keys
- [ ] DTLS-PSK handshake completes in < 150ms
- [ ] All game packets encrypted with AES-GCM
- [ ] Zero per-packet overhead (no tokens)
- [ ] Same session_key works across zone transitions
- [ ] Three security modes: None, PSK-AES, DTLS-PSK
- [ ] Production-ready with comprehensive tests
