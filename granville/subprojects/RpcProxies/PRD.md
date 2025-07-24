# RPC Proxies - Project Requirements Document

## Executive Summary

This project aims to migrate Granville RPC's dynamic proxy generation (using Reflection.Emit) to compile-time generated proxies, aligning with Orleans' code generation approach while maintaining RPC's unique requirements.

## Problem Statement

### Current Situation
When Granville RPC creates grain references, it uses `RpcGrainReference` which inherits from Orleans' `GrainReference` but doesn't implement the grain interfaces (e.g., `IGameRpcGrain`). This causes `InvalidCastException` when trying to cast the reference to the interface type.

### Why Dynamic Generation Was Needed
1. **Orleans Code Generation Disabled**: The Shooter sample has Orleans code generation disabled (`<Orleans_DesignTimeBuild>true</Orleans_DesignTimeBuild>`) to avoid conflicts with Granville's code generation
2. **No Proxy Registration**: Even when Orleans proxies exist, they aren't registered in `TypeManifestOptions.InterfaceProxies` when code generation is disabled
3. **RPC-Specific Requirements**: RPC needs its own proxy behavior to:
   - Route calls through UDP transport instead of Orleans' TCP
   - Use RPC's serialization with marker bytes
   - Maintain isolated serialization sessions

### Orleans' Expectation vs RPC Reality
Orleans expects:
- Grain references implement the grain interface
- Proxies are generated at compile-time and registered during startup
- All RPC calls go through Orleans' transport layer

RPC needs:
- Its own transport (UDP via LiteNetLib)
- Custom serialization handling
- Ability to coexist with Orleans grains in the same application

## Requirements

### Functional Requirements

1. **Compile-Time Proxy Generation**
   - Generate proxy classes during build, not runtime
   - Support all RPC grain interfaces
   - Maintain compatibility with existing RPC infrastructure

2. **Interface Implementation**
   - Proxies must implement the grain interfaces
   - Forward all method calls to `RpcGrainReference.InvokeRpcMethodAsync`
   - Support Task, Task<T>, and ValueTask return types
   - Handle IAsyncEnumerable<T> (future)

3. **Registration and Discovery**
   - Automatically register generated proxies at startup
   - Support both RPC-only and mixed Orleans/RPC scenarios
   - Provide clear separation between RPC and Orleans proxies

4. **Build Integration**
   - Integrate with MSBuild/Roslyn code generation
   - Support incremental compilation
   - Work with .NET SDK projects

### Non-Functional Requirements

1. **Performance**
   - No runtime reflection for method invocation
   - Minimal overhead compared to Orleans proxies
   - Efficient proxy type lookup

2. **Maintainability**
   - Clear separation from Orleans code generation
   - Easy to debug generated code
   - Well-documented generation process

3. **Compatibility**
   - Work alongside Orleans proxies when both are present
   - Support .NET 8+ 
   - Compatible with AOT compilation (future)

## Design Considerations

### Option 1: Extend Orleans Code Generator
- Modify Orleans' code generator to understand RPC attributes
- Generate proxies that inherit from RPC base classes
- Challenges: Tight coupling, upstream maintenance burden

### Option 2: Separate RPC Code Generator (Recommended)
- Create Granville.Rpc.CodeGenerator package
- Generate proxies specifically for RPC interfaces
- Use similar patterns to Orleans but independent implementation
- Benefits: Full control, no Orleans conflicts, cleaner separation

### Option 3: Source Generators
- Use Roslyn source generators for proxy generation
- More modern approach than traditional code generation
- Benefits: Better IDE integration, incremental compilation

## Success Criteria

1. All RPC grain references can be cast to their interfaces without exceptions
2. No runtime Reflection.Emit usage
3. Build time impact < 2 seconds for typical project
4. Generated code is debuggable and readable
5. Clear migration path from current dynamic generation

## Migration Strategy

1. **Phase 1**: Implement compile-time generator alongside dynamic generation
2. **Phase 2**: Update RpcProxyProvider to prefer compile-time proxies
3. **Phase 3**: Remove dynamic generation code
4. **Phase 4**: Optimize and document the new system

## Open Questions

1. Should we use traditional code generation or Roslyn source generators?
2. How do we handle versioning of generated proxies?
3. Should generated proxies be in a separate assembly or embedded?
4. How do we ensure compatibility with future Orleans versions?