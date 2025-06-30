# Orleans and RPC Coexistence Summary

## Overview

This document summarizes the work done to enable Orleans client and RPC to coexist in the same application (like ActionServer).

## Problem Statement

When an application uses both Orleans client and RPC server/client (triple-mode), service registration conflicts occur because both systems implement the same Orleans interfaces:
- `IGrainFactory` / `GrainFactory`
- `IClusterManifestProvider`
- `GrainInterfaceTypeToGrainTypeResolver`

Without proper isolation, RPC services could override Orleans services, causing Orleans grain calls to fail.

## Solution Implemented

### 1. Keyed Service Registration Strategy

RPC now uses keyed services for all Orleans interfaces it implements:

```csharp
// RPC registration pattern
services.TryAddSingleton<RpcGrainFactory>(...);  // RPC-specific type, no conflict
services.AddKeyedSingleton<GrainFactory>("rpc", sp => sp.GetRequiredService<RpcGrainFactory>());
services.AddKeyedSingleton<IGrainFactory>("rpc", (sp, key) => sp.GetRequiredKeyedService<GrainFactory>("rpc"));

// Standalone fallback
services.TryAddSingleton<GrainFactory>(sp => sp.GetRequiredService<RpcGrainFactory>());
```

### 2. RPC Internal Service Usage

RPC components that need their own implementations use keyed services:

```csharp
// In RpcClient.cs
var grainFactory = _serviceProvider.GetRequiredKeyedService<IGrainFactory>("rpc");
```

### 3. Grain Reference Activator Fix

`RpcGrainReferenceActivatorProvider` now only handles RPC grains:

```csharp
public bool TryGet(GrainType grainType, GrainInterfaceType interfaceType, out IGrainReferenceActivator activator)
{
    // Only handle grains that have RPC proxy types
    if (_rpcProvider.TryGet(interfaceType, out var proxyType))
    {
        // ... handle RPC grain
        return true;
    }
    
    // Let Orleans handle its own grains
    activator = null;
    return false;
}
```

## Files Modified

1. `/src/Rpc/Orleans.Rpc.Client/Hosting/DefaultRpcClientServices.cs`
   - Implemented keyed service registration for client services

2. `/src/Rpc/Orleans.Rpc.Server/Hosting/DefaultRpcServerServices.cs`
   - Implemented keyed service registration for server services

3. `/src/Rpc/Orleans.Rpc.Client/RpcClient.cs`
   - Updated to use keyed IGrainFactory service

4. `/src/Rpc/Orleans.Rpc.Client/RpcGrainReferenceActivatorProvider.cs`
   - Fixed to only handle RPC grains, not Orleans grains

## Remaining Considerations

### 1. Service Registration Order
- Orleans client must be registered before RPC services
- This ensures Orleans unkeyed services take precedence
- ActionServer's Program.cs has a comment documenting this

### 2. Shared Services
Some services remain shared between Orleans and RPC:
- `GrainReferenceActivator` - Single instance, works correctly
- `GrainPropertiesResolver` - Uses unkeyed manifest provider
- Multiple `IGrainReferenceActivatorProvider` - Order matters

### 3. Temporary RPC Clients
ActionServer creates temporary RPC clients for server-to-server communication.
These should be properly disposed to avoid resource leaks.

## Testing Recommendations

1. **Standalone RPC Mode**: Verify RPC works without Orleans
2. **Triple-Mode (ActionServer)**: Verify both Orleans and RPC grains work
3. **Resource Cleanup**: Monitor for memory leaks from temporary clients
4. **Service Resolution**: Add diagnostic logging to verify correct services are used

## Version

These changes are included in Forkleans version 9.2.0.9-preview3 and later.