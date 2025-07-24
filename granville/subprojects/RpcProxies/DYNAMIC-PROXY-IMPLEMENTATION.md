# Dynamic Proxy Implementation (v128)

## Current Implementation

Uses Reflection.Emit to generate proxy classes at runtime that:
1. Implement the grain interface (e.g., `IGameRpcGrain`)
2. Wrap an `RpcGrainReference` instance
3. Forward all method calls to `RpcGrainReference.InvokeRpcMethodAsync`

## The Solution

### RpcInterfaceProxyFactory

Creates dynamic proxy classes at runtime using `Reflection.Emit`:

```csharp
public static Type GetOrCreateProxyType(Type grainInterfaceType)
{
    return _proxyTypeCache.GetOrAdd(grainInterfaceType, CreateProxyType);
}

private static Type CreateProxyType(Type interfaceType)
{
    // Generate a class that:
    // 1. Implements the grain interface
    // 2. Contains a private RpcGrainReference field
    // 3. Forwards all method calls to RpcGrainReference.InvokeRpcMethodAsync
}
```

### RpcInterfaceProxyActivator

Wraps `RpcGrainReference` instances in the dynamic proxy:

```csharp
public GrainReference CreateReference(GrainId grainId)
{
    // Create the underlying RpcGrainReference
    var rpcGrainRef = new RpcGrainReference(...);
    
    // Wrap it in the proxy that implements the interface
    return (GrainReference)_proxyCtor.Invoke(new object[] { rpcGrainRef });
}
```

### Method Forwarding

The generated proxy forwards all interface method calls to `RpcGrainReference.InvokeRpcMethodAsync`:

```csharp
// Generated IL code effectively does:
public Task<TResult> SomeMethod(TArg arg)
{
    return _grainReference.InvokeRpcMethodAsync<TResult>(
        methodId: GetMethodId("SomeMethod"),
        arguments: new object[] { arg }
    );
}
```

## Limitations

1. **Performance**: Runtime code generation has overhead
2. **Debugging**: Generated code is harder to debug
3. **AOT**: Incompatible with ahead-of-time compilation
4. **Maintenance**: Complex Reflection.Emit code
5. **IAsyncEnumerable**: Not yet supported

## Files Modified

1. `/src/Rpc/Orleans.Rpc.Client/RpcInterfaceProxyFactory.cs` - New file for dynamic proxy generation
2. `/src/Rpc/Orleans.Rpc.Client/RpcGrainReferenceActivatorProvider.cs` - Updated to use dynamic proxies
3. `/src/Rpc/Orleans.Rpc.Client/RpcProvider.cs` - Returns false to force RPC proxy usage

## Migration Path

This dynamic solution is temporary. The plan is to:
1. Implement compile-time proxy generation (like Orleans)
2. Generate proxies during build
3. Register them at startup
4. Remove all Reflection.Emit code

See `PRD.md` and `TASKS.md` for the complete migration plan.