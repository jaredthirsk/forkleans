# Forkleans RPC Multi-Server Support Design

## Current Architecture Analysis

### 1. Connection Management

**Current State:**
- `RpcClient` maintains a single `IRpcTransport _transport` instance
- Single `IPEndPoint _serverEndpoint` field
- `ConnectAsync()` only uses the first endpoint from `RpcClientOptions.ServerEndpoints`
- All requests go through `SendRequestAsync()` which sends to the single `_serverEndpoint`

**Key Issues:**
- No connection pooling or management for multiple servers
- Transport abstraction assumes single peer connection
- No routing logic to select which server handles a request

### 2. Manifest Management

**Current State:**
- `RpcClientManifestProvider` maintains a single `ClusterManifest`
- `UpdateFromServer()` completely replaces the manifest on each connection
- No tracking of which grains are available on which server
- No conflict resolution between different server manifests

### 3. Grain Reference Routing

**Current State:**
- `RpcGrainReference` calls `_rpcClient.SendRequestAsync()` directly
- No server affinity or routing information in grain references
- All requests go to the single connected server

### 4. Transport Layer

**Current State:**
- `IRpcTransport` interface designed for single connection
- Client transports (e.g., `LiteNetLibClientTransport`) maintain single `_serverPeer`
- No abstraction for managing multiple transport connections

## Proposed Multi-Server Architecture

### Solution 1: Connection Pool with Smart Routing

**Design:**
```csharp
// New connection pool to manage multiple server connections
public interface IRpcConnectionPool
{
    Task<IRpcConnection> GetConnectionAsync(GrainId grainId, GrainInterfaceType interfaceType);
    Task<IRpcConnection> GetConnectionByServerIdAsync(string serverId);
    Task ConnectToServerAsync(IPEndPoint endpoint, CancellationToken cancellationToken);
    Task DisconnectFromServerAsync(string serverId);
    IReadOnlyList<IRpcConnection> GetActiveConnections();
}

// Individual connection abstraction
public interface IRpcConnection
{
    string ServerId { get; }
    IPEndPoint Endpoint { get; }
    bool IsConnected { get; }
    Task<Protocol.RpcResponse> SendRequestAsync(Protocol.RpcRequest request);
}

// Updated RpcClient to use connection pool
internal sealed class RpcClient : IClusterClient, IRpcClient, IHostedService
{
    private readonly IRpcConnectionPool _connectionPool;
    
    internal async Task<Protocol.RpcResponse> SendRequestAsync(Protocol.RpcRequest request)
    {
        var connection = await _connectionPool.GetConnectionAsync(
            request.GrainId, 
            request.InterfaceType);
        return await connection.SendRequestAsync(request);
    }
}
```

**Implementation Steps:**
1. Create `RpcConnection` class that wraps a transport instance
2. Implement `RpcConnectionPool` with configurable routing strategies
3. Update `RpcClient` to use the connection pool
4. Add server selection logic based on grain type/ID

### Solution 2: Per-Server Manifest Management

**Design:**
```csharp
public interface IMultiServerManifestProvider : IClusterManifestProvider
{
    void UpdateServerManifest(string serverId, RpcGrainManifest manifest);
    ClusterManifest GetServerManifest(string serverId);
    bool TryGetGrainServer(GrainType grainType, out string serverId);
    IReadOnlyList<string> GetServersForGrain(GrainType grainType);
}

public class MultiServerManifestProvider : IMultiServerManifestProvider
{
    private readonly ConcurrentDictionary<string, ClusterManifest> _serverManifests = new();
    private readonly ConcurrentDictionary<GrainType, HashSet<string>> _grainToServers = new();
    private ClusterManifest _mergedManifest;
    
    public void UpdateServerManifest(string serverId, RpcGrainManifest manifest)
    {
        var clusterManifest = BuildClusterManifest(manifest);
        _serverManifests[serverId] = clusterManifest;
        UpdateGrainMappings(serverId, manifest);
        RebuildMergedManifest();
    }
}
```

**Features:**
- Track manifests per server
- Build grain-to-server mappings
- Provide merged view for compatibility
- Support conflict resolution strategies

### Solution 3: Enhanced Grain References

**Design:**
```csharp
public interface IRpcGrainReference : IGrainReference
{
    string PreferredServerId { get; }
    ServerAffinityMode AffinityMode { get; }
}

public enum ServerAffinityMode
{
    None,           // Any server can handle
    Preferred,      // Try preferred, fallback to any
    Required        // Must use specific server
}

internal class RpcGrainReference : GrainReference, IRpcGrainReference
{
    private readonly string _preferredServerId;
    private readonly ServerAffinityMode _affinityMode;
    
    public async Task<T> InvokeRpcMethodAsync<T>(int methodId, object[] arguments)
    {
        var request = BuildRequest(methodId, arguments);
        
        // Route based on affinity
        var connection = _affinityMode switch
        {
            ServerAffinityMode.Required => 
                await _connectionPool.GetConnectionByServerIdAsync(_preferredServerId),
            ServerAffinityMode.Preferred => 
                await TryPreferredOrFallback(),
            _ => 
                await _connectionPool.GetConnectionAsync(GrainId, InterfaceType)
        };
        
        var response = await connection.SendRequestAsync(request);
        return DeserializeResponse<T>(response);
    }
}
```

