# Orleans Extension Opportunity: Granville RPC

## Executive Summary

Granville RPC represents a natural extension to Orleans that enables direct RPC communication patterns while leveraging Orleans' robust infrastructure. By adding minimal InternalsVisibleTo attributes to Orleans, the community could benefit from a unified programming model that supports both grain-based and RPC-based communication patterns.

## Current Situation

The core issue is that Granville.Rpc needs access to Orleans internal types (like `IRuntimeClient`), but the official Microsoft.Orleans packages don't expose these internals. This is exactly the scenario where official `InternalsVisibleTo` support would be beneficial.

## Recommended Strategy for Orleans Integration

### 1. **Demonstrate Minimal Orleans Modifications**

The only changes needed in Orleans are InternalsVisibleTo attributes:

```csharp
// In Orleans.Core AssemblyInfo.cs:
[assembly: InternalsVisibleTo("Granville.Rpc.Server")]
[assembly: InternalsVisibleTo("Granville.Rpc.Client")]
[assembly: InternalsVisibleTo("Granville.Rpc.Abstractions")]
[assembly: InternalsVisibleTo("Granville.Rpc.Sdk")]
```

### 2. **Value Proposition for Orleans Community**

#### Unified Programming Model
- Developers can use grains for stateful actors and RPC for stateless services
- Both patterns share the same serialization, networking, and cluster infrastructure
- Reduces learning curve for teams already using Orleans

#### Performance Benefits
- RPC provides lower latency for stateless operations
- Avoids grain activation overhead for simple request/response patterns
- Ideal for high-frequency, low-latency scenarios (e.g., game servers, real-time systems)

#### Ecosystem Growth
- Opens Orleans to new use cases that require direct communication
- Attracts developers from traditional RPC frameworks (gRPC, WCF)
- Enables hybrid architectures combining the best of both patterns

#### Minimal Maintenance Burden
- RPC extension maintained separately from Orleans core
- Changes to Orleans automatically benefit RPC (serialization improvements, performance optimizations)
- No additional complexity in Orleans codebase

### 3. **Compelling Demo Architecture**

The Shooter sample demonstrates complex real-world scenarios:

- **Silo**: Orleans host managing game world state
- **ActionServer**: RPC server + RPC client + Orleans client (showing the trifecta)
  - RPC server: Handles real-time player actions
  - RPC client: Communicates with other ActionServers
  - Orleans client: Queries/updates world state in grains
- **Client**: Web client showing end-user experience
- **Bot**: Automated testing showing IAsyncEnumerable and observers

### 4. **Technical Integration Points**

Granville RPC leverages Orleans infrastructure:
- **Serialization**: Uses Orleans.Serialization for consistent data formats
- **Networking**: Builds on Orleans' connection management
- **Cluster Awareness**: Integrates with Orleans membership for service discovery
- **Configuration**: Follows Orleans configuration patterns
- **Logging/Telemetry**: Uses Orleans instrumentation

### 5. **Proposed Implementation Path**

1. **Phase 1**: Demonstrate proof of concept with modified packages
2. **Phase 2**: Submit PR to Orleans with InternalsVisibleTo additions
3. **Phase 3**: Contribute RPC extension as separate NuGet packages
4. **Phase 4**: Create documentation and samples for the community

### 6. **Benefits to Microsoft/Orleans Team**

- **Expanded Use Cases**: Orleans becomes viable for more scenarios
- **Community Contribution**: High-quality extension maintained by community
- **Competitive Advantage**: Orleans offers both actor model and RPC patterns
- **Low Risk**: Minimal changes to Orleans core, extension maintained separately

## Why This Matters

Modern distributed systems often need both patterns:
- **Grains**: Perfect for game entities, user sessions, shopping carts
- **RPC**: Ideal for API gateways, real-time updates, stateless computations

By officially supporting RPC extensions, Orleans becomes a complete platform for building distributed systems, not just actor-based systems.

## Call to Action

The Orleans team should consider:
1. Adding the minimal InternalsVisibleTo attributes
2. Establishing guidelines for Orleans extensions
3. Creating an official extensions repository or registry

This small change would unlock significant innovation in the Orleans ecosystem while maintaining the core framework's stability and focus.

## Demonstration

The Shooter sample (available at github.com/jaredthirsk/orleans) shows:
- Multi-silo Orleans cluster with UFX SignalR backplane
- Multiple RPC ActionServers handling real-time game logic
- Seamless integration between grains and RPC services
- Complex patterns like IAsyncEnumerable and grain observers
- Production-ready architecture for latency-sensitive applications

This is not just a toy example - it represents real architectural patterns needed in gaming, financial services, IoT, and other latency-sensitive domains.