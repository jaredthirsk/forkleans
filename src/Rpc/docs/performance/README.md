# Granville RPC Performance Documentation

This directory contains comprehensive performance analysis and optimization guides for Granville RPC, specifically focused on game development scenarios.

## Quick Navigation

### 🎯 For Game Developers
- **[Memory Optimization Guide](MEMORY-OPTIMIZATION-GUIDE.md)** - Practical techniques for reducing memory allocations
- **[Hot Path Optimization](HOT-PATH-OPTIMIZATION.md)** - Abstraction levels and bypass techniques for high-frequency operations

### 📊 For Performance Engineers
- **[Memory Allocations Analysis](MEMORY-ALLOCATIONS.md)** - Detailed analysis of allocation patterns and optimization recommendations

### 🔧 For Contributors
- **[Benchmarking Guide](BENCHMARKING-GUIDE.md)** - How to run and interpret performance benchmarks *(TODO)*
- **[Profiling Tools](PROFILING-TOOLS.md)** - Memory and performance profiling setup *(TODO)*

## Performance Summary

### Current State (July 2025)
- **Full RPC Overhead**: ~1.0ms over raw transport
- **Memory per Small RPC**: 250KB allocated
- **Memory per Large RPC**: 10MB+ allocated
- **GC Pressure**: High (15-1200+ Gen0 collections per benchmark)

### Optimization Potential
- **Phase 1 Fixes**: 70-80% allocation reduction
- **Phase 2 Optimizations**: 90% allocation reduction
- **Phase 3 Hot Path**: Near-zero allocation for critical paths

## Key Findings

### Memory Allocation Hotspots
1. **JSON Serialization**: 5-40x allocation multiplier
2. **ArrayBufferWriter**: New instance per message
3. **Transport Layer**: Multiple byte array copies
4. **Event System**: Frequent event args allocation

### Game Development Impact
- **60Hz Updates**: Current implementation too expensive
- **10Hz State Sync**: Manageable with optimizations
- **Event-Driven**: Acceptable for rare operations

## Recommended Approach by Use Case

### Real-Time Updates (60Hz+)
```csharp
// Use direct transport bypass
var transport = rpcClient.GetDirectTransport();
transport.SendUnreliable(positionBytes);
```
- **Target**: 0 allocations
- **Implementation**: Direct transport access
- **Use for**: Position updates, input events

### Game State Sync (10-30Hz)
```csharp
// Use bypass API with pooling
await rpcClient.Bypass.SendReliableOrdered(pooledBuffer);
```
- **Target**: <100 bytes per operation
- **Implementation**: Bypass + object pooling
- **Use for**: Health updates, state synchronization

### Game Events (1-10Hz)
```csharp
// Use full RPC with optimization
await rpcClient.InvokeAsync<IGameService>(s => s.ProcessEvent(evt));
```
- **Target**: <1KB per operation
- **Implementation**: Full RPC + pooling
- **Use for**: Inventory, achievements, game logic

### System Events (Event-Driven)
```csharp
// Use full RPC - allocation acceptable
await rpcClient.InvokeAsync<IGameService>(s => s.OnPlayerJoined(player));
```
- **Target**: Any reasonable allocation
- **Implementation**: Full RPC convenience
- **Use for**: Chat, player join/leave, admin operations

## Optimization Roadmap

### ✅ Phase 1: Critical Fixes (Weeks 1-2)
- [ ] Implement `ArrayPool<byte>` for all byte arrays
- [ ] Add `ArrayBufferWriter` pooling
- [ ] Cache frequently used objects
- [ ] Eliminate unnecessary `ToArray()` calls

**Expected Impact**: 70-80% allocation reduction

### 🔄 Phase 2: Serialization Optimization (Weeks 3-6)
- [ ] Replace JSON with binary MessagePack
- [ ] Implement zero-copy serialization
- [ ] Add struct-based message types
- [ ] Optimize Orleans serialization integration

**Expected Impact**: 90% allocation reduction

### 📋 Phase 3: Hot Path Implementation (Weeks 7-10)
- [ ] Build allocation-free direct APIs
- [ ] Add message batching capabilities
- [ ] Implement zero-copy transport operations
- [ ] Create game-specific optimized paths

**Expected Impact**: Near-zero allocation for critical paths

### 🚀 Phase 4: Advanced Features (Weeks 11-16)
- [ ] Custom memory allocators
- [ ] SIMD-optimized serialization
- [ ] Lock-free data structures
- [ ] Hardware-specific optimizations

**Expected Impact**: Sub-microsecond overhead

## Benchmarking

### Available Benchmarks
- **`RpcLatencyBenchmark`**: Basic latency and allocation measurement
- **`RpcMemoryBenchmark`**: Detailed memory allocation analysis
- **End-to-End Benchmarks**: Real-world game scenarios

### Running Benchmarks
```bash
cd /granville/benchmarks
pwsh scripts/run-microbenchmarks.ps1
```

### Key Metrics to Monitor
- **Allocated Memory**: Total bytes allocated per operation
- **GC Frequency**: Collections per second by generation
- **Allocation Rate**: Bytes allocated per second
- **Hot Path Latency**: Time spent in critical operations

## Orleans Core Recommendations

### Proposed Changes
1. **Buffer Pooling**: Built-in `ArrayPool` integration
2. **Binary Serialization**: Alternative to JSON for performance
3. **Memory Diagnostics**: Enhanced allocation tracking
4. **Zero-Copy APIs**: Memory-efficient alternatives

### Integration Strategy
- Maintain backward compatibility
- Opt-in performance features
- Gradual migration path
- Enhanced telemetry

## Game Development Guidelines

### Architecture Recommendations
1. **Separate hot and cold paths**: Different optimization strategies
2. **Use appropriate abstractions**: Match frequency to API level
3. **Implement batching**: Reduce per-operation overhead
4. **Monitor continuously**: Track allocation patterns

### Common Pitfalls to Avoid
- Using full RPC for high-frequency updates
- Allocating arrays in hot paths
- Ignoring GC pressure warnings
- Premature optimization without profiling

### Performance Budgets
- **60Hz Operations**: 0 allocations
- **10Hz Operations**: <100 bytes
- **1Hz Operations**: <1KB
- **Event Operations**: <10KB

## Contributing

### Adding New Benchmarks
1. Create benchmark in `/granville/benchmarks/src/Granville.Benchmarks.Micro/`
2. Use `[MemoryDiagnoser]` attribute
3. Include baseline comparisons
4. Document findings in this directory

### Performance Analysis Process
1. Identify performance issue
2. Create focused benchmark
3. Analyze allocation patterns
4. Propose optimization strategy
5. Implement and validate
6. Update documentation

### Documentation Standards
- Include benchmark data
- Provide practical examples
- Show before/after comparisons
- Update roadmap status

## Resources

### External Documentation
- [BenchmarkDotNet Documentation](https://benchmarkdotnet.org/)
- [.NET Memory Performance](https://docs.microsoft.com/en-us/dotnet/standard/garbage-collection/performance)
- [Orleans Performance Guide](https://docs.microsoft.com/en-us/dotnet/orleans/host/monitoring/)

### Internal References
- [Granville RPC Architecture](../README.md)
- [Transport Layer Documentation](../transport/)
- [Serialization Guide](../serialization/)
- [Orleans Integration](../orleans-integration/)

---

*Performance analysis framework established: July 16, 2025*  
*Next review: August 16, 2025*  
*Maintainer: Granville RPC Performance Team*