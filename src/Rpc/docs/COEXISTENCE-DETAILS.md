# Orleans and RPC Coexistence Details

## Overview

This document provides detailed information about Orleans classes, their dependencies, and the strategy for ensuring Orleans client and RPC server can coexist in the same application without service registration conflicts.

## Core Orleans Classes and Dependencies

### 1. GrainPropertiesResolver

**Purpose**: Resolves grain properties from the cluster manifest for a given GrainType.

**Constructor Dependencies**:
```csharp
public GrainPropertiesResolver(IClusterManifestProvider clusterManifestProvider)
```

**Key Issue**: Uses unkeyed `IClusterManifestProvider`, which in a triple-mode app (Orleans client + RPC server) will get Orleans' manifest provider instead of RPC's.

**Used By**:
- `RpcGrainReferenceActivatorProvider` - to check grain properties
- `PlacementStrategyResolver` - to determine placement strategies
- `PlacementFilterStrategyResolver` - for placement filtering
- `GrainDirectoryResolver` - to resolve grain directories

### 2. GrainInterfaceTypeToGrainTypeResolver

**Purpose**: Maps grain interface types to their implementation grain types.

**Constructor Dependencies**:
```csharp
public GrainInterfaceTypeToGrainTypeResolver(IClusterManifestProvider clusterManifestProvider)
```

**Current Fix**: RPC already registers this as a keyed service using RPC's manifest provider.

### 3. GrainBindingsResolver

**Purpose**: Resolves bindings (extensions) for grain types.

**Constructor Dependencies**:
```csharp
public GrainBindingsResolver(IClusterManifestProvider clusterManifestProvider)
```

**Issue**: Not currently handled by RPC's keyed service strategy.

### 4. GrainVersionManifest

**Purpose**: Manages grain interface versions for compatibility checking.

**Constructor Dependencies**:
```csharp
public GrainVersionManifest(IClusterManifestProvider clusterManifestProvider)
```

**Issue**: Not currently handled by RPC's keyed service strategy.

## Dependency Hierarchy

```
IClusterManifestProvider (root dependency)
├── GrainPropertiesResolver ⚠️ (currently unkeyed - ISSUE!)
│   ├── RpcGrainReferenceActivatorProvider ⚠️ (gets wrong manifest)
│   ├── PlacementStrategyResolver (Orleans-only, not used by RPC)
│   ├── PlacementFilterStrategyResolver (Orleans-only, not used by RPC)
│   └── GrainDirectoryResolver (Orleans-only, not used by RPC)
├── GrainInterfaceTypeToGrainTypeResolver ✅ (already keyed for RPC)
│   └── GrainFactory / RpcGrainFactory ✅ (already keyed)
├── GrainBindingsResolver (not currently used by RPC)
│   └── (used for grain extensions)
└── GrainVersionManifest (may be used by RPC)
    └── (used for version compatibility)
```

**Legend**:
- ✅ Already fixed with keyed services
- ⚠️ Needs fixing
- No symbol: Not used by RPC or no conflict

## Core Principles

1. **Minimize Orleans modifications**: Orleans services remain unkeyed to avoid modifying original Orleans code
2. **RPC-specific types are safe**: Types like `RpcGrainFactory`, `RpcClient`, etc. don't conflict with Orleans
3. **Use keyed services only when necessary**: Only when RPC implements an Orleans interface that Orleans also implements

## Service Registration Strategy

### Safe to Register as Unkeyed (No Conflicts)

These RPC-specific types don't exist in Orleans and can be registered normally:

```csharp
// RPC-specific types - no conflicts
services.TryAddSingleton<RpcGrainFactory>(...);
services.TryAddSingleton<RpcClient>(...);
services.TryAddSingleton<RpcProvider>(...);
services.TryAddSingleton<RpcGrainReferenceActivatorProvider>(...);
// etc.
```

### Requires Keyed Registration (Conflict Resolution)

These are Orleans interfaces that both systems implement differently:

| Orleans Interface | Orleans Implementation | RPC Implementation | Resolution |
|-------------------|----------------------|-------------------|------------|
| `IClusterManifestProvider` | Fetches from cluster | Local/RPC manifest | RPC uses key `"rpc"` |
| `GrainFactory` | Orleans factory | RpcGrainFactory | RPC uses key `"rpc"` |
| `IGrainFactory` | Maps to GrainFactory | Maps to RpcGrainFactory | RPC uses key `"rpc"` |
| `IInternalGrainFactory` | Maps to GrainFactory | Maps to RpcGrainFactory | RPC uses key `"rpc"` |
| `GrainInterfaceTypeToGrainTypeResolver` | Uses Orleans manifest | Uses RPC manifest | RPC uses key `"rpc"` |

