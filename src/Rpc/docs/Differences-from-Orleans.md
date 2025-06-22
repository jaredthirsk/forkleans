# Differences from Orleans

This document outlines the architectural differences between Orleans RPC and standard Orleans, and clarifies which services are shared, which are separate, and which require keyed service registration.

## Overview

Orleans RPC provides a lightweight one-to-one client-server communication model over extensible transports (focusing on reliable UDP), while standard Orleans provides a distributed actor model with clustering. The RPC system reuses many Orleans components but replaces the clustering layer.

## Major Architectural Differences

### 1. Clustering Model
- **Orleans**: Multi-node cluster with gossip protocol, membership table, and automatic failover
- **Orleans RPC**: Direct client-server connection without clustering or failover

### 2. Service Discovery
- **Orleans**: Uses `IClusterMembershipService` with membership table providers
- **Orleans RPC**: Uses `IRpcServerDirectory` for server discovery (in-memory or custom)

### 3. Transport Layer
- **Orleans**: TCP-based connections with silo-to-silo and client-to-gateway communication
- **Orleans RPC**: Extensible transport system with focus on reliable UDP, supports multiple transports

### 4. Manifest Management
- **Orleans**: `IClusterManifestProvider` manages grain type metadata across cluster
- **Orleans RPC**: Custom `IClusterManifestProvider` implementation that works without clustering

## Service Categories

### 1. Services That Need Keyed Registration

These services have different implementations for RPC vs Orleans and must be registered with keys to avoid conflicts:

| Service | Key | Purpose |
|---------|-----|---------|
| `IClusterManifestProvider` | `"rpc"` | Type metadata management |
| `GrainFactory` | `"rpc"` | Grain factory implementation |
| `IGrainFactory` | `"rpc"` | Grain factory interface |
| `IInternalGrainFactory` | `"rpc"` | Internal grain factory interface |
| `GrainInterfaceTypeToGrainTypeResolver` | `"rpc"` | Interface to grain type mapping |
| `IRpcTransportConnectionFactory` | Transport name | Transport-specific connection factories |

### 2. Services with RPC-Specific Implementations

These are completely replaced in RPC mode:

| Orleans Service | RPC Service | Purpose |
|-----------------|-------------|---------|
| `IClusterMembershipService` | Not used | RPC doesn't use clustering |
| `IGatewayManager` | Not used | RPC uses direct connections |
| `ISiloStatusOracle` | Not used | No silo health monitoring in RPC |
| `IMembershipTableGrain` | Not used | No membership management |

### 3. Services Shared Between Orleans and RPC

These services work identically in both modes:

| Service | Purpose |
|---------|---------|
| `ISerializer` | Object serialization |
| `ILoggerFactory` | Logging infrastructure |
| `IOptions<*>` | Configuration system |
| `IGrainFactory` | Grain activation |
| `IInvokableObjectFactory` | Method invocation |
| `ICodecProvider` | Codec registration |
| `ITypeResolver` | Type resolution |
| `DeepCopier` | Object deep copying |

### 4. RPC-Specific Services

New services introduced for RPC functionality:

| Service | Purpose |
|---------|---------|
| `IRpcTransportConnection` | Individual transport connections |
| `IRpcServerDirectory` | Server discovery and registration |
| `IRpcClient` | RPC client implementation |
| `IRpcServer` | RPC server implementation |
| `IRpcMessageHandler` | Message processing |
| `InMemoryRpcServerDirectory` | Default server directory |

## Key Implementation Patterns

### 1. Service Registration Pattern

```csharp
// Step 1: Register RPC-specific types (no conflicts with Orleans)
services.TryAddSingleton<RpcGrainFactory>(...);
services.TryAddSingleton<RpcClientManifestProvider>(...);

// Step 2: Register Orleans interfaces with RPC implementations as keyed services
services.AddKeyedSingleton<IClusterManifestProvider>("rpc", sp => 
    sp.GetRequiredService<RpcClientManifestProvider>());
services.AddKeyedSingleton<GrainFactory>("rpc", sp => 
    sp.GetRequiredService<RpcGrainFactory>());
services.AddKeyedSingleton<IGrainFactory>("rpc", (sp, key) => 
    sp.GetRequiredKeyedService<GrainFactory>("rpc"));

// Step 3: For standalone RPC (without Orleans), register as unkeyed
// TryAddSingleton ensures Orleans takes precedence when both are present
services.TryAddSingleton<IClusterManifestProvider>(sp => 
    sp.GetRequiredService<RpcClientManifestProvider>());
services.TryAddSingleton<GrainFactory>(sp => 
    sp.GetRequiredService<RpcGrainFactory>());
```

