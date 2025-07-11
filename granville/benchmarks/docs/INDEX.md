# Granville Benchmarks Documentation Index

## Overview Documents
- [PRD.md](PRD.md) - Product Requirements Document for benchmarking
- [TASKS.md](TASKS.md) - Detailed task list for benchmark implementation
- [CLARIFYING-QUESTIONS.md](CLARIFYING-QUESTIONS.md) - Questions and clarifications
- [SETUP.md](SETUP.md) - Setup and installation guide

## Architecture
- [README.md](../README.md) - Overview and project structure
- Configuration files in `/config/`:
  - `default.json` - Standard benchmark configuration
  - `stress.json` - Stress testing configuration  
  - `network-impaired.json` - Network impairment testing

## Implementation Status

### Completed
- ✅ Core metrics collection framework
- ✅ Workload abstraction and base classes
- ✅ FPS game simulation workload
- ✅ MOBA game simulation workload
- ✅ Benchmark runner and orchestrator
- ✅ Results exporter (JSON, CSV, Markdown)
- ✅ Visualization script with Chart.js
- ✅ Configuration system
- ✅ Basic project structure

### In Progress
- 🔄 Integration with actual RPC implementation
- 🔄 Network emulation implementation
- 🔄 Micro-benchmarks with BenchmarkDotNet

### TODO
- ❌ MMO-style workload implementation
- ❌ Stress test workloads
- ❌ Orleans TCP baseline comparisons
- ❌ Real UDP transport integration
- ❌ Security benchmarks (when implemented)
- ❌ Multi-zone server testing
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
- `NetworkEmulator.cs` - Network condition simulation (stub)

## Results and Analysis

Results are exported to `/results/` in multiple formats:
- JSON - Raw data for further processing
- CSV - For spreadsheet analysis
- Markdown - Human-readable summary with recommendations
- HTML - Interactive charts and visualizations

## Next Steps

1. **Integration**: Connect benchmarks to actual RPC implementation
2. **Validation**: Verify metrics collection accuracy
3. **Expansion**: Add more workload scenarios
4. **Automation**: CI/CD integration for performance regression testing