### Registration Pattern

```csharp
// Step 1: Register RPC-specific implementation (no conflict)
services.TryAddSingleton<RpcGrainFactory>(...);

// Step 2: Register as keyed service for Orleans interfaces
services.AddKeyedSingleton<GrainFactory>("rpc", sp => sp.GetRequiredService<RpcGrainFactory>());
services.AddKeyedSingleton<IGrainFactory>("rpc", (sp, key) => sp.GetRequiredKeyedService<GrainFactory>("rpc"));

// Step 3: For standalone RPC mode, also register as unkeyed
// TryAddSingleton ensures Orleans takes precedence when both are present
services.TryAddSingleton<GrainFactory>(sp => sp.GetRequiredService<RpcGrainFactory>());
services.TryAddFromExisting<IGrainFactory, GrainFactory>();
```

## Current Issues and Solutions

### 1. GrainPropertiesResolver Dependency Issue

**Problem**: `RpcGrainReferenceActivatorProvider` depends on `GrainPropertiesResolver`, which uses the unkeyed `IClusterManifestProvider`. In triple-mode apps, this gets Orleans' manifest instead of RPC's.

**Current Code**:
```csharp
// In RpcGrainReferenceActivatorProvider constructor
public RpcGrainReferenceActivatorProvider(
    GrainPropertiesResolver propertiesResolver, // This uses Orleans' manifest!
    // ... other dependencies
)
```

**Solution Pattern**: Register Orleans classes with manual construction when they need RPC services:
```csharp
// Register GrainPropertiesResolver as keyed service for RPC
services.AddKeyedSingleton<GrainPropertiesResolver>("rpc", (sp, key) => 
    new GrainPropertiesResolver(
        sp.GetRequiredKeyedService<IClusterManifestProvider>("rpc")
    ));

// Update RpcGrainReferenceActivatorProvider to use keyed resolver
services.TryAddSingleton<RpcGrainReferenceActivatorProvider>(sp => 
    new RpcGrainReferenceActivatorProvider(
        sp.GetRequiredKeyedService<GrainPropertiesResolver>("rpc"), // Use RPC's resolver
        // ... other dependencies
    ));
```

### 2. Other Orleans Classes Needing RPC Services

**Classes that need similar treatment**:
```csharp
// GrainBindingsResolver - if RPC uses grain extensions
services.AddKeyedSingleton<GrainBindingsResolver>("rpc", (sp, key) => 
    new GrainBindingsResolver(
        sp.GetRequiredKeyedService<IClusterManifestProvider>("rpc")
    ));

// GrainVersionManifest - for version compatibility
services.AddKeyedSingleton<GrainVersionManifest>("rpc", (sp, key) => 
    new GrainVersionManifest(
        sp.GetRequiredKeyedService<IClusterManifestProvider>("rpc")
    ));
```

### 3. Registration Order Dependency

**Issue**: Orleans client must be registered before RPC to ensure correct service precedence.

**Reason**: When using `TryAddSingleton`, the first registration wins. Orleans needs to win for unkeyed services.

## Testing Scenarios

### Scenario 1: Standalone RPC Client
- No Orleans client present
- RPC services work via unkeyed fallbacks
- All services resolve correctly

### Scenario 2: Orleans Client + RPC Server (ActionServer)
- Orleans client registered first
- RPC server adds its keyed services
- Orleans unkeyed services take precedence
- RPC uses its keyed services internally

### Scenario 3: Service Resolution Verification
```csharp
// Verify correct services are registered
var grainFactory = serviceProvider.GetService<IGrainFactory>();
var rpcGrainFactory = serviceProvider.GetKeyedService<IGrainFactory>("rpc");

// In ActionServer scenario:
// - grainFactory should be Orleans implementation
// - rpcGrainFactory should be RPC implementation
```

## Manual Construction Pattern for Orleans Classes

When an Orleans class needs RPC-specific services via constructor injection, we use manual construction with keyed services:

### Pattern 1: Simple Constructor
```csharp
// Orleans class with single dependency
services.AddKeyedSingleton<GrainPropertiesResolver>("rpc", (sp, key) => 
    new GrainPropertiesResolver(
        sp.GetRequiredKeyedService<IClusterManifestProvider>("rpc")
    ));
```

