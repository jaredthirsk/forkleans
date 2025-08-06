# Zone Detection Strategy Guide

The zone detection strategy allows RPC clients to route grain calls to specific RPC servers based on zone assignments. This is useful for:

- **Spatial partitioning**: Route game entities to servers based on their world position
- **Load balancing**: Distribute grains across servers based on type or other criteria
- **Regional affinity**: Keep grains close to their data or users

## Basic Usage

### 1. Built-in Strategies

#### Shooter Zone Detection Strategy
```csharp
// In your client configuration
builder.UseZoneDetectionStrategy<ShooterZoneDetectionStrategy>();
```

This strategy:
- Maps a 1000x1000 game world into 100 zones (10x10 grid)
- Each zone covers a 100x100 area
- Zone IDs start at 1000 (zones 1000-1099)

#### Grain Type Based Strategy
```csharp
// Configure specific grain types to route to specific zones
builder.UseZoneDetectionStrategy<GrainTypeBasedZoneDetectionStrategy>(strategy =>
{
    strategy.AddMapping("IPlayerGrain", 1001);      // All player grains go to zone 1001
    strategy.AddMapping("IEnemyGrain", 1002);       // All enemy grains go to zone 1002
    strategy.AddMapping("IProjectileGrain", 1003);  // All projectiles go to zone 1003
});
```

### 2. Custom Strategy Implementation

Create your own strategy by implementing `IZoneDetectionStrategy`:

```csharp
public class MyCustomZoneDetectionStrategy : IZoneDetectionStrategy
{
    private readonly ILogger<MyCustomZoneDetectionStrategy> _logger;
    
    public MyCustomZoneDetectionStrategy(ILogger<MyCustomZoneDetectionStrategy> logger)
    {
        _logger = logger;
    }
    
    public int? GetZoneId(GrainId grainId)
    {
        // Extract grain type and key
        var grainType = grainId.Type;
        var grainKey = grainId.Key.ToString();
        
        // Your custom logic here
        if (grainType.ToString().Contains("IPlayerGrain"))
        {
            // Extract player ID from key and calculate zone
            var playerId = ExtractPlayerId(grainKey);
            return CalculateZoneForPlayer(playerId);
        }
        
        // Return null for no specific zone routing
        return null;
    }
    
    public int? GetZoneId(GrainType grainType, string grainKey)
    {
        // Alternative method for when you have the type and key separately
        return GetZoneId(GrainId.Create(grainType, grainKey));
    }
}
```

### 3. Server-Side Zone Configuration

Servers must declare which zones they handle:

```csharp
// In your server configuration
services.Configure<RpcServerOptions>(options =>
{
    options.ServerId = "game-server-1";
    options.ZoneId = 1001;  // This server handles zone 1001
});
```

### 4. Multi-Zone Servers

A server can handle multiple zones:

```csharp
// During server startup, after connecting to clients
rpcServer.UpdateZoneMappings(new Dictionary<int, string>
{
    { 1001, "game-server-1" },
    { 1002, "game-server-1" },
    { 1003, "game-server-1" }
});
```

## Advanced Scenarios

### Dynamic Zone Assignment

For games where entities move between zones:

```csharp
public class DynamicZoneDetectionStrategy : IZoneDetectionStrategy
{
    private readonly IPositionTracker _positionTracker;
    
    public int? GetZoneId(GrainId grainId)
    {
        // Get current position from a position tracking service
        var position = _positionTracker.GetPosition(grainId);
        if (position != null)
        {
            return ShooterZoneDetectionStrategy.CalculateZoneFromPosition(
                position.X, position.Y);
        }
        
        return null;
    }
}
```

### Hybrid Routing

Combine multiple strategies:

```csharp
public class HybridZoneDetectionStrategy : IZoneDetectionStrategy
{
    private readonly GrainTypeBasedZoneDetectionStrategy _typeStrategy;
    private readonly ShooterZoneDetectionStrategy _spatialStrategy;
    
    public int? GetZoneId(GrainId grainId)
    {
        // First try type-based routing
        var zoneId = _typeStrategy.GetZoneId(grainId);
        if (zoneId.HasValue)
            return zoneId;
            
        // Fall back to spatial routing
        return _spatialStrategy.GetZoneId(grainId);
    }
}
```

## Best Practices

1. **Zone Stability**: Avoid frequent zone changes for grains to minimize cross-server communication
2. **Zone Granularity**: Balance between too many zones (overhead) and too few (poor distribution)
3. **Fallback Strategy**: Always have a default server for grains without specific zone assignments
4. **Monitor Zone Distribution**: Track which zones are handling the most traffic for load balancing

## Troubleshooting

If grains are not routing to the expected server:

1. Check that the zone detection strategy is registered in the client
2. Verify the server has declared it handles the target zone
3. Enable debug logging to see zone detection decisions:
   ```csharp
   logging.AddFilter("Granville.Rpc.Zones", LogLevel.Debug);
   logging.AddFilter("Granville.Rpc.RpcConnectionManager", LogLevel.Debug);
   ```