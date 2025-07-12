# Granville Product Requirements Document (PRD)

## 1. Product Overview
Granville is a friendly extension to Microsoft Orleans, designed as a fork that preserves the upstream codebase largely intact while introducing swappable RPC capabilities over UDP libraries such as LiteNetLib and Ruffles. This extension shifts away from Orleans' traditional clustered silo model toward a more decentralized approach where individual servers manage high-performance zones independently. Clients connect directly to relevant RPC servers, and servers can communicate via RPC with each other. Orleans silos may optionally be retained for application-specific coordination between clients and servers. The primary focus is enabling efficient, low-latency RPC for high-performance games, leveraging UDP for fast, unreliable communication suited to real-time scenarios.

Granville aims to explore whether this UDP-based RPC layer can provide a useful and efficient alternative to standard Orleans networking, particularly for gaming applications requiring high throughput and minimal latency, without compromising the core actor model (grains) of Orleans.

## 2. Goals and Objectives
- **Primary Goal**: Extend Orleans with UDP-based RPC to support high-performance, real-time applications like multiplayer games, evaluating its efficiency in terms of latency, throughput, and reliability.
- **Objectives**:
  - Maintain compatibility with upstream Orleans to minimize disruption and allow easy merging of future updates.
  - Introduce swappable UDP transports (e.g., LiteNetLib, Ruffles) for flexibility in networking choices.
  - Eliminate dependency on clustered silos for core RPC, assigning zone responsibility to individual servers.
  - Enable peer-to-peer-like RPC between servers and optional integration with Orleans silos for coordination.
  - Benchmark performance against standard Orleans TCP-based communication to validate suitability for games.
  - Ensure the extension is modular, with changes limited to adding InternalsVisibleTo attributes for new DLL projects.

## 3. Target Audience
- Game developers building high-performance, real-time multiplayer systems in .NET.
- Developers familiar with Orleans seeking faster networking options for distributed applications.
- Researchers or hobbyists exploring actor models with custom transports for low-latency scenarios.

## 4. Features
### Core Features
- **UDP-Based RPC Layer**: Implement RPC over UDP using libraries like LiteNetLib or Ruffles, with runtime swapping between transports.
- **Decentralized Server Model**: Each server handles specific high-performance zones; no global clustering required.
- **Client-Server Connectivity**: Clients connect directly to one or more RPC servers based on zone interests.
- **Inter-Server RPC**: Servers communicate via the same UDP RPC mechanism for cross-zone interactions.
- **Optional Orleans Integration**: Retain silos for coordination (e.g., discovery, load balancing) without mandating them for RPC.

### Additional Features
- **Serialization Optimization**: Support efficient serialization for game data (e.g., positions, events) to minimize overhead.
- **Reliability Options**: Configurable reliability modes (e.g., unreliable for updates, reliable for critical messages) built on UDP.
- **Error Handling and Retries**: Built-in mechanisms for handling packet loss in game contexts.
- **Monitoring and Diagnostics**: Extend Orleans' tools to monitor UDP RPC performance metrics like latency and packet loss.

## 5. Technical Requirements
- **Platform**: .NET (compatible with Orleans' requirements, e.g., .NET 8+).
- **Dependencies**:
  - Microsoft Orleans (forked upstream).
  - UDP Libraries: LiteNetLib, Ruffles (integrated as swappable modules).
  - No additional package installations; use built-in or bundled libraries.
- **Integration Points**:
  - Add InternalsVisibleTo to Orleans assemblies for access by Granville DLLs.
  - Hook into Orleans' grain activation and messaging pipeline for RPC overrides.
- **API Changes**:
  - New interfaces/configurations for UDP transport selection and zone management.
  - Minimal changes to existing Orleans APIs to preserve compatibility.

## 6. Non-Functional Requirements
- **Performance**: Achieve sub-10ms latency for RPC calls in local networks; handle 10,000+ concurrent connections per server with low CPU overhead.
- **Scalability**: Support horizontal scaling by adding independent servers for new zones.
- **Reliability**: Provide at-least-once delivery options for critical RPCs; tolerate up to 5% packet loss without game-breaking issues.
- **Security**: Basic authentication for RPC connections; no encryption by default (defer to application layer for games).
- **Compatibility**: Run on Windows, Linux, macOS; integrate seamlessly with existing Orleans projects.

## 7. Assumptions and Dependencies
- Assumptions:
  - Users have basic knowledge of Orleans and .NET distributed systems.
  - UDP is suitable for target games, where occasional loss is acceptable (e.g., position updates).
  - Benchmarking will use synthetic game workloads (e.g., simulated players).
- Dependencies:
  - Upstream Orleans updates can be merged with minimal conflicts.
  - UDP libraries remain maintained and performant.

## 8. Risks
- **Performance Bottlenecks**: UDP implementation may introduce unexpected overhead in serialization or retries.
- **Compatibility Issues**: Changes could break upstream merges if Orleans evolves significantly.
- **Adoption**: Limited appeal if benchmarks show insufficient gains over TCP for games.
- **Security**: UDP's unreliability may exacerbate denial-of-service risks in public games.
- Mitigation: Conduct early benchmarks and provide configurable fallbacks to TCP.

## 9. High-Level Timeline
- Phase 1: Design and Prototyping (1-2 months).
- Phase 2: Implementation and Integration (2-3 months).
- Phase 3: Testing and Benchmarking (1-2 months).
- Phase 4: Documentation and Release (1 month).

# Hierarchical Task List for Benchmarking

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

# Clarifying Questions Document

1. What specific game features (e.g., FPS, MOBA) are you targeting, and what are the expected RPC call frequencies (e.g., 60Hz updates)?
2. Are there any pre-implemented parts in the fork (e.g., prototype UDP integration) beyond the docs folder?
3. What reliability guarantees are needed for different RPC types (e.g., fire-and-forget for positions, reliable for scores)?
4. Do you have preferred benchmarking tools or metrics (e.g., focus on latency percentiles like p99)?
5. Any constraints on .NET versions or platforms for the extension?
6. How should zone discovery work (e.g., manual config, service registry)?
7. Are there plans for encryption over UDP, or is it application-specific?
8. What upstream Orleans features must remain untouched?