### Pattern 2: Multiple Dependencies
```csharp
// Orleans class with multiple dependencies, some RPC-specific
services.AddKeyedSingleton<SomeOrleansClass>("rpc", (sp, key) => 
    new SomeOrleansClass(
        sp.GetRequiredKeyedService<IClusterManifestProvider>("rpc"), // RPC service
        sp.GetRequiredService<ILoggerFactory>(),                     // Shared service
        sp.GetRequiredService<IOptions<SomeOptions>>()              // Shared config
    ));
```

### Pattern 3: Updating Consumers
```csharp
// When registering a class that needs the RPC version
services.TryAddSingleton<RpcGrainReferenceActivatorProvider>(sp => 
    new RpcGrainReferenceActivatorProvider(
        sp.GetRequiredService<IServiceProvider>(),
        sp.GetRequiredService<RpcProvider>(),
        sp.GetRequiredKeyedService<GrainPropertiesResolver>("rpc"), // Use RPC's version
        sp.GetRequiredService<GrainVersionManifest>(),
        sp.GetRequiredService<CodecProvider>(),
        sp.GetRequiredService<CopyContextPool>()
    ));
```

## Why Manual Construction?

1. **No Orleans Code Modification**: We can't modify Orleans classes to use `[FromKeyedServices]` attributes
2. **Selective Dependency Override**: Only override specific dependencies that need RPC versions
3. **Maintains Compatibility**: Orleans code continues to work unmodified with unkeyed services

## Implementation Status (Updated: 2025-06-22)

### Completed ✅
- [x] Remove keyed registration of RpcGrainFactory in DefaultRpcClientServices.cs
- [x] Remove keyed registration of RpcGrainFactory in DefaultRpcServerServices.cs  
- [x] Ensure GrainFactory/IGrainFactory/IInternalGrainFactory use correct pattern
- [x] Update Differences-from-Orleans.md with simplified approach
- [x] Update RpcClient to use keyed IGrainFactory service
- [x] Fix RpcGrainReferenceActivatorProvider to only handle RPC grains
- [x] Document triple-mode application considerations

### Pending Testing
- [ ] Test standalone RPC client scenario
- [ ] Test Orleans client + RPC server scenario (ActionServer)
- [ ] Verify no Orleans grain interception by RPC
- [ ] Test temporary RPC client resource cleanup

### Remaining Issues to Fix

1. **GrainPropertiesResolver Registration** ⚠️
   - Currently registered as unkeyed singleton in both client and server
   - Will use Orleans' manifest provider in triple-mode apps
   - Needs to be registered as keyed service with RPC's manifest provider

2. **GrainVersionManifest Registration** ⚠️
   - May have similar issues if used by RPC
   - Needs investigation

3. **Update RpcGrainReferenceActivatorProvider** ⚠️
   - Currently gets unkeyed GrainPropertiesResolver
   - Needs to be updated to use keyed version

## RPC Internal Service Usage

When RPC components need to use their own implementations (not Orleans'), they must use keyed services:

```csharp
// In RpcClient.cs - ensure we get RPC's grain factory, not Orleans'
var grainFactory = _serviceProvider.GetRequiredKeyedService<IGrainFactory>("rpc");
```

This is critical in scenarios like ActionServer where both Orleans client and RPC server are present.

## Impact of Current GrainPropertiesResolver Issue

In triple-mode applications (Orleans client + RPC server), the current implementation has this flow:

1. `RpcGrainReferenceActivatorProvider` is constructed with unkeyed `GrainPropertiesResolver`
2. `GrainPropertiesResolver` uses unkeyed `IClusterManifestProvider` (Orleans' provider)
3. When checking grain properties, RPC looks in Orleans' manifest instead of RPC's manifest
4. This could cause incorrect behavior if Orleans and RPC have different grain properties

**Example Scenario**:
- Orleans has grain `IWorldManagerGrain` with property `Unordered=false`
- RPC has grain `IGameRpcGrain` with property `Unordered=true`
- RPC checks properties for `IGameRpcGrain` but gets Orleans' manifest
- Property lookup fails or returns wrong values

## Notes

- The goal is to touch Orleans code as little as possible
- RPC should work standalone AND alongside Orleans
- Service registration order matters: Orleans first, then RPC
- TryAddSingleton is our friend for avoiding conflicts
- Manual construction pattern allows selective dependency override