# RPC Proxy Casting Fix (v128)

## Problem

RPC grain references couldn't be cast to their interfaces, causing `InvalidCastException`:

```csharp
var grain = grainFactory.GetGrain<IGameRpcGrain>(grainId);
// InvalidCastException: Unable to cast object of type 'RpcGrainReference' to type 'IGameRpcGrain'
```

## Root Cause

1. `RpcGrainReference` doesn't implement grain interfaces (e.g., `IGameRpcGrain`)
2. Orleans-generated proxies exist but aren't registered when Orleans code generation is disabled
3. RPC needs its own grain reference type for UDP transport and custom serialization

## Solution (v128)

Implemented dynamic proxy wrappers using Reflection.Emit:

1. **RpcInterfaceProxyFactory** - Generates proxy classes at runtime that:
   - Implement the grain interface
   - Wrap an RpcGrainReference instance
   - Forward all method calls to `InvokeRpcMethodAsync`

2. **RpcGrainReferenceActivatorProvider** - Updated to:
   - Detect RPC interfaces by naming convention
   - Create dynamic proxy wrappers
   - Return proxy instances that can be cast to interfaces

## Example

```csharp
// Before: InvalidCastException
var grain = grainFactory.GetGrain<IGameRpcGrain>(grainId);

// After: Works! Returns proxy that implements IGameRpcGrain
// Internally: new RpcProxy_IGameRpcGrain(rpcGrainReference)
```

## Future Work

This dynamic generation is temporary. Plan to migrate to compile-time proxy generation:
- See `/granville/subprojects/RpcProxies/PRD.md` for requirements
- See `/granville/subprojects/RpcProxies/TASKS.md` for implementation plan

## Related Issues

- Bot connection failures due to casting exceptions
- Orleans proxy registration when code generation is disabled
- RPC/Orleans coexistence in hybrid applications