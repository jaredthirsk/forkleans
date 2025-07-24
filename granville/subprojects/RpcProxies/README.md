# RPC Proxy System Overview

## The Problem

When using Granville RPC, grain references cannot be cast to their interfaces:

```csharp
var grain = grainFactory.GetGrain<IGameRpcGrain>(grainId);
// InvalidCastException: Unable to cast 'RpcGrainReference' to 'IGameRpcGrain'
```

## What We Tried

### Attempt 1: Use Orleans-Generated Proxies
**Why it failed:**
- Orleans code generation is disabled in Shooter sample to avoid type conflicts
- Even when proxies exist, they aren't registered in `TypeManifestOptions.InterfaceProxies`
- Orleans proxies route through TCP; RPC needs UDP transport

### Attempt 2: Enable Orleans Proxies for RPC
**Why it failed:**
- Orleans proxies inherit from `GrainReference` and call `InvokeMethodAsync`
- This routes through Orleans' TCP transport, not RPC's UDP
- Can't override transport behavior without breaking Orleans grains

### Attempt 3: Delegate to Orleans' RpcProvider
**Why it failed:**
- `RpcProvider.TryGet()` returns false when codegen is disabled
- No way to register proxies without enabling full Orleans codegen
- Would still have transport mismatch even if registration worked

## What We Implemented (v128)

**Dynamic Proxy Generation** - A temporary solution using Reflection.Emit:

```csharp
// RpcInterfaceProxyFactory generates at runtime:
public class DynamicProxy_IGameRpcGrain : GrainReference, IGameRpcGrain
{
    private RpcGrainReference _rpcRef;
    
    public Task<Result> Method(Args args) 
    {
        // Forward to RPC transport, not Orleans
        return _rpcRef.InvokeRpcMethodAsync<Result>(methodId, new[] { args });
    }
}
```

This works but has issues:
- Runtime overhead
- Hard to debug
- Incompatible with AOT
- Complex Reflection.Emit code

## What We Want To Do

**Compile-Time RPC Proxy Generation** - The proper solution:

1. **Create Granville.Rpc.CodeGenerator**
   - Separate from Orleans code generation
   - Generate RPC-specific proxies at build time
   - Register in RPC's own type system

2. **Key Design Principles**
   - RPC and Orleans are parallel systems, not one unified system
   - Each has its own transport (UDP vs TCP)
   - Each has its own proxy generation
   - They can coexist in the same application

3. **Generated Proxy Pattern**
   ```csharp
   [GeneratedCode("Granville.Rpc.CodeGenerator", "1.0")]
   public class RpcProxy_IGameRpcGrain : GrainReference, IGameRpcGrain
   {
       // Compile-time generated, optimized for RPC transport
   }
   ```

## Why RPC Needs Its Own Proxy System

| Aspect | Orleans | RPC |
|--------|---------|-----|
| Transport | TCP via Silo messaging | UDP via LiteNetLib |
| Serialization | Orleans binary format | RPC format with marker bytes |
| Routing | Silo directory | Client-side server selection |
| Proxy Base | GrainReference.InvokeMethodAsync | RpcGrainReference.InvokeRpcMethodAsync |
| Use Case | Distributed state/actors | High-performance game networking |

## Next Steps

1. Implement compile-time proxy generation (see `TASKS.md`)
2. Migrate Shooter sample to use generated proxies
3. Remove dynamic proxy code
4. Document patterns for hybrid Orleans/RPC applications

## Key Insight

RPC isn't trying to replace Orleans' proxy system - it needs its own parallel implementation. Just as RPC has its own transport layer, it needs its own proxy generation that integrates with that transport. The two systems share grain concepts but diverge in implementation by necessity.