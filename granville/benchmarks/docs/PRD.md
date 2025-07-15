# Granville Benchmarking Product Requirements Document (PRD)

## 1. Product Overview
Granville is a friendly fork of Microsoft Orleans, keeping the upstream codebase basically intact while extending it with RPC capabilities using swappable UDP libraries such as LiteNetLib and Ruffles. The only meaningful change to Orleans is adding InternalsVisibleTo attributes for new DLL projects. This extension introduces a more traditional RPC approach: no clustered silo model, where each server is solely responsible for its high-performance zone(s); clients connect directly to one or more RPC servers based on zone interests; RPC servers communicate via RPC with each other; and Orleans silos may optionally be used for application-specific coordination between clients and servers.

This benchmarking PRD focuses on evaluating the performance, efficiency, and suitability of Granville's UDP-based RPC for high-performance games. Since the core implementation is mostly complete (excluding security), benchmarking will validate whether this approach offers advantages in latency, throughput, and reliability over standard Orleans TCP, particularly under game-like workloads.

## 2. Goals and Objectives
- **Primary Goal**: Quantify the performance overhead that Granville RPC adds on top of raw transport libraries (LiteNetLib, Ruffles) to ensure minimal abstraction cost while providing RPC features.
- **Secondary Goal**: Compare Granville's UDP RPC against upstream Orleans TCP to determine viability for real-time gaming applications.
- **Objectives**:
  - **Overhead Measurement**: Precisely measure latency/throughput overhead of Granville RPC vs. raw LiteNetLib/Ruffles usage
  - **Transport Comparison**: Compare swappable UDP transports (LiteNetLib vs. Ruffles) for optimal selection in games
  - **Abstraction Level Analysis**: Evaluate performance trade-offs between full RPC vs. lower-level transport abstractions
  - **Hot Path Optimization**: Identify opportunities for direct transport API access in performance-critical scenarios
  - **Game Scenario Validation**: Simulate high-performance game scenarios to assess real-world efficiency
  - **Bottleneck Identification**: Provide data for future optimizations and minimal-overhead design decisions

## 3. Target Audience
- Game developers evaluating Granville for multiplayer systems requiring low-latency RPC.
- .NET distributed systems engineers comparing UDP vs. TCP in actor models.
- The project maintainer (jaredthirsk) for validating the fork's extensions.

## 4. Features
### Core Benchmarking Features
- **Overhead Measurement Suite**: Benchmarks comparing identical workloads across abstraction levels:
  1. **Raw Transport**: Direct LiteNetLib/Ruffles API usage (baseline)
  2. **Granville Raw Transport**: Granville's IRawTransport abstraction
  3. **Granville RPC**: Full RPC with serialization and method calls
- **Abstraction Level Testing**: Evaluate performance of different API layers:
  - **Direct Transport Methods**: Raw "Unreliable", "Reliable Ordered", "Unreliable Ordered" calls
  - **Transport Channel Access**: Multi-channel reliable ordering with direct channel APIs
  - **Full RPC Stack**: Complete method invocation with serialization overhead
- **Workload Simulations**: Synthetic game traffic generators for player movements, entity synchronization, and event broadcasting
- **Comparison Modes**: Run tests across all abstraction levels with identical message patterns
- **Configurable Parameters**: Vary connection counts, message frequencies, packet loss rates, and zone configurations
- **Metrics Collection**: Automated logging of latency (average, p99), throughput (messages/sec), error rates, and resource utilization

### Additional Features
- **Visualization Tools**: Generate charts/graphs for metric comparisons (e.g., latency histograms).
- **Stress Testing**: Simulate peak loads (e.g., 10,000+ concurrent clients) and network impairments.
- **Reliability Testing**: Evaluate handling of unreliable UDP with configurable retry/reliability layers.
- **Reporting**: Generate comprehensive reports with raw data, summaries, and recommendations.

## 5. Technical Requirements
- **Environment**: .NET 8+ runtime; test on Windows/Linux servers; local network or cloud (e.g., Azure/AWS) for distributed setups.
- **Dependencies**:
  - Granville fork (current implementation).
  - Benchmarking libraries: BenchmarkDotNet, NBench, or custom harnesses.
  - Network simulation tools: tc (Linux) or similar for inducing latency/loss.
  - Monitoring: Prometheus, Grafana, or .NET diagnostics tools.
- **Integration Points**:
  - Hook into Granville's RPC pipeline for instrumentation.
  - Use Orleans' existing logging/telemetry where possible.
- **API/Setup**:
  - Benchmark scripts/configs in the granville/docs or a new benchmarks folder.

## 6. Non-Functional Requirements
- **Performance Metrics Targets**:
  - **Overhead Target**: Granville RPC should add <2ms latency vs. raw transport usage
  - **Raw Transport Latency**: <1ms for local UDP calls; <5ms under simulated WAN conditions
  - **Full RPC Latency**: <5ms for local RPC calls; <20ms under simulated WAN conditions
  - **Throughput**: >10,000 messages/sec per server at each abstraction level
  - **Resilience**: Maintain <1% error rate with 5% packet loss across all abstraction levels
- **Scalability**: Test up to 100 servers/zones and 50,000 clients
- **Reproducibility**: All benchmarks scripted with seedable randomness
- **Duration**: Individual tests <30 minutes; full suite <4 hours
- **Data Integrity**: Collect at least 10 runs per configuration for statistical significance
- **Overhead Precision**: Measure overhead with microsecond-level accuracy to detect sub-millisecond differences

## 7. Assumptions and Dependencies
- Assumptions:
  - Granville's UDP RPC is functional for basic calls; any bugs will be noted/fixed during setup.
  - Game workloads focus on frequent, small messages (e.g., position updates at 60Hz).
  - Benchmarks run on hardware with consistent network conditions.
- Dependencies:
  - Access to the forkleans repo for building/running.
  - No major upstream Orleans changes affecting compatibility.

## 8. Risks
- **Inaccurate Simulations**: Workloads may not fully mimic real games, leading to misleading results.
- **Environmental Variability**: Network fluctuations could skew metrics.
- **Tool Overhead**: Benchmarking libraries might add artificial latency.
- Mitigation: Use controlled environments, multiple runs, and baseline corrections.

## 9. High-Level Timeline
- Phase 1: Setup and Planning (1-2 weeks).
- Phase 2: Implementation of Benchmark Harness (2-3 weeks).
- Phase 3: Execution and Analysis (3-4 weeks).
- Phase 4: Reporting and Iteration (1-2 weeks).
