# RPC Proxies - Project Requirements Document

## Executive Summary

Enable RPC grain references to be cast to their interfaces, replacing the current dynamic (Reflection.Emit) solution with a sustainable approach.

## Project Goals

1. **Enable interface casting** for RPC grain references
2. **Reuse existing infrastructure** where possible
3. **Maintain separation** between RPC and Orleans transport layers
4. **Optimize for simplicity** - minimize new code and complexity

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

### Option 1: Custom IGrainReferenceRuntime (NEW - Recommended)
- Implement RpcGrainReferenceRuntime to intercept proxy calls
- Reuse Orleans-generated proxies completely
- Route calls through RPC UDP transport instead of Orleans TCP
- Benefits: Minimal code, reuses Orleans infrastructure, simpler maintenance

### Option 2: Separate RPC Code Generator
- Create Granville.Rpc.CodeGenerator package
- Generate proxies specifically for RPC interfaces
- Use similar patterns to Orleans but independent implementation
- Challenges: Code duplication, maintenance burden, complexity

### Option 3: Extend Orleans Code Generator
- Modify Orleans' code generator to understand RPC attributes
- Generate proxies that inherit from RPC base classes
- Challenges: Tight coupling, upstream maintenance burden, fork divergence

## Success Criteria

1. All RPC grain references can be cast to their interfaces without exceptions
2. No runtime Reflection.Emit usage
3. Build time impact < 2 seconds for typical project
4. Generated code is debuggable and readable
5. Clear migration path from current dynamic generation

## Migration Strategy (Option 1 - Recommended)

1. **Phase 1**: Enable Orleans code generation in RPC projects
2. **Phase 2**: Implement RpcGrainReferenceRuntime 
3. **Phase 3**: Configure DI to use RPC runtime for RPC interfaces
4. **Phase 4**: Remove dynamic proxy generation code
5. **Phase 5**: Document and optimize

## Open Questions

1. How do we detect RPC interfaces vs Orleans interfaces at runtime?
2. Should RpcGrainReference continue to extend GrainReference?
3. How do we handle mixed RPC/Orleans scenarios in the same app?
4. Can we maintain backward compatibility during migration?

## Implementation Notes

The key insight is that Orleans proxies already do exactly what we need - they implement the grain interface and forward calls to the runtime. By implementing our own IGrainReferenceRuntime, we can intercept these calls and route them through RPC's UDP transport instead of Orleans' TCP transport. This approach:

- Eliminates need for separate proxy generation
- Reuses Orleans' battle-tested proxy code
- Maintains clean separation of transport concerns
- Simplifies our codebase significantly