### Solution 4: Transport Layer Enhancement

**Design:**
```csharp
// New multi-peer transport abstraction
public interface IMultiPeerTransport : IRpcTransport
{
    Task<string> ConnectToPeerAsync(IPEndPoint endpoint, CancellationToken cancellationToken);
    Task DisconnectFromPeerAsync(string peerId);
    Task SendToPeerAsync(string peerId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken);
    IReadOnlyList<string> GetConnectedPeers();
}

// Update client transport implementations
public class LiteNetLibMultiClientTransport : IMultiPeerTransport
{
    private readonly ConcurrentDictionary<string, NetPeer> _peers = new();
    
    public async Task<string> ConnectToPeerAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        var peer = _netManager.Connect(endpoint, "RpcConnection");
        var peerId = Guid.NewGuid().ToString();
        _peers[peerId] = peer;
        return peerId;
    }
}
```

### Solution 5: Configuration and Routing Strategies

**Design:**
```csharp
public class RpcClientOptions
{
    public MultiServerMode Mode { get; set; } = MultiServerMode.SingleServer;
    public IServerSelectionStrategy ServerSelection { get; set; }
    public IManifestConflictResolver ConflictResolver { get; set; }
    public bool EnableConnectionPooling { get; set; } = true;
    public int MaxConnectionsPerServer { get; set; } = 1;
}

public interface IServerSelectionStrategy
{
    Task<string> SelectServerAsync(
        GrainId grainId,
        GrainInterfaceType interfaceType,
        IReadOnlyList<string> availableServers);
}

// Built-in strategies
public class RoundRobinStrategy : IServerSelectionStrategy { }
public class ConsistentHashStrategy : IServerSelectionStrategy { }
public class GrainTypeAffinityStrategy : IServerSelectionStrategy { }
public class LowestLatencyStrategy : IServerSelectionStrategy { }
```

## Implementation Plan

### Phase 1: Foundation (Backward Compatible)
1. Create connection abstraction interfaces
2. Implement `RpcConnection` wrapper class
3. Add `IRpcConnectionPool` interface
4. Create basic pool implementation (single connection)

### Phase 2: Multi-Connection Support
1. Implement `RpcConnectionPool` with multiple connections
2. Update transport layer for multi-peer support
3. Add connection management (connect/disconnect/reconnect)
4. Implement basic round-robin routing

### Phase 3: Smart Routing
1. Implement per-server manifest tracking
2. Add grain-to-server mapping
3. Implement routing strategies
4. Add server affinity to grain references

### Phase 4: Advanced Features
1. Connection health monitoring
2. Automatic failover
3. Load balancing strategies
4. Latency-based routing

## Migration Path

1. **Opt-in via Configuration:**
   ```csharp
   builder.Host.UseOrleansRpcClient(rpcBuilder =>
   {
       rpcBuilder.Configure<RpcClientOptions>(options =>
       {
           options.Mode = MultiServerMode.MultiServer;
           options.ServerSelection = new RoundRobinStrategy();
       });
   });
   ```

2. **Gradual Feature Adoption:**
   - Start with single server (current behavior)
   - Enable multi-server with round-robin
   - Add smart routing as needed

3. **Backward Compatibility:**
   - Default to single-server mode
   - Existing code continues to work
   - New features behind feature flags

## Key Benefits

1. **Scalability:** Distribute load across multiple servers
2. **Fault Tolerance:** Automatic failover on server failure
3. **Performance:** Route requests to optimal server
4. **Flexibility:** Support heterogeneous server deployments
5. **Gradual Migration:** Adopt features as needed

## Example Usage

```csharp
// Configure multi-server RPC client
builder.Host.UseOrleansRpcClient(rpcBuilder =>
{
    rpcBuilder.UseLiteNetLib();
    rpcBuilder.Configure<RpcClientOptions>(options =>
    {
        options.Mode = MultiServerMode.MultiServer;
        options.ServerEndpoints.Add(new IPEndPoint(IPAddress.Parse("10.0.0.1"), 11111));
        options.ServerEndpoints.Add(new IPEndPoint(IPAddress.Parse("10.0.0.2"), 11111));
        options.ServerEndpoints.Add(new IPEndPoint(IPAddress.Parse("10.0.0.3"), 11111));
        
        // Use consistent hashing for grain distribution
        options.ServerSelection = new ConsistentHashStrategy();
        
        // Enable connection pooling
        options.EnableConnectionPooling = true;
        options.MaxConnectionsPerServer = 3;
    });
});

// Use the client normally - routing happens automatically
var client = host.Services.GetRequiredService<IRpcClient>();
var grain = client.GetGrain<IGameGrain>(playerId);
await grain.UpdatePosition(position); // Automatically routed to appropriate server
```