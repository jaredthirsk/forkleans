# Granville RPC

A high-performance UDP-based RPC extension for Microsoft Orleans, designed for real-time applications like multiplayer games.

## Overview

Granville RPC extends Orleans with swappable UDP transports (LiteNetLib, Ruffles) to enable low-latency, high-throughput communication. Instead of Orleans' traditional clustered TCP model, Granville provides a decentralized approach where individual servers manage high-performance zones independently, with clients connecting directly to relevant RPC servers.

## Key Features

- **UDP-Based RPC**: Fast, unreliable communication suitable for real-time scenarios
- **Swappable Transports**: Runtime selection between LiteNetLib, Ruffles, and Orleans.TCP
- **Decentralized Architecture**: Zone-based server management without global clustering
- **Direct Client-Server Communication**: Clients connect to specific zone servers
- **Inter-Server RPC**: Servers communicate via the same UDP mechanism
- **Optional Orleans Integration**: Retain silos for coordination while using UDP for performance-critical paths

## Performance Goals

- **Sub-10ms latency** for RPC calls in local networks
- **10,000+ concurrent connections** per server with low CPU overhead
- **Tolerance for 5% packet loss** without application-breaking issues
- **Horizontal scaling** by adding independent servers for new zones

## Benchmarking Results

*Last Updated: July 15, 2025*

### Current Findings

Our comprehensive benchmarking framework has tested application-level performance across multiple transports:

#### FPS Game Simulation (100 clients, 60Hz updates)
- **Average Latency**: 9.6-9.9ms (consistent across all transports)
- **Throughput**: ~4,925-4,977 messages/second
- **Reliability**: 0% error rate, 0% packet loss
- **Best Performance**: Ruffles (9.63ms average, 4,977 msg/s)

#### MOBA Game Simulation (100 clients, 30Hz mixed messages)
- **Average Latency**: 13.7-13.8ms (consistent across all transports)
- **Throughput**: ~4,995-4,997 messages/second
- **Reliability**: 0% error rate, 0% packet loss
- **Best Performance**: Ruffles (13.74ms average, 4,997 msg/s)

#### Resource Utilization
- **UDP Transports**: 0.5-1.9% CPU usage
- **Orleans.TCP**: 2.4% CPU usage (higher overhead)
- **Memory**: ~80-95MB peak usage across all transports

### Current Limitations

The current benchmarks use simulated network delays (`Task.Delay`) rather than actual transport layers, which masks real performance differences. **Phase 1** of our roadmap focuses on implementing raw transport benchmarks to measure true network performance.

### Next Steps

1. **Raw Transport Benchmarks**: Direct ping-pong latency tests without simulation overhead
2. **Network Condition Testing**: Performance under various latency, packet loss, and bandwidth scenarios
3. **Advanced Analytics**: Detailed resource utilization and transport-specific metrics

See `/granville/benchmarks/docs/roadmap/BENCHMARKING-ROADMAP.md` for detailed benchmarking plans.

## Quick Start

### Basic Usage

```csharp
// Server setup with LiteNetLib transport
var builder = Host.CreateDefaultBuilder()
    .UseOrleans(siloBuilder =>
    {
        siloBuilder.UseGranvilleRpc(options =>
        {
            options.Transport = RpcTransportType.LiteNetLib;
            options.Port = 12345;
            options.MaxConnections = 1000;
        });
    });

var host = builder.Build();
await host.RunAsync();
```

```csharp
// Client connection
var client = new GranvilleRpcClient();
await client.ConnectAsync("127.0.0.1", 12345);

// Send unreliable message (position updates)
await client.SendUnreliableAsync(positionData);

// Send reliable message (game events)
await client.SendReliableAsync(gameEvent);
```

### Transport Selection

```csharp
// Runtime transport switching
services.Configure<GranvilleRpcOptions>(options =>
{
    options.Transport = RpcTransportType.LiteNetLib;  // or Ruffles, Orleans.TCP
    options.Reliable = false;                         // unreliable for speed
    options.MaxPacketSize = 1024;
});
```

## Architecture

### Zone-Based Server Model

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   Zone Server   │    │   Zone Server   │    │   Zone Server   │
│   (0,0) - (1,1) │    │   (1,0) - (2,1) │    │   (2,0) - (3,1) │
│                 │    │                 │    │                 │
│  UDP RPC Server │    │  UDP RPC Server │    │  UDP RPC Server │
│                 │    │                 │    │                 │
└─────────────────┘    └─────────────────┘    └─────────────────┘
         │                       │                       │
         └───────────────────────┼───────────────────────┘
                                 │
                    ┌─────────────────┐
                    │   Game Client   │
                    │                 │
                    │ Multi-Zone RPC  │
                    │   Connection    │
                    └─────────────────┘
```

### Transport Layers

- **LiteNetLib**: Optimized for games, minimal overhead
- **Ruffles**: Robust UDP with reliability options
- **Orleans.TCP**: Fallback to standard Orleans networking

## Project Structure

```
src/Rpc/
├── Orleans.Rpc.Abstractions/     # Core interfaces and types
├── Orleans.Rpc.Client/           # Client-side RPC implementation
├── Orleans.Rpc.Server/           # Server-side RPC implementation
├── Orleans.Rpc.Transport.LiteNetLib/  # LiteNetLib transport
├── Orleans.Rpc.Transport.Ruffles/     # Ruffles transport
├── Orleans.Rpc.Sdk/              # Code generation and tooling
└── docs/                         # Documentation and examples
```

## Example: Space Shooter Game

The `/granville/samples/Rpc/Shooter` demo showcases Granville RPC in action:

- **100 concurrent players** across multiple zones
- **60Hz position updates** via unreliable UDP
- **Game events** via reliable UDP
- **Zone transitions** with server handoff
- **Real-time multiplayer** with minimal latency

## Compatibility

- **Orleans Version**: Compatible with Orleans 9.x
- **Platforms**: Windows, Linux, macOS
- **.NET Version**: .NET 8+
- **Package Names**: `Granville.Rpc.*` to avoid conflicts with official Orleans packages

## Development Status

- ✅ **Core RPC Framework**: Complete
- ✅ **LiteNetLib Transport**: Functional
- ✅ **Ruffles Transport**: Functional
- ✅ **Client/Server APIs**: Stable
- ✅ **Demo Application**: Working
- 🔄 **Raw Transport Benchmarks**: In Progress
- 📋 **Documentation**: Ongoing

## Contributing

Granville RPC is part of the Orleans fork at `/mnt/g/forks/orleans/`. See the main repository for contribution guidelines.

## License

This project follows the same license as Microsoft Orleans (MIT License).

---

*For detailed technical documentation, see `/src/Rpc/docs/`*  
*For benchmarking information, see `/granville/benchmarks/`*  
*For sample applications, see `/granville/samples/Rpc/`*