# Complete Forkleans RPC Implementation for Shooter Sample

## Overview
This document summarizes the complete implementation of Forkleans RPC for the Shooter sample game, including all fixes and optimizations.

## Key Features Implemented

### 1. **Forkleans RPC for All Communication**
- Removed custom UDP game server (was on ports 9000-9999)
- All game communication now uses Forkleans RPC over LiteNetLib
- Single UDP connection per client for all operations

### 2. **Dynamic Port Allocation**
- **HTTP Ports**: Dynamically assigned by Aspire
- **RPC Ports**: 12000-12008 (one per ActionServer instance)
- No port conflicts when running multiple instances

### 3. **localStorage Player Name**
- Player names are remembered across sessions
- Auto-connects on page load if name exists
- Clears on disconnect

## Architecture

```
Client (Blazor)          ActionServer (RPC)           Silo (Orleans)
     │                         │                           │
     ├──── HTTP Register ──────┼───────────────────────────►
     │                         │                           │
     ◄──── ActionServer Info ──┼───────────────────────────┤
     │      (with RPC port)    │                           │
     │                         │                           │
     ├──── RPC Connect ────────►                           │
     │     (UDP/LiteNetLib)    │                           │
     │                         │                           │
     ├──── RPC Game Calls ─────►                           │
     │     - ConnectPlayer     │                           │
     │     - UpdateInput       ├──── Orleans Client ──────►
     │     - GetWorldState     │     (State queries)       │
     │     - DisconnectPlayer  │                           │
```

## Implementation Details

### Port Allocation Strategy

1. **Environment Variable (RPC_PORT)**
   - Highest priority
   - Set by Aspire for each instance

2. **Instance ID Based**
   - Uses ASPIRE_INSTANCE_ID if available
   - Port = 12000 + instance_id

3. **Dynamic Discovery**
   - Fallback with random delay
   - Scans for available port

### Client Connection Flow

1. **Registration**
   - HTTP POST to Silo's REST API
   - Receives ActionServerInfo with RPC port

2. **Hostname Resolution**
   - Resolves "localhost" to IP address
   - Required for RPC client connection

3. **RPC Connection**
   - Creates Host with UseOrleansRpcClient
   - Connects to ActionServer's RPC endpoint
   - Uses LiteNetLib UDP transport

### RPC Grain Implementation

```csharp
public interface IGameRpcGrain : Forkleans.IGrainWithStringKey
{
    [RpcMethod(DeliveryMode = RpcDeliveryMode.Reliable)]
    Task<bool> ConnectPlayer(string playerId);
    
    [RpcMethod(DeliveryMode = RpcDeliveryMode.Unreliable)]
    Task<WorldState> GetWorldState();
    
    [RpcMethod(DeliveryMode = RpcDeliveryMode.Unreliable)]
    Task UpdatePlayerInput(string playerId, Vector2 moveDirection, bool isShooting);
}
```

## Configuration Files

### Shooter.AppHost/Program.cs
```csharp
// Create 9 ActionServer instances
for (int i = 0; i < 9; i++)
{
    var rpcPort = 12000 + i;
    builder.AddProject<Projects.Shooter_ActionServer>($"shooter-actionserver-{i}")
        .WithEnvironment("RPC_PORT", rpcPort.ToString())
        .WithEnvironment("ASPIRE_INSTANCE_ID", i.ToString())
        .WithReference(silo)
        .WaitFor(silo);
}
```

### Shooter.ActionServer/Program.cs
```csharp
// Dynamic port assignment under Aspire
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPIRE_INSTANCE_ID")))
{
    builder.Configuration["urls"] = null;
    builder.WebHost.UseUrls(); // Dynamic ports
}

// RPC server configuration
builder.Host.UseOrleansRpc(rpcBuilder =>
{
    rpcBuilder.ConfigureEndpoint(rpcPort);
    rpcBuilder.UseLiteNetLib();
    rpcBuilder.AddAssemblyContaining<GameRpcGrain>()
             .AddAssemblyContaining<IGameRpcGrain>();
});
```

## Troubleshooting

### Port Conflicts
- Check Aspire dashboard for actual port assignments
- Verify no other processes on ports 12000-12008
- ActionServers log their RPC port on startup

### Connection Issues
- Verify hostname resolution in client logs
- Check firewall for UDP ports 12000-12008
- Ensure ActionServer registered with Silo

### Performance
- Monitor RPC round-trip times
- Check for packet loss on UDP
- Consider reliable vs unreliable delivery modes

## Future Enhancements

1. **Connection Pooling**: Share RPC connections between game sessions
2. **Load Balancing**: Distribute players based on server load
3. **Streaming**: Use Forkleans streaming for continuous updates
4. **Observability**: Add metrics for RPC performance monitoring