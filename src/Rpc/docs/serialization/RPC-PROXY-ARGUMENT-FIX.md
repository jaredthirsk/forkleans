# RPC Proxy Argument Serialization Fix

## Problem

When using Orleans-generated proxies with the RPC client, method arguments were not being properly captured and serialized. The server would receive null values instead of the actual arguments passed by the client.

### Symptoms
- Server receives null for method arguments (e.g., playerId in ConnectPlayer)
- Only 7 bytes serialized instead of full argument data
- IInvokable.GetArgumentCount() returns 0 in OutsideRpcRuntimeClient

### Root Cause
Orleans-generated proxy classes create IInvokable objects but don't properly initialize them with method arguments when used in the RPC client context. The Orleans proxy system expects a different initialization pattern than what the RPC client provides.

## Solution

Create an RpcProvider that prevents Orleans-generated proxies from being used for RPC grains, forcing the system to use RpcGrainReference instead.

### Implementation

1. **Created RpcProvider.cs**:
```csharp
namespace Granville.Rpc
{
    internal sealed class RpcProvider
    {
        public bool TryGet(GrainInterfaceType interfaceType, out Type proxyType)
        {
            // Always return false to force use of RpcGrainReference
            proxyType = null;
            return false;
        }
    }
}
```

2. **Updated RpcGrainReferenceActivatorProvider.cs**:
- When RpcProvider.TryGet returns false, create RpcGrainReferenceActivator
- This ensures all RPC grains use RpcGrainReference which properly handles argument serialization

### How It Works

1. RpcGrainReferenceActivatorProvider is registered as the first IGrainReferenceActivatorProvider
2. When creating a grain reference, it checks RpcProvider.TryGet()
3. Since it always returns false, RpcGrainReferenceActivator is used
4. RpcGrainReference instances are created with proper InvokeRpcMethodAsync methods
5. These methods correctly serialize arguments before sending RPC requests

### Benefits

- Arguments are properly captured and serialized
- No changes needed to Orleans proxy generation
- Clean separation between Orleans and RPC grain invocation patterns
- Maintains compatibility with existing code

## Future Improvements

1. **Selective Proxy Usage**: Implement logic in RpcProvider to selectively allow Orleans proxies for certain interfaces that don't use RPC
2. **Proxy Integration**: Fix Orleans proxy integration to properly initialize IInvokable with arguments
3. **Performance**: Investigate if custom proxies could provide better performance than reflection-based invocation

## Testing

The fix was tested with the Shooter sample where the bot successfully passes playerId arguments to the server. The server now receives the correct GUID values instead of null.