# RPC Proxies - Implementation Tasks

## Phase 1: Research and Design

### 1.1 Analyze Orleans Proxy Generation
- [ ] Study Orleans.CodeGenerator implementation
  - [ ] Understand how Orleans identifies interfaces to generate
  - [ ] Learn how Orleans generates proxy classes
  - [ ] Document Orleans' proxy registration mechanism
- [ ] Identify what we can reuse vs what needs custom implementation
- [ ] Document findings in `docs/orleans-proxy-analysis.md`

### 1.2 Evaluate Code Generation Approaches
- [ ] Traditional MSBuild Code Generation
  - [ ] Pros/cons analysis
  - [ ] Integration complexity
  - [ ] Build performance impact
- [ ] Roslyn Source Generators
  - [ ] Pros/cons analysis
  - [ ] .NET version requirements
  - [ ] Incremental compilation support
- [ ] Hybrid approach feasibility
- [ ] Make recommendation in `docs/codegen-approach-recommendation.md`

### 1.3 Design RPC Proxy Generation
- [ ] Define RPC interface detection strategy
  - [ ] Attribute-based marking (e.g., `[RpcGrainInterface]`)
  - [ ] Naming convention (e.g., `*RpcGrain`)
  - [ ] Configuration-based selection
- [ ] Design proxy class structure
  - [ ] Base class (inherit from GrainReference or custom)
  - [ ] Interface implementation pattern
  - [ ] Method forwarding mechanism
- [ ] Plan registration and discovery
  - [ ] Compile-time manifest generation
  - [ ] Runtime registration API
  - [ ] Integration with RpcProxyProvider

## Phase 2: Prototype Implementation

### 2.1 Create Granville.Rpc.CodeGenerator Project
- [ ] Set up project structure
  - [ ] Create Orleans.Rpc.CodeGenerator project
  - [ ] Configure as MSBuild task or source generator
  - [ ] Add necessary dependencies
- [ ] Implement basic code generation
  - [ ] Interface scanning
  - [ ] Proxy class generation
  - [ ] Basic method forwarding

### 2.2 Implement Core Generation Logic
- [ ] Interface Analysis
  - [ ] Parse interface methods
  - [ ] Handle generic methods
  - [ ] Support async patterns (Task, ValueTask)
  - [ ] Plan for IAsyncEnumerable support
- [ ] Proxy Generation
  - [ ] Generate constructor
  - [ ] Generate interface implementation
  - [ ] Generate method forwarding logic
  - [ ] Handle special cases (properties, events)
- [ ] Metadata Generation
  - [ ] Generate proxy manifest
  - [ ] Include version information
  - [ ] Support incremental updates

### 2.3 Build Integration
- [ ] MSBuild Integration
  - [ ] Create build targets
  - [ ] Configure in Directory.Build.targets
  - [ ] Handle incremental builds
- [ ] NuGet Package
  - [ ] Package structure
  - [ ] Build props/targets files
  - [ ] Package dependencies

## Phase 3: Integration with RPC Runtime

### 3.1 Update RpcProxyProvider
- [ ] Add compile-time proxy lookup
  - [ ] Load generated proxy manifest
  - [ ] Implement proxy type resolution
  - [ ] Fall back to dynamic generation (temporarily)
- [ ] Performance optimization
  - [ ] Cache proxy type lookups
  - [ ] Minimize reflection usage

### 3.2 Update RpcGrainReferenceActivatorProvider
- [ ] Integrate with compile-time proxies
  - [ ] Detect when compile-time proxy exists
  - [ ] Use compile-time proxy instead of dynamic
  - [ ] Maintain compatibility mode

### 3.3 Testing Infrastructure
- [ ] Unit tests for code generator
- [ ] Integration tests for generated proxies
- [ ] Performance benchmarks
- [ ] Compatibility tests with Orleans

## Phase 4: Migration and Cleanup

### 4.1 Migrate Existing Samples
- [ ] Update Shooter sample
  - [ ] Enable RPC code generation
  - [ ] Remove dynamic proxy workarounds
  - [ ] Verify functionality
- [ ] Create migration guide
  - [ ] Step-by-step instructions
  - [ ] Common issues and solutions
  - [ ] Performance comparison

### 4.2 Remove Dynamic Generation
- [ ] Mark dynamic generation as obsolete
- [ ] Add migration warnings
- [ ] Remove Reflection.Emit code
- [ ] Update documentation

### 4.3 Documentation and Polish
- [ ] API documentation
- [ ] Architecture documentation
- [ ] Troubleshooting guide
- [ ] Performance tuning guide

## Phase 5: Advanced Features

### 5.1 Enhanced Functionality
- [ ] Support for IAsyncEnumerable methods
- [ ] Custom serialization attributes
- [ ] Interceptor support
- [ ] Telemetry integration

### 5.2 Tooling
- [ ] Visual Studio integration
- [ ] Debugging support for generated code
- [ ] Diagnostic analyzers
- [ ] Code fixes for common issues

### 5.3 Performance Optimizations
- [ ] Zero-allocation method calls
- [ ] Struct-based proxies (where applicable)
- [ ] AOT compilation support
- [ ] Trimming support

## Technical Debt Items

- [ ] Investigate why Orleans proxies aren't registered when codegen is disabled
- [ ] Document the relationship between RPC and Orleans proxy systems
- [ ] Create comprehensive test suite for edge cases
- [ ] Performance profiling and optimization
- [ ] Security review of generated code

## Success Metrics

- [ ] All RPC interfaces have compile-time proxies
- [ ] Zero runtime reflection for proxy creation
- [ ] Build time increase < 2 seconds
- [ ] 100% backward compatibility
- [ ] No performance regression vs dynamic proxies