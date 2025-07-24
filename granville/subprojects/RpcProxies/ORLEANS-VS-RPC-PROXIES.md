# Orleans vs RPC Proxy Patterns

## Why Orleans' Pattern Doesn't Work for RPC

### Orleans' Proxy Pattern

Orleans generates proxy classes at compile-time that:
1. Inherit from `GrainReference` 
2. Implement the grain interface (e.g., `IMyGrain`)
3. Override `InvokeMethodAsync` to handle method calls
4. Are registered in `TypeManifestOptions.InterfaceProxies` during startup

### RPC's Requirements

RPC needs different behavior that conflicts with Orleans' assumptions:

1. **Transport Layer**
   - Orleans: TCP-based messaging through silos
   - RPC: UDP-based direct communication via LiteNetLib

2. **Serialization**
   - Orleans: Uses Orleans.Serialization with its own wire format
   - RPC: Requires marker bytes (0x00, 0xFE, 0xFF) and isolated sessions

3. **Routing**
   - Orleans: Grain location via directory, silo-to-silo messaging
   - RPC: Direct server connections, client-side routing

4. **Coexistence**
   - Need to support both Orleans and RPC grains in same application
   - Can't override Orleans' transport for all grains

### The Fundamental Conflict

Orleans expects all grain references to use its infrastructure:
```csharp
// Orleans pattern - all calls go through Orleans transport
public class Proxy_IMyGrain : GrainReference, IMyGrain
{
    public Task<string> SayHello(string name)
    {
        // This calls Orleans' InvokeMethodAsync which uses TCP transport
        return InvokeMethodAsync<string>(methodId, new object[] { name });
    }
}
```

RPC needs its own path:
```csharp
// RPC pattern - calls go through UDP transport
public class RpcProxy_IMyGrain : GrainReference, IMyGrain  
{
    private readonly RpcGrainReference _rpcRef;
    
    public Task<string> SayHello(string name)
    {
        // This must call RPC's InvokeRpcMethodAsync for UDP transport
        return _rpcRef.InvokeRpcMethodAsync<string>(methodId, new object[] { name });
    }
}
```

### Why We Can't Just Use Orleans Code Generation

1. **Method Routing**: Orleans-generated proxies call `GrainReference.InvokeMethodAsync`, which routes through Orleans' TCP transport. RPC needs UDP.

2. **Type Registration**: When Orleans code generation is disabled (to avoid conflicts), proxies aren't registered in `TypeManifestOptions.InterfaceProxies`.

3. **Serialization Context**: Orleans proxies use Orleans' serialization context. RPC needs isolated sessions with specific marker bytes.

4. **Dual Runtime**: In hybrid apps, some grains use Orleans (TCP) while others use RPC (UDP). A single proxy type can't handle both.

## The Solution Path

### Short Term: Dynamic Proxies (Current)
- Generate proxies at runtime with Reflection.Emit
- Wrap RpcGrainReference to implement interfaces
- Forward calls to RPC transport

### Long Term: RPC Code Generation
- Create `Granville.Rpc.CodeGenerator` separate from Orleans
- Generate RPC-specific proxies at compile-time
- Register in RPC's own type manifest
- Maintain clear separation from Orleans

### Key Design Principle

RPC and Orleans are **parallel systems** that can coexist, not a single unified system. They share:
- Interface definitions
- Grain identity concepts  
- Some serialization infrastructure

But they diverge on:
- Transport layer (UDP vs TCP)
- Proxy implementation
- Method invocation path
- Runtime behavior

This separation is intentional and necessary for RPC's performance and flexibility requirements.