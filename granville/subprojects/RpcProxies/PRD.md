# RPC Proxies - Project Requirements Document

## Executive Summary

Implement compile-time proxy generation for Granville RPC to replace the current dynamic (Reflection.Emit) solution.

## Project Goals

1. **Generate RPC proxies at compile-time** (not runtime)
2. **Maintain complete separation** from Orleans proxy system
3. **Enable seamless coexistence** of RPC and Orleans grains
4. **Optimize for performance** - zero runtime reflection

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