# ActionServer Triple-Mode Issues and Recommendations

## Overview
ActionServer operates in three modes simultaneously:
1. Orleans Client (connects to Orleans silo)
2. RPC Server (hosts RPC grains)
3. RPC Client (connects to other ActionServers)

## Current Status (Updated: 2025-06-22)

### Completed Fixes

1. **✅ Service Registration Conflicts Resolved**
   - Implemented keyed service registration for all conflicting Orleans interfaces
   - RPC uses keyed services ("rpc") while Orleans remains unkeyed
   - Updated `RpcClient` to use `GetRequiredKeyedService<IGrainFactory>("rpc")`
   - See: `/src/Rpc/Orleans.Rpc.Client/Hosting/DefaultRpcClientServices.cs`
   - See: `/src/Rpc/Orleans.Rpc.Server/Hosting/DefaultRpcServerServices.cs`

2. **✅ RpcGrainReferenceActivatorProvider Fixed**
   - Changed from claiming ALL grain types to only RPC grains
   - Now returns `false` for non-RPC grains, allowing Orleans to handle them
   - Fixed in: `/src/Rpc/Orleans.Rpc.Client/RpcGrainReferenceActivatorProvider.cs`

### Remaining Issues

### 1. ~~RpcGrainReferenceActivatorProvider Too Greedy~~ ✅ FIXED

**Previous Issue**: The provider claimed to handle ALL grain types.

**Status**: Fixed - Now only handles grains with RPC proxy types:
```csharp
public bool TryGet(GrainType grainType, GrainInterfaceType interfaceType, out IGrainReferenceActivator activator)
{
    // Only handle grains that have RPC proxy types
    if (_rpcProvider.TryGet(interfaceType, out var proxyType))
    {
        // ... create RPC activator
        return true;
    }
    
    // No RPC proxy type found - let other providers handle this grain
    activator = null;
    return false;
}
```

### 2. Temporary RPC Client Resource Leaks

**Issue**: Server-to-server communication creates temporary hosts without disposal:
```csharp
var hostBuilder = Host.CreateDefaultBuilder()
    .UseOrleansRpcClient(...)
    .Build();
// No using statement or disposal!
```

**Fix**: Implement proper disposal pattern:
```csharp
using var host = Host.CreateDefaultBuilder()
    .UseOrleansRpcClient(...)
    .Build();
await host.StartAsync();
try 
{
    var rpcClient = host.Services.GetRequiredService<IClusterClient>();
    // ... use client
}
finally
{
    await host.StopAsync();
}
```

### 3. Shared GrainPropertiesResolver Issue ⚠️ HIGH PRIORITY

**Issue**: Both Orleans and RPC use the same `GrainPropertiesResolver` instance, which uses the unkeyed `IClusterManifestProvider`. This means:
- Orleans grains get properties from Orleans manifest ✓
- RPC grains get properties from Orleans manifest ✗ (should use RPC manifest)

**Current Impact**:
- `RpcGrainReferenceActivatorProvider` gets wrong grain properties
- Could cause incorrect `InvokeMethodOptions` (e.g., Unordered flag)
- May affect other grain metadata lookups

**Fix Required**:
```csharp
// Register GrainPropertiesResolver as keyed service
services.AddKeyedSingleton<GrainPropertiesResolver>("rpc", (sp, key) => 
    new GrainPropertiesResolver(
        sp.GetRequiredKeyedService<IClusterManifestProvider>("rpc")
    ));

// Update RpcGrainReferenceActivatorProvider constructor
services.TryAddSingleton<RpcGrainReferenceActivatorProvider>(sp => 
    new RpcGrainReferenceActivatorProvider(
        // ... other params
        sp.GetRequiredKeyedService<GrainPropertiesResolver>("rpc"), // Use RPC version
        // ... other params
    ));
```

See `/src/Rpc/docs/COEXISTENCE-DETAILS.md` for full dependency analysis.

### 4. Service Registration Order Fragility

**Issue**: The comment "Configure Orleans client BEFORE RPC" indicates fragile ordering dependency.

**Fix**: Make registration more robust:
- Use `TryAddSingleton` consistently
- Document exactly which services have ordering dependencies
- Consider adding runtime validation to detect misconfigurations

## Summary of Changes Made

### Service Registration Strategy
1. **Keyed Services**: RPC now uses keyed services for all Orleans interfaces it implements
2. **Unkeyed Fallbacks**: TryAddSingleton provides standalone RPC support
3. **RPC Internal Usage**: RPC components use `GetRequiredKeyedService` to ensure correct resolution

### Key Files Modified
- `/src/Rpc/Orleans.Rpc.Client/Hosting/DefaultRpcClientServices.cs`
- `/src/Rpc/Orleans.Rpc.Server/Hosting/DefaultRpcServerServices.cs`
- `/src/Rpc/Orleans.Rpc.Client/RpcClient.cs`
- `/src/Rpc/Orleans.Rpc.Client/RpcGrainReferenceActivatorProvider.cs`

## Remaining Tasks

1. **High Priority**: Fix `GrainPropertiesResolver` to use keyed service with RPC manifest
   - Register as keyed service in both client and server
   - Update `RpcGrainReferenceActivatorProvider` to use keyed version
   - Test grain property resolution in triple-mode

2. **High Priority**: Fix temporary RPC client disposal in ActionServer
   - Add proper using/disposal pattern
   - Test for resource leaks

3. **Medium Priority**: Investigate other Orleans classes that may need keying
   - `GrainVersionManifest` if used by RPC
   - `GrainBindingsResolver` if RPC uses extensions

4. **Low Priority**: Add runtime validation for service registration order
   - Detect misconfiguration early
   - Provide clear error messages

## Testing Scenarios

1. **Test Orleans grain calls from ActionServer**
   - Verify Orleans grains resolve correctly
   - Verify Orleans manifest is used

2. **Test RPC grain hosting in ActionServer**
   - Verify RPC grains activate correctly
   - Verify RPC manifest is used

3. **Test server-to-server RPC calls**
   - Verify no resource leaks
   - Monitor memory usage during transfers

4. **Test grain type conflicts**
   - Create same interface name in Orleans and RPC
   - Verify correct resolution based on client type