# RPC Proxy Argument Serialization Fix

## Problem

When using Orleans-generated proxies with the RPC client, method arguments were not being properly captured and serialized. The server would receive null values instead of the actual arguments passed by the client.

### Symptoms
- Server receives null for method arguments (e.g., playerId in ConnectPlayer)
- Only 7 bytes serialized instead of full argument data
- IInvokable.GetArgument(index) returns null even though arguments were passed

### Root Cause
Orleans-generated proxy classes create IInvokable objects and populate fields like `arg0`, `arg1`, etc. with the method arguments. However, the `GetArgument(int index)` method implementation doesn't properly return these field values, always returning null instead.

## Initial Approach (Doesn't Work)

The initial approach was to disable Orleans proxies entirely by making RpcProvider always return false. However, this doesn't work because:
- RpcGrainReference doesn't implement the specific grain interfaces (e.g., IGameRpcGrain)
- Client code expects to cast the grain reference to the interface type
- Without the proxy, the cast fails with an InvalidCastException

## Actual Solution

Fix the argument extraction in RpcGrainReferenceRuntime.GetMethodArguments() to use reflection when GetArgument returns null.

### Implementation

Updated **RpcGrainReferenceRuntime.cs**:
```csharp
private object[] GetMethodArguments(IInvokable invokable)
{
    var argumentCount = invokable.GetArgumentCount();
    var arguments = new object[argumentCount];
    
    for (int i = 0; i < argumentCount; i++)
    {
        arguments[i] = invokable.GetArgument(i);
        
        // If GetArgument returns null, try to get the value via reflection
        // This is a workaround for Orleans-generated proxies that don't properly implement GetArgument
        if (arguments[i] == null && argumentCount > 0)
        {
            var fieldName = $"arg{i}";
            var field = invokable.GetType().GetField(fieldName, 
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            
            if (field != null)
            {
                arguments[i] = field.GetValue(invokable);
            }
        }
    }
    
    return arguments;
}
```

### How It Works

1. Orleans-generated proxy creates an IInvokable with fields `arg0`, `arg1`, etc. populated with actual values
2. When RpcGrainReferenceRuntime calls `GetArgument(i)`, it returns null (Orleans bug)
3. Our fix detects the null return and uses reflection to read the field value directly
4. The correct argument values are then passed to the RPC transport

### Benefits

- Maintains full interface compatibility (proxies implement the grain interfaces)
- Arguments are properly extracted and serialized
- No changes needed to Orleans proxy generation
- Simple, localized fix in one method
- Can be easily removed if Orleans fixes the issue upstream

## Testing

The fix was tested with the Shooter sample where the bot successfully passes playerId arguments to the server. The server now receives the correct GUID values instead of null.