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

See the documentation in `/docs/` for detailed setup and execution instructions.