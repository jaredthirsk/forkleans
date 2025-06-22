# Orleans and RPC Coexistence Strategy

## Overview

This document outlines the strategy for ensuring Orleans client and RPC server can coexist in the same application without service registration conflicts.

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

## Current Issues to Fix

### 1. Remove Unnecessary Keyed Registration

The current code has this pattern which is unnecessary:
```csharp
// WRONG - RpcGrainFactory doesn't need keying
services.AddKeyedSingleton<RpcGrainFactory>("rpc", ...);
```

Should be:
```csharp
// RIGHT - RpcGrainFactory is RPC-specific, no conflict
services.TryAddSingleton<RpcGrainFactory>(...);
```

### 2. Simplify Registration Order

Current implementation is overly complex. Simplify to:
1. Register RPC-specific types normally
2. Register Orleans interfaces with RPC implementations as keyed
3. Use TryAdd for unkeyed fallbacks for standalone mode

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

## RPC Internal Service Usage

When RPC components need to use their own implementations (not Orleans'), they must use keyed services:

```csharp
// In RpcClient.cs - ensure we get RPC's grain factory, not Orleans'
var grainFactory = _serviceProvider.GetRequiredKeyedService<IGrainFactory>("rpc");
```

This is critical in scenarios like ActionServer where both Orleans client and RPC server are present.

## Notes

- The goal is to touch Orleans code as little as possible
- RPC should work standalone AND alongside Orleans
- Service registration order matters: Orleans first, then RPC
- TryAddSingleton is our friend for avoiding conflicts