using Forkleans;
using Forkleans.Utilities;
using Shooter.ActionServer.Services;
using Shooter.ActionServer.Simulation;
using Shooter.Shared.Models;
using Shooter.Shared.RpcInterfaces;
using Shooter.Shared.GrainInterfaces;
using System.Runtime.CompilerServices;

namespace Shooter.ActionServer.Grains;

/// <summary>
/// Grain implementation that exposes game functionality via Forkleans RPC.
/// This grain runs in the ActionServer process and has direct access to game services.
/// Note: This uses Forkleans.Grain, not Forkleans.Grain, for RPC compatibility.
/// </summary>
public class GameRpcGrain : Forkleans.Grain, IGameRpcGrain
{
    private readonly GameService _gameService;
    private readonly IWorldSimulation _worldSimulation;
    private readonly ILogger<GameRpcGrain> _logger;
    private readonly Forkleans.IClusterClient _orleansClient;
    private readonly ObserverManager<IGameRpcObserver> _observers;

    public GameRpcGrain(
        GameService gameService,
        IWorldSimulation worldSimulation,
        ILogger<GameRpcGrain> logger,
        Forkleans.IClusterClient orleansClient)
    {
        _gameService = gameService;
        _worldSimulation = worldSimulation;
        _logger = logger;
        _orleansClient = orleansClient;
        _observers = new ObserverManager<IGameRpcObserver>(TimeSpan.FromMinutes(5), logger);
    }

    public async Task<string> ConnectPlayer(string playerId)
    {
        _logger.LogInformation("RPC: Player {PlayerId} connecting via Forkleans RPC", playerId);
        var result = await _gameService.ConnectPlayer(playerId);
        _logger.LogInformation("RPC: ConnectPlayer returning {Result} for player {PlayerId}", result, playerId);
        return result ? "SUCCESS" : "FAILED";
    }

    public async Task DisconnectPlayer(string playerId)
    {
        _logger.LogInformation("RPC: Player {PlayerId} disconnecting via Forkleans RPC", playerId);
        await _gameService.DisconnectPlayer(playerId);
    }

    public async Task<WorldState> GetWorldState()
    {
        return await Task.FromResult(_worldSimulation.GetCurrentState());
    }

    public async Task UpdatePlayerInput(string playerId, Vector2 moveDirection, bool isShooting)
    {
        await _gameService.UpdatePlayerInput(playerId, moveDirection, isShooting);
    }
    
    public async Task UpdatePlayerInputEx(string playerId, Vector2? moveDirection, Vector2? shootDirection)
    {
        await _gameService.UpdatePlayerInputEx(playerId, moveDirection, shootDirection);
    }

    public async Task<bool> TransferEntityIn(string entityId, EntityType type, int subType, Vector2 position, Vector2 velocity, float health)
    {
        _logger.LogInformation("RPC: Transferring entity {EntityId} via Forkleans RPC", entityId);
        return await _worldSimulation.TransferEntityIn(entityId, type, subType, position, velocity, health);
    }

    public async Task<List<GridSquare>> GetAvailableZones()
    {
        try
        {
            var worldManager = _orleansClient.GetGrain<IWorldManagerGrain>(0);
            var servers = await worldManager.GetAllActionServers();
            var zones = servers.Select(s => s.AssignedSquare).ToList();
            _logger.LogInformation("RPC: Returning {Count} available zones", zones.Count);
            return zones;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get available zones");
            return new List<GridSquare>();
        }
    }

    public Task ReceiveScoutAlert(GridSquare playerZone, Vector2 playerPosition)
    {
        _logger.LogInformation("RPC: Received scout alert - Player spotted in zone ({X},{Y}) at position {Position}", 
            playerZone.X, playerZone.Y, playerPosition);
        
        // Alert local enemies to move toward the player zone
        _worldSimulation.ProcessScoutAlert(playerZone, playerPosition);
        
        return Task.CompletedTask;
    }
    
    public Task<WorldState> GetLocalWorldState()
    {
        // Return only local entities without fetching from adjacent zones
        var state = _worldSimulation.GetCurrentState();
        return Task.FromResult(state);
    }
    
    public Task<ZoneStats> GetZoneStats()
    {
        var state = _worldSimulation.GetCurrentState();
        
        var factoryCount = state.Entities.Count(e => e.Type == EntityType.Factory && e.State != EntityStateType.Dead);
        var enemyCount = state.Entities.Count(e => e.Type == EntityType.Enemy && e.State != EntityStateType.Dead);
        var playerCount = state.Entities.Count(e => e.Type == EntityType.Player && e.SubType == 0 && e.State != EntityStateType.Dead);
        
        return Task.FromResult(new ZoneStats(factoryCount, enemyCount, playerCount));
    }
    
    public Task TransferBulletTrajectory(string bulletId, int subType, Vector2 origin, Vector2 velocity, float spawnTime, float lifespan, string? ownerId)
    {
        var currentZone = _worldSimulation.GetAssignedSquare();
        _logger.LogInformation("RPC: Zone ({ZoneX},{ZoneY}) receiving bullet trajectory {BulletId} with origin {Origin}, velocity {Velocity}, lifespan {Lifespan}", 
            currentZone.X, currentZone.Y, bulletId, origin, velocity, lifespan);
        
        // Transfer the bullet trajectory to the world simulation
        _worldSimulation.ReceiveBulletTrajectory(bulletId, subType, origin, velocity, spawnTime, lifespan, ownerId);
        
        return Task.CompletedTask;
    }
    
