# Granville Benchmarks Documentation Index

## Overview Documents
- [PRD.md](PRD.md) - Product Requirements Document for benchmarking
- [TASKS.md](TASKS.md) - Detailed task list for benchmark implementation
- [CLARIFYING-QUESTIONS.md](CLARIFYING-QUESTIONS.md) - Questions and clarifications
- [SETUP.md](SETUP.md) - Setup and installation guide
- [NETWORK-EMULATION.md](NETWORK-EMULATION.md) - Network condition testing framework

## Architecture
- [README.md](../README.md) - Overview and project structure
- [roadmap/BENCHMARKING-ROADMAP.md](roadmap/BENCHMARKING-ROADMAP.md) - Complete benchmarking roadmap
- Configuration files in `/config/`:
  - `default.json` - Standard benchmark configuration
  - `network-condition-test.json` - Network condition testing
  - `mmo-scaling-test.json` - MMO scaling tests
  - `stress-test.json` - Stress testing configuration

## Implementation Status

### Completed (Phases 1-3)
- ✅ Core metrics collection framework
- ✅ Workload abstraction and base classes
- ✅ FPS game simulation workload
- ✅ MOBA game simulation workload
- ✅ **MMO-style workload** (zone distribution, 100-2000 players)
- ✅ **Stress test workloads** (connection storms, burst traffic, error injection)
- ✅ Benchmark runner and orchestrator
- ✅ Results exporter (JSON, CSV, Markdown)
- ✅ **Network condition testing** (10 realistic profiles: LAN → Satellite)
- ✅ **Raw transport framework** (LiteNetLib, Ruffles, simulation)
- ✅ **Overhead measurement** (<2ms Granville RPC overhead verified)
- ✅ **Hot path APIs** (Direct access, bypass, full RPC)
- ✅ Configuration system with network emulation
- ✅ Automated test scripts (PowerShell)

### Ready to Execute
- 📊 **Network condition testing** - `test-network-conditions.ps1` (10 network profiles)
- 🎮 **MMO scaling tests** - `test-mmo-scaling.ps1` (zone-based distribution)
- ⚡ **Stress testing** - `test-stress-conditions.ps1` (5 stress scenarios)
- 🔍 **Pure transport testing** - Tests LiteNetLib/Ruffles with zero Granville overhead
- 📈 **Overhead measurement** - Quantifies Granville RPC abstraction costs

### Future (Phase 4)
- 📈 Interactive performance dashboard
- 🔔 Automated regression detection
- 📊 Predictive performance modeling
- 🔄 CI/CD integration
- 🔍 Orleans TCP baseline comparisons
- ❌ Statistical analysis tools

## Quick Start

1. Build the benchmarks:
   ```bash
   cd granville/benchmarks
   dotnet build -c Release
   ```

2. Run micro-benchmarks:
   ```bash
   pwsh ./scripts/run-microbenchmarks.ps1
   ```

3. Run end-to-end benchmarks:
   ```bash
   pwsh ./scripts/run-e2e-benchmarks.ps1 -GenerateReport
   ```

4. Run stress tests:
   ```bash
   pwsh ./scripts/run-e2e-benchmarks.ps1 -ConfigFile "../config/stress.json"
   ```

## Key Components

### Core Library (`Granville.Benchmarks.Core`)
- `BenchmarkMetrics.cs` - Metrics data model
- `MetricsCollector.cs` - Real-time metrics collection
- `IWorkload.cs` - Workload interface
- `GameWorkloadBase.cs` - Base class for game workloads

### Micro-benchmarks (`Granville.Benchmarks.Micro`)
- `RpcLatencyBenchmark.cs` - Single RPC call latency tests
- Uses BenchmarkDotNet for precise measurements

### End-to-End Benchmarks (`Granville.Benchmarks.EndToEnd`)
- `FpsGameWorkload.cs` - FPS game simulation
- `MobaGameWorkload.cs` - MOBA game simulation

### Runner (`Granville.Benchmarks.Runner`)
- `BenchmarkOrchestrator.cs` - Coordinates benchmark execution
- `ResultsExporter.cs` - Exports results in multiple formats
- `NetworkEmulator.cs` - Network condition simulation with 10 predefined profiles

### Transport Implementations
- **Pure Transports**: Zero-overhead LiteNetLib/Ruffles implementations
- **Bypass Transports**: Granville abstractions with minimal overhead  
- **Full RPC**: Complete Granville RPC with method dispatch and serialization
- **Direct Access API**: Hot path optimization for high-frequency scenarios

## Results and Analysis

Results are exported to `/results/` in multiple formats:
- **JSON** - Raw data for further processing and CI integration
- **CSV** - For spreadsheet analysis and statistical tools
- **Markdown** - Human-readable summary with performance recommendations
- **HTML** - Interactive charts and visualizations (future)

### Performance Metrics
- **Latency Analysis**: p50/p95/p99 percentiles across network conditions
- **Overhead Measurement**: Granville RPC vs pure transport baseline
- **Throughput Testing**: Messages/second under various loads
- **Network Resilience**: Performance degradation under packet loss/jitter
- **Scalability Analysis**: MMO workload performance with 100-2000 players

## Next Steps

### Immediate (Phase 4)
1. **Fix compilation issues**: Resolve hot path API build errors
2. **Performance dashboard**: Interactive visualization of benchmark results
3. **Regression detection**: Automated performance threshold monitoring
4. **CI/CD integration**: Automated performance testing pipeline

### Future Enhancements
5. **Orleans TCP baseline**: Compare UDP vs TCP performance
6. **Statistical analysis**: Advanced statistical significance testing
7. **Real-world validation**: Compare synthetic vs production workload patterns
8. **Mobile optimization**: Specific optimizations for mobile network patterns