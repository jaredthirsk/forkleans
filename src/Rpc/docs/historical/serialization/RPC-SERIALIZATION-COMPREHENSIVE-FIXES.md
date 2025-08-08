# RPC Serialization Comprehensive Fixes

## Summary of Issues and Fixes

### 1. Server-Side Serialization Missing Marker Bytes
**Issue**: Server was not adding Orleans binary marker byte (0x00) to serialized responses
**Fix**: Updated `RpcConnection.ProcessRequest` to add marker byte before sending response

### 2. Client-Side Deserialization Expecting Marker Bytes
**Issue**: Client expected marker bytes but couldn't handle them properly
**Fix**: Updated `OutsideRpcRuntimeClient.DeserializePayload` to detect and remove marker bytes

### 3. Orleans Proxy Not Capturing Arguments
**Issue**: Orleans-generated proxies create empty argument arrays instead of capturing actual method arguments
**Root Cause**: `IInvokable` objects created by Orleans proxies don't initialize the Arguments property
**Workaround**: Created `RpcProxyProvider` that always returns false to force use of `RpcGrainReference`

### 4. Namespace Conflict with Orleans.GrainReferences.RpcProvider
**Issue**: Orleans Core has its own `RpcProvider` class in the same namespace
**Fix**: Renamed our class to `RpcProxyProvider` in the `Granville.Rpc` namespace

## Files Modified

1. `/src/Rpc/Orleans.Rpc.Server/RpcConnection.cs`
   - Added marker byte to server responses

2. `/src/Rpc/Orleans.Rpc.Client/OutsideRpcRuntimeClient.cs`
   - Added marker byte detection and removal in deserialization

3. `/src/Rpc/Orleans.Rpc.Client/RpcProvider.cs`
   - Renamed to `RpcProxyProvider`
   - Moved to `Granville.Rpc` namespace

4. `/src/Rpc/Orleans.Rpc.Client/RpcGrainReferenceActivatorProvider.cs`
   - Updated to use `RpcProxyProvider`

5. `/src/Rpc/Orleans.Rpc.Client/Hosting/DefaultRpcClientServices.cs`
   - Updated to register `RpcProxyProvider`

## Version Progression
- v116: Initial issue identified
- v117: Fixed marker byte handling
- v118: Fixed Orleans proxy issue
- v119: Added diagnostics
- v120: Fixed namespace conflict
- v121: Final clean version

## Testing Results
After all fixes were applied, the bot can successfully:
1. Connect to the RPC server
2. Send playerId argument correctly
3. Receive and deserialize server responses
4. Proceed to game loop

## Future Improvements
1. Fix Orleans proxy IInvokable initialization upstream
2. Add comprehensive RPC serialization tests
3. Implement better error messages for serialization mismatches