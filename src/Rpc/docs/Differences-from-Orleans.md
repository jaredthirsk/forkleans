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
| `IClusterManifestProvider` | `"rpc"` / `"orleans"` | Type metadata management |
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
// RPC Client Services
services.AddKeyedSingleton<IClusterManifestProvider, ClientClusterManifestProvider>("rpc");
services.AddSingleton<IClusterManifestProvider>(sp => 
    sp.GetRequiredKeyedService<IClusterManifestProvider>("rpc"));

// Orleans Client Services (when both are used)
services.AddKeyedSingleton<IClusterManifestProvider, OrleansProvider>("orleans");
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

### 1. High Risk - Services Both Systems Register

| Service | Orleans Registration | RPC Registration | Risk |
|---------|---------------------|------------------|------|
| `GrainFactory` | Registered by Orleans client | Registered by RPC (as RpcGrainFactory) | **HIGH** - RPC may override |
| `IGrainFactory` | Mapped from GrainFactory | Mapped from GrainFactory | **HIGH** - Wrong factory used |
| `IInternalGrainFactory` | Mapped from GrainFactory | Mapped from GrainFactory | **HIGH** - Wrong factory used |
| `GrainReferenceActivator` | Single instance | Single instance | **MEDIUM** - Shared service |
| `IGrainReferenceActivatorProvider` | Multiple providers | RPC adds its own first | **MEDIUM** - Order matters |
| `GrainPropertiesResolver` | Uses Orleans manifest | Uses RPC manifest | **MEDIUM** - TryAddSingleton, order matters |
| `IClusterManifestProvider` (unkeyed) | Orleans provider | RPC provider (TryAdd) | **HIGH** - First registration wins |

### 2. Critical Resolution Chain

When `GetGrain<IWorldManagerGrain>()` is called:
```
IGrainFactory (which concrete type?)
    ↓
GrainFactory.GetGrain<T>() (Orleans or RPC?)
    ↓  
GrainInterfaceTypeToGrainTypeResolver (which manifest provider?)
    ↓
IClusterManifestProvider (Orleans or RPC?)
```

### 3. Specific Conflict Points

1. **GrainFactory Registration**:
   - Orleans registers `ClusterClient.GrainFactory`
   - RPC registers `RpcGrainFactory` as `GrainFactory`
   - Last registration wins!

2. **GrainReferenceActivator Providers**:
   - RPC adds `RpcGrainReferenceActivatorProvider` first
   - This may intercept grain creation requests

3. **Manifest Provider Resolution**:
   - Even with keyed services, the wrong resolver might get the wrong provider
   
4. **GrainPropertiesResolver Dependency**:
   - `GrainPropertiesResolver` requires an unkeyed `IClusterManifestProvider`
   - RPC's `RpcGrainReferenceActivatorProvider` uses `GrainPropertiesResolver`
   - In standalone RPC client mode, RPC registers its manifest provider as unkeyed using `TryAddSingleton`
   - When Orleans client is present, it must be configured first so its manifest provider is used

### 4. Recommended Investigation

To diagnose which services are conflicting:

```csharp
// Check which GrainFactory is registered
var grainFactory = serviceProvider.GetService<IGrainFactory>();
Console.WriteLine($"GrainFactory type: {grainFactory?.GetType().FullName}");

// Check which IClusterClient is registered
var clusterClient = serviceProvider.GetService<IClusterClient>();
Console.WriteLine($"IClusterClient type: {clusterClient?.GetType().FullName}");

// Check all GrainReferenceActivatorProviders
var providers = serviceProvider.GetServices<IGrainReferenceActivatorProvider>();
foreach (var provider in providers)
{
    Console.WriteLine($"Provider: {provider.GetType().FullName}");
}
```

