# Granville RPC Benchmarks

This directory contains performance benchmarks for the Granville RPC extension to Orleans.

## Overview

These benchmarks evaluate the performance characteristics of Granville's UDP-based RPC compared to standard Orleans TCP communication, with a focus on game-like workloads.

## Project Structure

- `/src/` - Benchmark implementation code
  - `/Granville.Benchmarks.Core/` - Core benchmarking framework and utilities
  - `/Granville.Benchmarks.Micro/` - Micro-benchmarks using BenchmarkDotNet
  - `/Granville.Benchmarks.EndToEnd/` - End-to-end game simulation benchmarks
  - `/Granville.Benchmarks.Runner/` - Benchmark orchestration and execution
- `/results/` - Benchmark results and reports
- `/scripts/` - Automation scripts for running benchmarks
- `/config/` - Configuration files for different benchmark scenarios

## Key Metrics

- **Latency**: Average, p50, p95, p99 percentiles
- **Throughput**: Messages per second
- **CPU Usage**: Per-core and total utilization
- **Memory Usage**: Heap allocations and GC pressure
- **Network**: Packet loss recovery, bandwidth usage
- **Error Rates**: Failed RPCs, timeouts

## Transport Configurations

- LiteNetLib UDP (reliable and unreliable modes)
- Ruffles UDP (reliable and unreliable modes)
- Orleans TCP (baseline)

## Workload Scenarios

1. **FPS-style**: High-frequency position updates (60-120Hz)
2. **MOBA-style**: Mixed reliable/unreliable messages
3. **MMO-style**: Large player counts with zone transitions
4. **Stress Test**: Maximum throughput and connection scaling

## Getting Started

### Quick Start
```bash
# Build everything
cd granville/benchmarks
dotnet build -c Release

# Run basic benchmarks
pwsh ./scripts/run-microbenchmarks.ps1 -Quick

# Test network conditions (requires admin/sudo for system-level emulation)
pwsh ./scripts/test-network-conditions.ps1

# Run MMO scaling test
pwsh ./scripts/test-mmo-scaling.ps1
```

### Documentation
- **[SETUP.md](docs/SETUP.md)** - Complete setup and installation guide
- **[NETWORK-EMULATION.md](docs/NETWORK-EMULATION.md)** - Network condition testing framework
- **[INDEX.md](docs/INDEX.md)** - Complete documentation index and status
- **[TASKS.md](docs/TASKS.md)** - Implementation progress and remaining work

### Key Results
- **Overhead Measurement**: Granville RPC adds <2ms latency over raw transport
- **Network Resilience**: Performance across 10 realistic network condition profiles
- **Scalability**: MMO workload tested up to 2000 concurrent players
- **Hot Path APIs**: Zero-overhead direct transport access for performance-critical scenarios

## Performance Highlights

- **Low Overhead**: <2ms Granville RPC overhead vs pure LiteNetLib/Ruffles
- **Network Resilient**: Tested across 10 network profiles from Perfect to Satellite conditions
- **Game Optimized**: FPS, MOBA, and MMO workloads with realistic player interaction patterns
- **Hot Path Ready**: Direct transport access API for zero-overhead scenarios
- **Stress Tested**: 5 stress test scenarios including connection storms and error injection
- **Cross Platform**: Linux (tc), Windows (clumsy), and application-level network emulation