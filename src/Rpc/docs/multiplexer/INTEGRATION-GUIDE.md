# RPC Client Multiplexer Integration Guide

This guide explains how to integrate the RPC Client Multiplexer into existing applications.

## Overview

The RPC Client Multiplexer replaces direct `RpcClient` usage to enable:
- Multiple concurrent server connections
- Seamless server transitions
- Zone-aware and service-based routing
- Connection health monitoring

## Migration Steps

### 1. Update Dependencies

Add the multiplexer namespace:
```csharp
using Granville.Rpc.Multiplexing;
using Granville.Rpc.Multiplexing.Strategies;
using Granville.Rpc.Hosting;
```

### 2. Replace RpcClient with Multiplexer

#### Before (Direct RpcClient):
```csharp
// Old approach - single server connection
var hostBuilder = Host.CreateDefaultBuilder()
    .UseOrleansRpcClient(rpcBuilder =>
    {
        rpcBuilder.ConnectTo(host, port);
        rpcBuilder.UseLiteNetLib();
    })
    .Build();

await hostBuilder.StartAsync();
var rpcClient = hostBuilder.Services.GetRequiredService<IRpcClient>();
var grain = rpcClient.GetGrain<IMyGrain>("key");
```

#### After (Multiplexer):
```csharp
// New approach - multiple server support
var services = new ServiceCollection();
services.AddLogging();
services.AddSerializer(/* ... */);

// Add multiplexer with configuration
services.AddRpcClientMultiplexerWithBuilder(options =>
{
    options.EnableHealthChecks = true;
    options.HealthCheckInterval = TimeSpan.FromSeconds(30);
})
.UseZoneBasedRouting(); // Or other routing strategy

var provider = services.BuildServiceProvider();
var multiplexer = provider.GetRequiredService<IRpcClientMultiplexer>();

// Register servers
multiplexer.RegisterServer(new ServerDescriptor
{
    ServerId = "server1",
    HostName = "host1",
    Port = 12001,
    IsPrimary = true,
    Metadata = new Dictionary<string, string> { ["zone"] = "1,0" }
});

// Get grains through multiplexer
var grain = await multiplexer.GetGrainAsync<IMyGrain>("key");
```

### 3. Implement Zone-Aware Grains

For zone-based routing, implement `IZoneAwareGrain`:
```csharp
public interface IGameGrain : IRpcGrainInterface, IZoneAwareGrain
{
    // Your grain methods
}
```

### 4. Configure Routing Strategies

#### Zone-Based Routing:
```csharp
services.AddRpcClientMultiplexerWithBuilder()
    .UseZoneBasedRouting();

// When getting grains, provide zone context:
var context = new RoutingContext();
context.SetProperty("Zone", "1,0");
var grain = await multiplexer.GetGrainAsync<IGameGrain>("key", context);
```

#### Service-Based Routing:
```csharp
services.AddRpcClientMultiplexerWithBuilder()
    .UseServiceBasedRouting(strategy =>
    {
        strategy.MapService<IChatGrain>("chat-server");
        strategy.MapService<IGameGrain>("game-server");
    });
```

#### Composite Routing:
```csharp
services.AddRpcClientMultiplexerWithBuilder()
    .UseCompositeRouting(composite =>
    {
        // Zone routing for zone-aware grains
        composite.AddStrategy(
            type => typeof(IZoneAwareGrain).IsAssignableFrom(type),
            new ZoneBasedRoutingStrategy(logger));
        
        // Service routing for specific services
        var serviceStrategy = new ServiceBasedRoutingStrategy(logger);
        serviceStrategy.MapService<IChatGrain>("chat-server");
        composite.AddStrategy(
            type => type == typeof(IChatGrain),
            serviceStrategy);
        
        // Default strategy for everything else
        composite.SetDefaultStrategy(new PrimaryServerStrategy());
    });
```

### 5. Handle Dynamic Server Discovery

```csharp
// Periodically discover new servers
private async void DiscoverServers()
{
    var newServers = await GetAvailableServers();
    foreach (var server in newServers)
    {
        if (!multiplexer.IsServerRegistered(server.Id))
        {
            multiplexer.RegisterServer(new ServerDescriptor
            {
                ServerId = server.Id,
                HostName = server.Host,
                Port = server.Port,
                Metadata = server.Metadata
            });
        }
    }
}
```

### 6. Error Handling and Reconnection

The multiplexer handles reconnection automatically, but you can monitor health:
```csharp
// Check server health
var servers = multiplexer.GetRegisteredServers();
foreach (var server in servers)
{
    if (server.HealthStatus == ServerHealthStatus.Unhealthy)
    {
        _logger.LogWarning("Server {ServerId} is unhealthy", server.ServerId);
    }
}

// Force health check
await multiplexer.CheckServerHealthAsync("server1");
```

## Shooter Game Integration Example

See `MultiplexedRpcGameClientService.cs` for a complete example that:
1. Discovers servers from a service catalog
2. Registers multiple zone-based servers
3. Implements custom routing for zone transitions
4. Handles dynamic server discovery
5. Maintains persistent connections

Key patterns from the Shooter example:
```csharp
// Custom routing strategy
private class ShooterRoutingStrategy : IGrainRoutingStrategy
{
    private readonly Dictionary<string, string> _zoneToServerMapping = new();
    
    public void MapZoneToServer(string zone, string serverId)
    {
        _zoneToServerMapping[zone] = serverId;
    }
    
    public Task<string> SelectServerAsync(
        Type grainInterface,
        string grainKey,
        IReadOnlyDictionary<string, IServerDescriptor> servers,
        IRoutingContext context)
    {
        var zone = context?.GetProperty<string>("Zone");
        if (zone != null && _zoneToServerMapping.TryGetValue(zone, out var serverId))
        {
            return Task.FromResult(serverId);
        }
        
        // Fallback to primary
        var primary = servers.Values.FirstOrDefault(s => s.IsPrimary);
        return Task.FromResult(primary?.ServerId);
    }
}
```

## Testing

1. **Unit Tests**: Mock `IRpcClientMultiplexer` for testing
2. **Integration Tests**: Use multiple test servers with different zones
3. **Load Tests**: Verify connection pooling and routing performance

## Performance Considerations

1. **Connection Pooling**: Multiplexer maintains persistent connections
2. **Lazy Connection**: Use `EagerConnect = false` for on-demand connections
3. **Health Checks**: Adjust `HealthCheckInterval` based on requirements
4. **Routing Cache**: Routing decisions are fast (in-memory lookups)

## Troubleshooting

### Common Issues:

1. **"No healthy servers available"**
   - Check server registration
   - Verify network connectivity
   - Review health check logs

2. **"Could not find an implementation"**
   - Ensure server manifest is populated
   - Check grain interface registration
   - Verify server supports the grain type

3. **Zone transitions failing**
   - Confirm zone metadata in server descriptors
   - Verify routing strategy configuration
   - Check zone context in grain calls

### Debug Logging:

Enable debug logging to see routing decisions:
```csharp
services.AddLogging(logging =>
{
    logging.SetMinimumLevel(LogLevel.Debug);
    logging.AddFilter("Granville.Rpc.Multiplexing", LogLevel.Debug);
});
```

## Best Practices

1. **Server Registration**: Register all known servers upfront
2. **Routing Context**: Always provide context for zone-aware grains
3. **Health Monitoring**: Monitor server health and handle failures
4. **Connection Limits**: Don't register too many servers (10-20 is reasonable)
5. **Graceful Shutdown**: Dispose multiplexer properly to close connections