    public Task Subscribe(IGameRpcObserver observer)
    {
        _observers.Subscribe(observer, observer);
        _logger.LogInformation("RPC: Observer subscribed to game updates");
        return Task.CompletedTask;
    }
    
    public Task Unsubscribe(IGameRpcObserver observer)
    {
        _observers.Unsubscribe(observer);
        _logger.LogInformation("RPC: Observer unsubscribed from game updates");
        return Task.CompletedTask;
    }
    
    // Methods to notify observers
    public void NotifyZoneStatsUpdated(ZoneStatistics stats)
    {
        _observers.Notify(observer => observer.OnZoneStatsUpdated(stats));
    }
    
    public void NotifyAvailableZonesChanged(List<GridSquare> availableZones)
    {
        _observers.Notify(observer => observer.OnAvailableZonesChanged(availableZones));
    }
    
    public void NotifyAdjacentEntitiesUpdated(Dictionary<string, List<EntityState>> entitiesByZone)
    {
        _observers.Notify(observer => observer.OnAdjacentEntitiesUpdated(entitiesByZone));
    }
    
    public void NotifyScoutAlert(ScoutAlert alert)
    {
        _observers.Notify(observer => observer.OnScoutAlert(alert));
    }
    
    // IAsyncEnumerable implementations
    public async IAsyncEnumerable<WorldState> StreamWorldStateUpdates([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _logger.LogInformation("RPC: Starting world state stream");
        
        while (!cancellationToken.IsCancellationRequested)
        {
            var state = _worldSimulation.GetCurrentState();
            yield return state;
            
            // Stream at 60 FPS
            await Task.Delay(16, cancellationToken);
        }
    }
    
    public async IAsyncEnumerable<ZoneStatistics> StreamZoneStatistics([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _logger.LogInformation("RPC: Starting zone statistics stream");
        
        while (!cancellationToken.IsCancellationRequested)
        {
            var state = _worldSimulation.GetCurrentState();
            var stats = new ZoneStatistics
            {
                Zone = _worldSimulation.GetAssignedSquare(),
                PlayerCount = state.Entities.Count(e => e.Type == EntityType.Player && e.SubType == 0 && e.State != EntityStateType.Dead),
                EntityCount = state.Entities.Count(e => e.State != EntityStateType.Dead),
                BulletCount = state.Entities.Count(e => e.Type == EntityType.Bullet),
                AverageUpdateTime = 0, // TODO: Track actual update time
                LastUpdate = DateTime.UtcNow
            };
            
            yield return stats;
            
            // Update every second
            await Task.Delay(1000, cancellationToken);
        }
    }
    
    public async IAsyncEnumerable<AdjacentZoneEntities> StreamAdjacentZoneEntities(string playerId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _logger.LogInformation("RPC: Starting adjacent zone entities stream for player {PlayerId}", playerId);
        
        while (!cancellationToken.IsCancellationRequested)
        {
            // Get player's position
            var state = _worldSimulation.GetCurrentState();
            var player = state.Entities.FirstOrDefault(e => e.EntityId == playerId);
            
            if (player != null)
            {
                var playerPosition = player.Position;
                var playerZone = GridSquare.FromPosition(playerPosition);
                
                // Get adjacent zones
                var adjacentZones = new List<GridSquare>();
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue; // Skip current zone
                        adjacentZones.Add(new GridSquare(playerZone.X + dx, playerZone.Y + dy));
                    }
                }
                
                // Get entities from adjacent zones
                var entitiesByZone = new Dictionary<string, List<EntityState>>();
                var worldManager = _orleansClient.GetGrain<IWorldManagerGrain>(0);
                
                foreach (var zone in adjacentZones)
                {
                    try
                    {
                        var serverInfo = await worldManager.GetActionServerForPosition(zone.GetCenter());
                        if (serverInfo != null)
                        {
                            // TODO: Get entities from the adjacent zone's ActionServer
                            // For now, just return empty lists
                            entitiesByZone[$"{zone.X},{zone.Y}"] = new List<EntityState>();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to get entities for adjacent zone ({X},{Y})", zone.X, zone.Y);
                    }
                }
                
                yield return new AdjacentZoneEntities
                {
                    EntitiesByZone = entitiesByZone,
                    Timestamp = DateTime.UtcNow
                };
            }
            
            // Update every 100ms
            await Task.Delay(100, cancellationToken);
        }
    }
    
    /// <summary>
    /// Called by WorldSimulation to notify all connected clients about game over.
    /// </summary>
    public void NotifyObserversGameOver(GameOverMessage gameOverMessage)
    {
        _logger.LogInformation("Notifying {ObserverCount} observers about game over", _observers.Count);
        _observers.Notify(observer => observer.OnGameOver(gameOverMessage));
    }
    
    /// <summary>
    /// Called by WorldSimulation to notify all connected clients about game restart.
    /// </summary>
    public void NotifyObserversGameRestarted()
    {
        _logger.LogInformation("Notifying {ObserverCount} observers about game restart", _observers.Count);
        _observers.Notify(observer => observer.OnGameRestarted());
    }
}