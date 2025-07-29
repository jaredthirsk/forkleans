# RPC Client Multiplexer - Shooter Game Scenario

## Current Problem Analysis

### Zone Transition Flow
1. Player spawns in zone (1,0) → connects to ActionServer at port 12003
2. Player moves to zone (0,1) → needs to connect to ActionServer at port 12001
3. Current implementation:
   - Disposes existing RpcClient
   - Creates new RpcClient for new zone
   - Loses connection to previous zone
   - Cannot access global grains consistently

### Issues Identified
```
2025-07-29 10:43:10.727 [Information] Inside Task.Run, about to call GetGrain. RpcClient is NULL
2025-07-29 10:44:39.297 [Error] Exception: System.InvalidOperationException: RPC client is not connected to any servers
```

The RpcClient becomes null or disconnected during zone transitions, causing GetGrain calls to fail.

## Solution with Multiplexer

### Server Architecture
```
┌─────────────────────┐     ┌─────────────────────┐     ┌─────────────────────┐
│   ActionServer #1   │     │   ActionServer #2   │     │   ActionServer #3   │
│   Zone: (0,0)       │     │   Zone: (0,1)       │     │   Zone: (1,0)       │
│   Port: 12000       │     │   Port: 12001       │     │   Port: 12003       │
│                     │     │                     │     │                     │
│ - IPlayerGrain      │     │ - IPlayerGrain      │     │ - IPlayerGrain      │
│ - IProjectileGrain  │     │ - IProjectileGrain  │     │ - IProjectileGrain  │
│ - IGameRpcGrain*    │     │ - IEnemyGrain       │     │ - IEnemyGrain       │
└─────────────────────┘     └─────────────────────┘     └─────────────────────┘
        ↑                            ↑                            ↑
        │                            │                            │
        └────────────────────────────┴────────────────────────────┘
                                     │
                            RpcClientMultiplexer
                                     │
                                Game Client
```
*IGameRpcGrain is hosted on primary server (zone 0,0)

### Implementation Changes

#### 1. Update GranvilleRpcGameClientService
```csharp
public class GranvilleRpcGameClientService : IGameClientService
{
    private readonly IRpcClientMultiplexer _multiplexer;
    private readonly ILogger<GranvilleRpcGameClientService> _logger;
    private string _currentZone;
    private readonly Dictionary<string, ServerDescriptor> _knownServers;

    public GranvilleRpcGameClientService(
        IRpcClientMultiplexer multiplexer,
        ILogger<GranvilleRpcGameClientService> logger)
    {
        _multiplexer = multiplexer;
        _logger = logger;
        _knownServers = new Dictionary<string, ServerDescriptor>();
    }

    public async Task<bool> ConnectAsync(string playerName)
    {
        try
        {
            // Register player and get zone assignment
            var registration = await RegisterPlayerAsync(playerName);
            _currentZone = registration.Zone;
            
            // Discover and register all action servers
            await DiscoverAndRegisterServersAsync();
            
            // Set initial routing context
            _multiplexer.SetRoutingContext(new RoutingContext
            {
                ["Zone"] = _currentZone,
                ["PlayerId"] = registration.PlayerId
            });
            
            // Get game grain - will route to primary server
            var gameGrain = _multiplexer.GetGrain<IGameRpcGrain>("game");
            
            // Connect player
            await gameGrain.ConnectPlayer(registration.PlayerId);
            
            _logger.LogInformation("Successfully connected player {PlayerId} in zone {Zone}", 
                registration.PlayerId, _currentZone);
                
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect player {PlayerName}", playerName);
            return false;
        }
    }

    private async Task DiscoverAndRegisterServersAsync()
    {
        // Discover all action servers from Silo
        var servers = await DiscoverServersAsync();
        
        foreach (var server in servers)
        {
            var descriptor = new ServerDescriptor
            {
                ServerId = server.ServerId,
                HostName = server.Hostname,
                Port = server.RpcPort,
                Metadata = new Dictionary<string, string>
                {
                    ["zone"] = server.Zone,
                    ["type"] = "action-server"
                },
                IsPrimary = server.Zone == "0,0" // Primary hosts global grains
            };
            
            _knownServers[server.ServerId] = descriptor;
            await _multiplexer.RegisterServerAsync(descriptor);
        }
    }

    public async Task OnPlayerPositionChanged(Vector2 newPosition)
    {
        var newZone = GetZoneForPosition(newPosition);
        
        if (newZone != _currentZone)
        {
            _logger.LogInformation("Player transitioning from zone {OldZone} to {NewZone}", 
                _currentZone, newZone);
            
            _currentZone = newZone;
            
            // Just update routing context - multiplexer handles the rest
            _multiplexer.SetRoutingContext(new RoutingContext
            {
                ["Zone"] = _currentZone,
                ["PlayerId"] = _playerId
            });
            
            // No need to recreate connections!
        }
    }

    public async Task UpdatePlayerStateAsync(PlayerState state)
    {
        // Get player grain - automatically routed to current zone's server
        var playerGrain = _multiplexer.GetGrain<IPlayerGrain>(_playerId);
        await playerGrain.UpdateState(state);
    }

    public async Task<WorldState> GetWorldStateAsync()
    {
        // Get game grain - always routed to primary server
        var gameGrain = _multiplexer.GetGrain<IGameRpcGrain>("game");
        return await gameGrain.GetWorldState();
    }
}
```