### 2. Transport Registration Pattern

```csharp
services.AddKeyedSingleton<IRpcTransportConnectionFactory, TcpTransportFactory>("tcp");
services.AddKeyedSingleton<IRpcTransportConnectionFactory, UdpTransportFactory>("udp");
```

### 3. Manifest Provider Differences

- **Orleans `ClientClusterManifestProvider`**: Fetches metadata from cluster silos
- **RPC `ClientClusterManifestProvider`**: Works without cluster, uses local type information

## Migration Considerations

### When Moving from Orleans to RPC

1. Remove clustering-related configuration
2. Replace gateway configuration with RPC server endpoints
3. Update service registrations to use RPC versions
4. Remove dependency on membership providers

### When Using Both Orleans and RPC Together

1. Use keyed services for conflicting implementations
2. Ensure proper service resolution order
3. Keep transport configurations separate
4. Be aware of serialization compatibility

### Critical Issue: Orleans Client + RPC Server Conflict

When an application uses both:
- Orleans client (via `UseOrleansClient`) to connect to a silo
- RPC server (via `UseOrleansRpc`) to host its own RPC grains

The following conflicts occur:

| Service | Orleans Client | RPC Client | Result |
|---------|----------------|------------|---------|
| `IClusterClient` | Orleans implementation | RPC implementation | RPC overwrites Orleans |
| `IClusterManifestProvider` | Fetches from cluster | Local RPC manifest | RPC overwrites Orleans |
| `IGrainFactory` | Creates Orleans proxies | Creates RPC proxies | RPC overwrites Orleans |

**Solution**: The application must ensure Orleans client services are registered AFTER RPC services, or use explicit service resolution:

```csharp
// Get the Orleans client specifically
var orleansClient = serviceProvider.GetServices<IClusterClient>()
    .OfType<Forkleans.Hosting.IOrleansClient>()
    .FirstOrDefault();
```

## Grain Type Resolution Components

### Components Shared Between Orleans and RPC

These components work identically in both modes:

| Component | Purpose | Notes |
|-----------|---------|-------|
| `GrainTypeResolver` | Maps classes to GrainType IDs | Same implementation |
| `IGrainTypeProvider` | Interface for type resolution | Same implementation |
| `AttributeGrainTypeProvider` | Resolves from [GrainType] attributes | Same implementation |
| `GrainInterfaceTypeResolver` | Maps interfaces to GrainInterfaceType IDs | Same implementation |
| `IGrainInterfaceTypeProvider` | Interface for interface resolution | Same implementation |
| `TypeNameGrainPropertiesProvider` | Adds type metadata | Same implementation |
| `IGrainPropertiesProvider` | Interface for property providers | Same implementation |

### Critical Component with Different Behavior

| Component | Orleans Behavior | RPC Behavior | Issue |
|-----------|-----------------|--------------|-------|
| `GrainInterfaceTypeToGrainTypeResolver` | Uses Orleans manifest provider | Uses RPC manifest provider | **Different grain metadata** |

The `GrainInterfaceTypeToGrainTypeResolver` is where the critical difference lies:
- It depends on `IClusterManifestProvider` to get grain metadata
- Orleans client gets metadata from the cluster silos
- RPC client gets metadata from local/RPC sources
- **If the wrong provider is used, grain lookups fail**

## Most Likely Service Conflicts (When Using Both Orleans Client and RPC Server)

These are the services most likely to cause conflicts when an application uses both Orleans client and RPC server:

### 1. Service Conflict Resolution

As of version 9.2.0.9, these conflicts have been resolved through keyed service registration:

| Service | Resolution | Status |
|---------|------------|--------|
| `GrainFactory` | RPC uses keyed service `"rpc"`, Orleans uses unkeyed | **RESOLVED** |
| `IGrainFactory` | RPC uses keyed service `"rpc"`, Orleans uses unkeyed | **RESOLVED** |
| `IInternalGrainFactory` | RPC uses keyed service `"rpc"`, Orleans uses unkeyed | **RESOLVED** |
| `IClusterManifestProvider` | RPC uses keyed service `"rpc"`, Orleans uses unkeyed | **RESOLVED** |
| `GrainInterfaceTypeToGrainTypeResolver` | RPC uses keyed service `"rpc"` | **RESOLVED** |
| `GrainReferenceActivator` | Shared single instance | **LOW RISK** - Works correctly |
| `IGrainReferenceActivatorProvider` | Multiple providers, order matters | **LOW RISK** - Expected behavior |
| `GrainPropertiesResolver` | Shared, uses unkeyed manifest provider | **LOW RISK** - TryAddSingleton protects |

