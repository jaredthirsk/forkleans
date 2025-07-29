# RPC Client Multiplexer Architecture

## Problem Statement

The current RpcClient implementation is designed for single-server scenarios, where a client connects to one RPC server at a time. However, real-world distributed applications often require:

1. **Multi-zone architecture**: Different servers handle different geographical zones or shards
2. **Service partitioning**: Different services/grains may be hosted on different servers
3. **Dynamic server selection**: Clients need to switch between servers based on context
4. **Connection reuse**: Maintaining persistent connections to multiple servers

### Current Limitations

- `RpcClient` maintains a single connection to one server
- No built-in routing logic for grain location
- Clients must manually manage multiple RpcClient instances
- Connection lifecycle is tightly coupled to application logic
- Zone transitions cause connection drops and recreation

## Solution: RpcClientMultiplexer

The RpcClientMultiplexer provides a higher-level abstraction that manages multiple RpcClient instances and routes grain requests to the appropriate server.

### Key Components

```
┌─────────────────────────────────────┐
│        Application Layer            │
├─────────────────────────────────────┤
│      RpcClientMultiplexer           │
│  ┌─────────────────────────────┐   │
│  │   Grain Routing Strategy    │   │
│  └─────────────────────────────┘   │
│  ┌─────────────────────────────┐   │
│  │  Connection Pool Manager    │   │
│  └─────────────────────────────┘   │
├─────────────────────────────────────┤
│   RpcClient    RpcClient   RpcClient│
│      (S1)        (S2)        (S3)   │
└─────────────────────────────────────┘
```

### Core Concepts

#### 1. Server Registration
```csharp
public interface IServerDescriptor
{
    string ServerId { get; }
    string Endpoint { get; }
    int Port { get; }
    HashSet<string> Capabilities { get; } // e.g., "zone:1,0", "service:game"
    bool IsPrimary { get; }
}
```

#### 2. Grain Routing Strategy
```csharp
public interface IGrainRoutingStrategy
{
    string SelectServer(GrainReference grain, IReadOnlyList<IServerDescriptor> servers);
}
```

Common strategies:
- **Zone-based**: Route to server handling specific zone
- **Service-based**: Route based on grain interface type
- **Primary-fallback**: Use primary server with fallback options
- **Round-robin**: Distribute load across servers
- **Sticky**: Keep grain calls on same server

#### 3. Connection Lifecycle
- Lazy connection establishment
- Automatic reconnection with backoff
- Connection health monitoring
- Graceful shutdown

### Usage Patterns

#### Basic Usage
```csharp
var multiplexer = new RpcClientMultiplexer(options =>
{
    options.RoutingStrategy = new ZoneBasedRoutingStrategy();
    options.ConnectionTimeout = TimeSpan.FromSeconds(5);
    options.EnableHealthChecks = true;
});

// Register servers
await multiplexer.RegisterServerAsync(new ServerDescriptor
{
    ServerId = "zone-1-0",
    Endpoint = "localhost",
    Port = 12003,
    Capabilities = { "zone:1,0" }
});

// Get grain - multiplexer handles routing
var grain = multiplexer.GetGrain<IGameRpcGrain>("game");
```

#### Zone-Aware Usage
```csharp
// Set current context
multiplexer.SetContext(new GrainContext { Zone = "1,0" });

// Grain calls automatically routed to zone server
var playerGrain = multiplexer.GetGrain<IPlayerGrain>(playerId);
```

#### Multi-Service Usage
```csharp
// Different services on different servers
var gameGrain = multiplexer.GetGrain<IGameRpcGrain>("game");     // -> game server
var chatGrain = multiplexer.GetGrain<IChatGrain>("global");      // -> chat server
var leaderboard = multiplexer.GetGrain<ILeaderboard>("global");  // -> stats server
```

## Implementation Details

### Connection Pool

- Maintains `Dictionary<string, RpcClient>` mapping serverId to client
- Lazy initialization of connections
- Connection sharing for efficiency
- Automatic cleanup of unused connections

### Routing Decision Flow

1. Extract grain type and key from request
2. Query routing strategy for server selection
3. Get or create RpcClient for selected server
4. Forward grain request to appropriate client
5. Handle failures with retry/fallback logic

### Error Handling

- **Connection failures**: Automatic retry with exponential backoff
- **Server unavailable**: Fallback to alternate servers
- **No suitable server**: Clear error messaging
- **Partial failures**: Circuit breaker pattern

### Monitoring

- Connection health metrics
- Request routing statistics
- Latency tracking per server
- Error rate monitoring

## Migration Path

### Phase 1: Implement Core Multiplexer
- Basic multiplexer with manual server registration
- Simple routing strategies
- Connection pooling

### Phase 2: Service Discovery
- Automatic server discovery
- Dynamic server registration/deregistration
- Health checking

### Phase 3: Advanced Features
- Custom routing strategies
- Load balancing
- Circuit breakers
- Metrics integration

## Example: Shooter Game

The Shooter game demonstrates the need for multiplexing:

1. **Multiple ActionServers**: Each handles different zones
2. **Zone transitions**: Players move between servers
3. **Global services**: Some grains (like IGameRpcGrain) need consistent routing

Without multiplexer:
```csharp
// Current problematic approach
_rpcClient?.Dispose();
_rpcClient = new RpcClient(...);
await _rpcClient.ConnectAsync(newZoneServer);
var grain = _rpcClient.GetGrain<IGameRpcGrain>("game"); // Which server?
```

With multiplexer:
```csharp
// Clean approach
_multiplexer.SetContext(new { Zone = newZone });
var grain = _multiplexer.GetGrain<IGameRpcGrain>("game"); // Routed correctly
```

## Benefits

1. **Simplified client code**: No manual connection management
2. **Better performance**: Connection reuse, no recreation overhead
3. **Improved reliability**: Automatic failover and retry
4. **Flexible routing**: Pluggable strategies for different scenarios
5. **Future-proof**: Easy to add new routing strategies