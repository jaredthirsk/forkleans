# Historical Troubleshooting Notes

This document contains troubleshooting notes from resolving various issues with Orleans RPC integration.

## Common Issues and Solutions

### 1. "No IClusterManifestProvider found" Error
- **Cause**: Conflicting service registrations
- **Solution**: Use keyed services for both RPC and Orleans providers

### 2. "No active nodes compatible with grain" Error
- **Cause**: Wrong manifest provider being used
- **Root Issue**: `GrainInterfaceTypeToGrainTypeResolver` getting RPC manifest instead of Orleans
- **Solution**: 
  1. Ensure RPC doesn't register as `IClusterClient`
  2. Fix keyed service resolution in RPC client
  3. Ensure correct manifest provider is used

### 3. Transport Connection Failures
- **Cause**: Missing transport factory registration
- **Solution**: Register appropriate transport factories as keyed services

### 4. Service Registration Conflicts
- **Cause**: Both RPC and Orleans registering same services
- **Fixed in Latest Version**:
  - RPC no longer registers as `IClusterClient`
  - `GrainInterfaceTypeToGrainTypeResolver` uses keyed RPC manifest provider
  - Prevents RPC from overriding Orleans services

## Investigation: "No active nodes compatible with grain proxy_iworldmanager"

### Timeline of Fixes Attempted

1. **Initial Issue**: ActionServer cannot connect to Silo's grains
2. **First Fix**: Updated RPC client/server to use keyed service registration for `IClusterManifestProvider`
3. **Second Fix**: Replaced `ConfigureApplicationParts` with `AddSerializer` (Orleans 7.0+ requirement)
4. **Third Fix**: Fixed ActionServerRegistrationService to use `IEnumerable<IClusterClient>` and select non-RPC client
5. **Fourth Fix**: Modified RPC to not register as `IClusterClient` and fixed keyed service resolution

### Key Discoveries

1. Orleans uses internal aliases like `proxy_iworldmanager` for `IWorldManagerGrain`
2. `GrainInterfaceTypeToGrainTypeResolver` is the critical component that depends on correct manifest provider
3. When both Orleans client and RPC server are used together, service registration conflicts occur
4. RPC was overriding Orleans client's `IClusterClient` registration

### Remaining Investigation Areas

- Other services that might be conflicting between Orleans and RPC
- Timing issues in service registration
- Manifest provider content differences

## Latest Investigation (Current)

### Services Most Likely to Conflict

Based on analysis, these services are registered by both Orleans and RPC:

1. **GrainFactory**: RPC registers RpcGrainFactory as GrainFactory
2. **IGrainFactory**: Both map from their respective GrainFactory
3. **IInternalGrainFactory**: Both map from their respective GrainFactory
4. **GrainReferenceActivator**: Single instance shared between both
5. **IGrainReferenceActivatorProvider**: Multiple providers, RPC adds its own first

### Diagnostic Service Added

To help diagnose service registration issues, added a DiagnosticService to ActionServer that logs:
- Which IGrainFactory implementation is registered
- Which IClusterClient implementation is registered
- Whether IRpcClient is available
- All registered IGrainReferenceActivatorProvider implementations
- Which IClusterManifestProvider is registered (both keyed and unkeyed)

### Current Fix Status

1. RPC no longer registers as `IClusterClient` (commented out)
2. RPC's `GrainInterfaceTypeToGrainTypeResolver` uses keyed manifest provider
3. RPC Server's `GrainInterfaceTypeToGrainTypeResolver` also fixed to use keyed manifest provider
4. GrainClassMap now registered as keyed service to avoid conflicts
5. Both systems still register GrainFactory - but diagnostic shows Orleans one is winning

### Diagnostic Results from ActionServer Log

From the diagnostic service output:
- ✓ `IGrainFactory type: Forkleans.GrainFactory` (Orleans, not RPC)
- ✓ `IClusterClient type: Forkleans.ClusterClient` (Orleans)
- ✓ `IRpcClient type: NULL` (RPC not overriding)
- ✗ `IClusterManifestProvider type: Forkleans.Rpc.RpcManifestProvider` (Wrong - RPC provider)
- ✗ `IClusterManifestProvider[orleans] type: NULL`
- ✗ `IClusterManifestProvider[rpc] type: NULL`

The core issue: RPC's manifest provider is being registered as the unkeyed IClusterManifestProvider