### 2. How the Resolution Works

When `GetGrain<IWorldManagerGrain>()` is called:

**In Orleans Client + RPC Server scenario:**
```
app.GetService<IGrainFactory>() → Orleans GrainFactory (unkeyed wins)
    ↓
Orleans GrainFactory.GetGrain<T>() → Uses Orleans manifest
    ↓  
Orleans creates proxy for Orleans silo
```

**In standalone RPC scenario:**
```
app.GetService<IGrainFactory>() → RPC GrainFactory (via TryAdd fallback)
    ↓
RPC GrainFactory.GetGrain<T>() → Uses RPC manifest
    ↓  
RPC creates proxy for RPC server
```

**For RPC internal usage:**
```
sp.GetKeyedService<IGrainFactory>("rpc") → RPC GrainFactory
    ↓
Always uses RPC implementation
```

### 3. Key Design Principles

1. **Orleans services remain unkeyed**: Minimizes changes to Orleans code
2. **RPC uses keyed services**: Prevents conflicts when both are present
3. **TryAdd provides fallback**: Enables standalone RPC mode
4. **Registration order matters**: Orleans first, then RPC

### 4. Service Registration Best Practices

1. **RPC-specific types**: Register normally (no conflict)
   ```csharp
   services.TryAddSingleton<RpcGrainFactory>(...);
   services.TryAddSingleton<RpcClient>(...);
   ```

2. **Orleans interfaces**: Use keyed registration for RPC
   ```csharp
   services.AddKeyedSingleton<IGrainFactory>("rpc", ...);
   services.AddKeyedSingleton<IClusterManifestProvider>("rpc", ...);
   ```

3. **Standalone support**: Add unkeyed fallback
   ```csharp
   services.TryAddSingleton<IGrainFactory>(sp => 
       sp.GetRequiredService<RpcGrainFactory>());
   ```

### 5. Verifying Correct Service Resolution

To verify services are correctly registered:

```csharp
// In ActionServer (Orleans + RPC), these should be different:
var orleansFactory = serviceProvider.GetService<IGrainFactory>();
var rpcFactory = serviceProvider.GetKeyedService<IGrainFactory>("rpc");

Console.WriteLine($"Orleans factory: {orleansFactory?.GetType().Name}"); // Should be Orleans GrainFactory
Console.WriteLine($"RPC factory: {rpcFactory?.GetType().Name}");        // Should be RpcGrainFactory

// Check manifest providers
var orleansManifest = serviceProvider.GetService<IClusterManifestProvider>();
var rpcManifest = serviceProvider.GetKeyedService<IClusterManifestProvider>("rpc");

Console.WriteLine($"Orleans manifest: {orleansManifest?.GetType().Name}"); // Orleans provider
Console.WriteLine($"RPC manifest: {rpcManifest?.GetType().Name}");        // RPC provider
```

## Special Considerations for Triple-Mode Applications

When an application is simultaneously an Orleans client, RPC server, and RPC client (like ActionServer):

### 1. Grain Reference Activator Providers
- Multiple `IGrainReferenceActivatorProvider` implementations can coexist
- RPC's provider only handles grains with RPC proxy types (returns false for others)
- Orleans' provider handles Orleans grains
- Order matters: providers are tried in registration order

### 2. Service Usage Patterns
- **Orleans client operations**: Use injected `IClusterClient` (Orleans)
- **RPC hosting**: Uses keyed services internally
- **RPC client operations**: Create temporary hosts or use keyed services

### 3. Temporary RPC Clients
When creating temporary RPC clients for server-to-server communication:
```csharp
using var host = Host.CreateDefaultBuilder()
    .UseOrleansRpcClient(...)
    .Build();
await host.StartAsync();
try 
{
    var rpcClient = host.Services.GetRequiredService<IClusterClient>();
    // Use client...
}
finally
{
    await host.StopAsync();
}
```

### 4. Known Limitations
- `GrainPropertiesResolver` is shared and uses unkeyed `IClusterManifestProvider`
- Service registration order is important (Orleans client before RPC)
- Temporary RPC client hosts should be properly disposed to avoid resource leaks

