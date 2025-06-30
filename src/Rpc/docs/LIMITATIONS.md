# Forkleans RPC Limitations

This document describes the current limitations of the Forkleans RPC implementation and potential future improvements.

## Multi-Server Support (Implemented)

As of the latest update, Forkleans RPC now supports multiple server connections with zone-aware routing. This addresses the previous single-server limitations.

### New Capabilities

1. **Multi-Server Connections**: Clients can connect to multiple RPC servers simultaneously
2. **Zone-Based Routing**: Requests can be routed to specific zones using integer zone IDs
3. **Per-Server Manifest Tracking**: Each server's grain manifest is tracked separately
4. **Dynamic Zone Discovery**: Servers can advertise their zone ID during handshake

### How Zone-Aware Routing Works

1. **Zone IDs**: Zones are identified by integer IDs for efficiency
2. **RpcRequest.TargetZoneId**: Optional field for specifying target zone
3. **Server Zone Advertisement**: Servers include their zone ID in RpcHandshakeAck
4. **Automatic Routing**: RpcConnectionManager routes requests to appropriate server based on zone

### Implementation Details

- **RpcConnectionManager**: Manages multiple connections and zone mappings
- **MultiServerManifestProvider**: Tracks manifests per server
- **IZoneAwareGrain**: Marker interface for grains that support zone routing
- **Zone ID not serialized over wire**: Server IDs are kept client-side only

## Remaining Limitations

### Manifest Management

#### Current Behavior
When an RPC client connects to a server, it receives an `RpcGrainManifest` containing:
- **Interface-to-Grain Mappings**: Maps interface types (e.g., `IGameRpcGrain`) to their implementation grain types
- **Grain Properties**: Metadata about each grain type including placement strategies and configuration
- **Interface Properties**: Information about each interface including method signatures and versions

The client's `RpcClientManifestProvider.UpdateFromServer()` method completely replaces the existing manifest with each new server's manifest:

```csharp
public void UpdateFromServer(RpcGrainManifest grainManifest)
{
    _clusterManifest = BuildClusterManifest(grainManifest);
}
```

#### Issues with Multiple Servers

1. **Last-Write-Wins Problem**: When connecting to multiple servers, only the manifest from the last server is retained. Previous manifests are completely discarded.

2. **No Manifest Merging**: There's no logic to:
   - Merge manifests from different servers
   - Detect conflicts between manifests
   - Validate compatibility between different server versions

3. **No Per-Server Tracking**: The client doesn't track which grains are available on which server. All grain routing decisions are based on a single global manifest.

4. **No Routing Information**: The manifest doesn't include information about which server can handle which grain types.

### Connection Management

1. **Single Endpoint Usage**: `RpcClient.ConnectAsync()` contains a TODO comment indicating it only uses the first endpoint provided.

2. **No Load Balancing**: No round-robin or other load balancing strategies for multiple servers.

3. **No Failover**: No automatic failover to alternate servers if the primary connection fails.

4. **No Connection Pooling**: Each client maintains a single connection, no pooling for better resource utilization.

5. **Multiple Hosts for Cross-Zone Communication**: In the Shooter sample, CrossZoneRpcService creates a new IHost (with full DI container) for each server connection, which is inefficient. The ideal solution would be to have a single RPC client that can dynamically connect to multiple servers, but this would require changes to the Forkleans RPC client implementation to support adding/removing connections at runtime.

### Version Management

1. **No Version Tracking**: The `RpcClientManifestProvider` uses `MajorMinorVersion.Zero` for all manifests, providing no version conflict detection.

2. **No Compatibility Checking**: No mechanism to ensure grain interface versions are compatible across different servers.

3. **Update Prevention**: The manifest update validator always returns `false`, preventing dynamic manifest updates after initial connection.

## Grain Reference Limitations

1. **No Server Affinity**: Grain references don't track which server they're associated with.

2. **Global Resolution**: All grain references are resolved using the single global manifest.

## Serialization Limitations

1. **No Per-Server Serialization Context**: Serialization configuration is global, not per-connection.

2. **Type Resolution Issues**: Custom types might resolve differently on different servers.

## Current Workarounds

For homogeneous server deployments (all servers running identical code):
- The manifest overwriting behavior is mostly harmless since all servers send identical manifests
- Interface-to-grain mappings remain consistent
- Grain properties and configurations match across servers

