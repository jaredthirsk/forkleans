using Forkleans;
using Shooter.ActionServer.Services;
using Shooter.ActionServer.Simulation;
using Shooter.Shared.Models;
using Shooter.Shared.RpcInterfaces;
using Shooter.Shared.GrainInterfaces;

namespace Shooter.ActionServer.Grains;

/// <summary>
/// Grain implementation that exposes game functionality via Forkleans RPC.
/// This grain runs in the ActionServer process and has direct access to game services.
/// Note: This uses Forkleans.Grain, not Orleans.Grain, for RPC compatibility.
/// </summary>
public class GameRpcGrain : Forkleans.Grain, IGameRpcGrain
{
    private readonly GameService _gameService;
    private readonly IWorldSimulation _worldSimulation;
    private readonly ILogger<GameRpcGrain> _logger;
    private readonly Forkleans.IClusterClient _orleansClient;

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
        _logger.LogInformation("RPC: Receiving bullet trajectory {BulletId} with origin {Origin}, velocity {Velocity}, lifespan {Lifespan}", 
            bulletId, origin, velocity, lifespan);
        
        // Transfer the bullet trajectory to the world simulation
        _worldSimulation.ReceiveBulletTrajectory(bulletId, subType, origin, velocity, spawnTime, lifespan, ownerId);
        
        return Task.CompletedTask;
    }
}