# Tasks

- [ ] **Research Phase**
  - [ ] Review existing Orleans networking (TCP-based) for baseline metrics.
  - [ ] Study UDP libraries (LiteNetLib, Ruffles) for integration patterns.
  - [ ] Identify game-specific workloads (e.g., player movement, entity synchronization).
  - [ ] Gather tools for benchmarking (e.g., .NET performance counters, custom simulators).

- [ ] **Implementation Phase**
  - [ ] Fork and setup repository with InternalsVisibleTo additions.
  - [ ] Integrate UDP transports as swappable modules.
    - [ ] Implement LiteNetLib wrapper for RPC.
    - [ ] Implement Ruffles wrapper for RPC.
  - [ ] Modify grain messaging to use UDP RPC.
    - [ ] Add zone management logic for servers.
    - [ ] Enable inter-server RPC.
  - [ ] Add configuration for reliability and serialization.

- [ ] **Testing Phase**
  - [ ] Unit tests for RPC calls over UDP.
    - [ ] Test client-server interactions.
    - [ ] Test inter-server communication.
  - [ ] Integration tests with optional silos.
  - [ ] Stress tests simulating packet loss and high load.
  - [ ] Functional tests for game scenarios (e.g., mock multiplayer session).

- [ ] **Benchmarking Phase**
  - [ ] Setup benchmark environment (local cluster, cloud instances).
  - [ ] Measure latency for RPC calls (UDP vs. TCP).
    - [ ] Test with 100, 1000, 5000 concurrent calls.
  - [ ] Measure throughput (messages/sec) under load.
  - [ ] Evaluate reliability with induced packet loss (1%, 5%).
  - [ ] Compare against standard Orleans in game-like simulations.
  - [ ] Analyze CPU/memory impact.
  - [ ] Document results in reports with graphs/tables.

- [ ] **Optimization and Iteration Phase**
  - [ ] Identify and fix bottlenecks from benchmarks.
  - [ ] Iterate on UDP configurations for better performance.
  - [ ] Re-run benchmarks after optimizations.