However, this breaks down during:
- Rolling updates with version differences
- Servers with different grain implementations
- Heterogeneous server configurations

## Future Improvements

See the [Potential Solutions](#potential-solutions) section below for proposed improvements to support proper multi-server scenarios.

---

## Potential Solutions

### 1. Per-Connection Manifest Management

Maintain separate manifests for each server connection:

```csharp
public class MultiServerManifestProvider : IClusterManifestProvider
{
    private readonly ConcurrentDictionary<string, ClusterManifest> _serverManifests = new();
    private readonly CompositeManifest _compositeManifest;

    public void UpdateFromServer(string serverId, RpcGrainManifest manifest)
    {
        var clusterManifest = BuildClusterManifest(manifest);
        _serverManifests[serverId] = clusterManifest;
        _compositeManifest.Rebuild(_serverManifests.Values);
    }
}
```

### 2. Grain Reference Enhancement

Include server routing information in grain references:

```csharp
public interface IRpcGrainReference : IGrainReference
{
    string PreferredServerId { get; }
    string[] AvailableServerIds { get; }
}
```

### 3. Connection Pool Management

Implement a connection pool with load balancing:

```csharp
public class RpcConnectionPool
{
    private readonly List<RpcConnection> _connections = new();
    private readonly ILoadBalancer _loadBalancer;

    public async Task<RpcConnection> GetConnectionAsync(GrainId grainId)
    {
        // Use grain ID to determine server affinity or round-robin
        return await _loadBalancer.SelectConnection(_connections, grainId);
    }
}
```

### 4. Manifest Conflict Resolution

Implement strategies for handling conflicting manifests:

```csharp
public interface IManifestConflictResolver
{
    GrainProperties ResolveGrainProperties(
        GrainType grainType,
        IEnumerable<(string serverId, GrainProperties props)> conflicts);
}

public class VersionBasedResolver : IManifestConflictResolver
{
    public GrainProperties ResolveGrainProperties(...)
    {
        // Select the highest version or most specific implementation
    }
}
```

### 5. Smart Client Routing

Implement client-side routing based on grain type and server capabilities:

```csharp
public class SmartRpcClient : IRpcClient
{
    private readonly IServerRegistry _serverRegistry;

    public async Task<TResult> InvokeAsync<TResult>(
        IGrainReference grain,
        string methodName,
        object[] arguments)
    {
        var servers = _serverRegistry.GetServersForGrain(grain.GrainType);
        var connection = await SelectOptimalConnection(servers, grain);
        return await connection.InvokeAsync<TResult>(grain, methodName, arguments);
    }
}
```

### 6. Versioning Support

Add proper version tracking and compatibility checking:

```csharp
public class VersionedManifestProvider
{
    public bool IsCompatible(InterfaceVersion clientVersion, InterfaceVersion serverVersion)
    {
        // Implement semantic versioning rules
        return clientVersion.Major == serverVersion.Major &&
               clientVersion.Minor <= serverVersion.Minor;
    }
}
```

### 7. Configuration Options

Add configuration to control multi-server behavior:

```csharp
public class RpcClientOptions
{
    public MultiServerMode Mode { get; set; } = MultiServerMode.SingleServer;
    public ConflictResolutionStrategy ConflictStrategy { get; set; }
    public bool EnableConnectionPooling { get; set; }
    public LoadBalancingStrategy LoadBalancing { get; set; }
}

public enum MultiServerMode
{
    SingleServer,        // Current behavior
    MultiServerMerged,   // Merge all manifests
    MultiServerIsolated  // Keep manifests separate
}
```

## Migration Path (Completed)

The multi-server support has been implemented with full backward compatibility:

1. **Phase 1**: ✅ Added RpcConnectionManager, MultiServerManifestProvider, IZoneAwareGrain
2. **Phase 2**: ✅ Multi-server support is active by default (single server still works)
3. **Phase 3**: ✅ RpcClient now uses connection manager internally
4. **Phase 4**: ✅ Zone-aware routing is opt-in via IZoneAwareGrain interface

### Backward Compatibility

- Existing single-server clients work unchanged
- TargetZoneId is optional - requests without it work as before
- Servers without zone awareness operate normally
- No breaking changes to existing APIs