#### 2. Configure Routing Strategy
```csharp
public class ShooterGameRoutingStrategy : IGrainRoutingStrategy
{
    private readonly ILogger<ShooterGameRoutingStrategy> _logger;

    public Task<string> SelectServerAsync(
        Type grainInterface,
        string grainKey,
        IReadOnlyDictionary<string, IServerDescriptor> servers,
        IRoutingContext context)
    {
        // Global grains always go to primary
        if (IsGlobalGrain(grainInterface))
        {
            var primary = servers.Values.FirstOrDefault(s => s.IsPrimary);
            if (primary != null)
            {
                _logger.LogDebug("Routing global grain {Interface} to primary server {ServerId}",
                    grainInterface.Name, primary.ServerId);
                return Task.FromResult(primary.ServerId);
            }
        }
        
        // Zone-aware grains go to zone server
        if (typeof(IZoneAwareGrain).IsAssignableFrom(grainInterface))
        {
            var zone = context.GetProperty<string>("Zone");
            if (!string.IsNullOrEmpty(zone))
            {
                var zoneServer = servers.Values.FirstOrDefault(s =>
                    s.Metadata.TryGetValue("zone", out var serverZone) && 
                    serverZone == zone);
                    
                if (zoneServer != null)
                {
                    _logger.LogDebug("Routing zone-aware grain {Interface} to zone {Zone} server {ServerId}",
                        grainInterface.Name, zone, zoneServer.ServerId);
                    return Task.FromResult(zoneServer.ServerId);
                }
            }
        }
        
        // Default to primary
        var fallback = servers.Values.FirstOrDefault(s => s.IsPrimary);
        return Task.FromResult(fallback?.ServerId ?? servers.Keys.FirstOrDefault());
    }

    private bool IsGlobalGrain(Type grainInterface)
    {
        // Global grains that should always go to primary
        return grainInterface == typeof(IGameRpcGrain) ||
               grainInterface == typeof(ILeaderboardGrain) ||
               grainInterface == typeof(IMatchmakingGrain);
    }
}
```

#### 3. Startup Configuration
```csharp
// In Program.cs or Startup.cs
builder.Services.AddRpcClient()
    .AddRpcClientMultiplexer(options =>
    {
        options.EnableHealthChecks = true;
        options.HealthCheckInterval = TimeSpan.FromSeconds(10);
        options.EagerConnect = false; // Connect on demand
    })
    .AddSingleton<IGrainRoutingStrategy, ShooterGameRoutingStrategy>();

// Replace direct RpcClient usage
builder.Services.Replace(ServiceDescriptor.Singleton<IGameClientService, GranvilleRpcGameClientService>());
```

## Benefits for Shooter Game

### 1. Seamless Zone Transitions
- No connection drops when moving between zones
- Instant zone switches without reconnection overhead
- Maintains connections to all relevant servers

### 2. Consistent Global State Access
- IGameRpcGrain always accessible regardless of player zone
- No "RpcClient is null" errors during transitions
- Reliable access to leaderboards, chat, etc.

### 3. Performance Improvements
- Connection reuse reduces latency
- No connection establishment during gameplay
- Parallel requests to multiple servers possible

### 4. Simplified Error Handling
- Automatic reconnection with backoff
- Health monitoring for server availability
- Graceful degradation when servers go offline

## Migration Steps

1. **Phase 1**: Implement core multiplexer
   - Create multiplexer classes
   - Add routing strategies
   - Test with manual server registration

2. **Phase 2**: Update GranvilleRpcGameClientService
   - Replace RpcClient with IRpcClientMultiplexer
   - Remove connection management code
   - Update zone transition logic

3. **Phase 3**: Enhanced features
   - Add server discovery from Silo
   - Implement health monitoring
   - Add metrics and diagnostics

## Testing Scenarios

### Scenario 1: Basic Zone Transition
```csharp
[Fact]
public async Task PlayerCanTransitionBetweenZones()
{
    // Arrange
    var multiplexer = CreateMultiplexer();
    await RegisterAllServers(multiplexer);
    
    // Act - Start in zone 0,0
    multiplexer.SetRoutingContext(new { Zone = "0,0" });
    var player1 = multiplexer.GetGrain<IPlayerGrain>(playerId);
    await player1.UpdatePosition(new Vector2(50, 50));
    
    // Transition to zone 1,0
    multiplexer.SetRoutingContext(new { Zone = "1,0" });
    var player2 = multiplexer.GetGrain<IPlayerGrain>(playerId);
    await player2.UpdatePosition(new Vector2(150, 50));
    
    // Assert - both operations succeed without connection errors
    Assert.True(await player1.IsConnected());
    Assert.True(await player2.IsConnected());
}
```

### Scenario 2: Global Grain Access
```csharp
[Fact]
public async Task GlobalGrainAccessibleFromAnyZone()
{
    // Arrange
    var multiplexer = CreateMultiplexer();
    
    // Act - Access game grain from different zones
    multiplexer.SetRoutingContext(new { Zone = "0,0" });
    var game1 = multiplexer.GetGrain<IGameRpcGrain>("game");
    
    multiplexer.SetRoutingContext(new { Zone = "1,1" });
    var game2 = multiplexer.GetGrain<IGameRpcGrain>("game");
    
    // Assert - same grain reference
    Assert.Equal(game1.GetPrimaryKey(), game2.GetPrimaryKey());
}
```

### Scenario 3: Server Failure Handling
```csharp
[Fact]
public async Task HandlesServerFailureGracefully()
{
    // Arrange
    var multiplexer = CreateMultiplexer();
    await RegisterAllServers(multiplexer);
    
    // Act - Simulate server going offline
    await SimulateServerOffline("zone-1-0");
    
    // Try to access grain in failed zone
    multiplexer.SetRoutingContext(new { Zone = "1,0" });
    
    // Assert - Should throw clear error or fallback
    await Assert.ThrowsAsync<NoAvailableServerException>(
        () => multiplexer.GetGrain<IPlayerGrain>(playerId).UpdatePosition(Vector2.Zero));
}
```