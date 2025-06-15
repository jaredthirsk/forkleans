# Forkleans RPC Implementation for Shooter Sample

## Overview
This document describes the implementation of Forkleans RPC for all Client-ActionServer communication in the Shooter sample game.

## Key Changes

### 1. Removed Custom UDP Game Server
- **Before**: ActionServers ran a custom UDP game server on ports 9000-9999
- **After**: All game communication now uses Forkleans RPC over LiteNetLib

### 2. Dynamic RPC Port Allocation
- **Implementation**: ActionServers now allocate RPC ports in the range 12000-12999
- **Process**:
  1. On startup, find an available port in the range
  2. Configure Forkleans RPC to listen on that port
  3. Report the RPC port to the Silo during registration

### 3. Client Uses Only Forkleans RPC
- **New Service**: `ForleansRpcGameClientService` handles all game communication
- **Features**:
  - Single UDP connection via LiteNetLib
  - Connects to ActionServer's reported RPC port
  - Implements all game operations via RPC grain calls

### 4. localStorage for Player Name
- **Auto-connect**: If a player name is stored, automatically connect on page load
- **Remember**: Player name saved when connecting
- **Clear**: Name removed when disconnecting

## Architecture

```
┌─────────────┐     Forkleans RPC      ┌──────────────────┐
│   Client    │◄──────────────────────►│  ActionServer    │
│  (Blazor)   │     UDP/LiteNetLib     │  (RPC Server)    │
└─────────────┘                        └──────────────────┘
      │                                         │
      │ HTTP (initial registration)             │ Orleans Client
      ▼                                         ▼
┌─────────────┐                        ┌──────────────────┐
│    Silo     │◄──────────────────────►│  World State     │
│ (REST API)  │     Orleans Cluster    │    (Grains)      │
└─────────────┘                        └──────────────────┘
```

## Port Configuration

| Service | Port Range | Protocol | Purpose |
|---------|------------|----------|---------|
| Silo HTTP | 7071 | HTTP | REST API for registration |
| Silo Orleans | 11111, 30000 | TCP | Orleans clustering |
| ActionServer HTTP | 7072+ | HTTP | Health checks, metrics |
| ActionServer RPC | 12000-12999 | UDP | Forkleans RPC game communication |
| Client | 5000 | HTTP | Blazor web app |

## RPC Grain Implementation

### Interface (`IGameRpcGrain`)
```csharp
public interface IGameRpcGrain : Forkleans.IGrainWithStringKey
{
    [RpcMethod(DeliveryMode = RpcDeliveryMode.Reliable)]
    Task<bool> ConnectPlayer(string playerId);
    
    [RpcMethod(DeliveryMode = RpcDeliveryMode.Reliable)]
    Task DisconnectPlayer(string playerId);
    
    [RpcMethod(DeliveryMode = RpcDeliveryMode.Unreliable)]
    Task<WorldState> GetWorldState();
    
    [RpcMethod(DeliveryMode = RpcDeliveryMode.Unreliable)]
    Task UpdatePlayerInput(string playerId, Vector2 moveDirection, bool isShooting);
}
```

### Benefits
1. **Single Connection**: All communication over one UDP connection
2. **Efficient Protocol**: Binary serialization with Forkleans
3. **Delivery Modes**: Reliable for critical operations, unreliable for frequent updates
4. **Simplified Architecture**: No need for separate HTTP and UDP endpoints

## Testing
1. Start the application: `dotnet run` in Shooter.AppHost
2. Multiple ActionServers will start on different RPC ports
3. Clients automatically connect to the correct RPC port
4. Player names are remembered across sessions

## Future Improvements
1. **Connection Pooling**: Share RPC connections between multiple game sessions
2. **Load Balancing**: Distribute players across ActionServers based on load
3. **Streaming**: Use Forkleans streaming for real-time game updates
4. **Low-Level API**: Expose datagram-level sending for custom protocols