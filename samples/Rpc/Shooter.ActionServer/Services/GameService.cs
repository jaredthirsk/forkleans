using Shooter.ActionServer.RpcInterfaces;
using Shooter.ActionServer.Simulation;
using Shooter.Shared.Models;
using Shooter.Shared.GrainInterfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Shooter.ActionServer.Services;

public class GameService : IGameService, IHostedService
{
    private readonly IWorldSimulation _simulation;
    private readonly Orleans.IClusterClient _orleansClient;
    private readonly ILogger<GameService> _logger;
    private CancellationTokenSource? _zoneMonitoringCts;
    private Task? _zoneMonitoringTask;

    public GameService(IWorldSimulation simulation, Orleans.IClusterClient orleansClient, ILogger<GameService> logger)
    {
        _simulation = simulation;
        _orleansClient = orleansClient;
        _logger = logger;
    }

    public Task<bool> ConnectPlayer(string playerId)
    {
        return _simulation.AddPlayer(playerId);
    }

    public Task DisconnectPlayer(string playerId)
    {
        _simulation.RemovePlayer(playerId);
        return Task.CompletedTask;
    }

    public Task<WorldState> GetWorldState()
    {
        return Task.FromResult(_simulation.GetCurrentState());
    }

    public Task UpdatePlayerInput(string playerId, Vector2 moveDirection, bool isShooting)
    {
        _simulation.UpdatePlayerInput(playerId, moveDirection, isShooting);
        return Task.CompletedTask;
    }
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _zoneMonitoringCts = new CancellationTokenSource();
        _zoneMonitoringTask = MonitorZoneTransitions(_zoneMonitoringCts.Token);
        return Task.CompletedTask;
    }
    
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _zoneMonitoringCts?.Cancel();
        if (_zoneMonitoringTask != null)
        {
            await _zoneMonitoringTask;
        }
    }
    
    private async Task MonitorZoneTransitions(CancellationToken cancellationToken)
    {
        var worldManager = _orleansClient.GetGrain<IWorldManagerGrain>(0);
        
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Handle player transfers
                var playersOutside = await _simulation.GetPlayersOutsideZone();
                foreach (var playerId in playersOutside)
                {
                    var playerInfo = _simulation.GetPlayerInfo(playerId);
                    if (playerInfo != null)
                    {
                        _logger.LogInformation("Player {PlayerId} at position {Position} is outside zone, initiating transfer", 
                            playerId, playerInfo.Position);
                        
                        // Update player position in Orleans FIRST before initiating transfer
                        await worldManager.UpdatePlayerPosition(playerId, playerInfo.Position);
                        
                        // Small delay to ensure position is persisted
                        await Task.Delay(50);
                        
                        // Initiate transfer with WorldManager
                        var transferInfo = await worldManager.InitiatePlayerTransfer(playerId, playerInfo.Position);
                        if (transferInfo?.NewServer != null)
                        {
                            _logger.LogInformation("Transferring player {PlayerId} from position {Position} to server {ServerId} for zone {Zone}", 
                                playerId, playerInfo.Position, transferInfo.NewServer.ServerId, transferInfo.NewServer.AssignedSquare);
                            
                            // Remove player from current simulation
                            _simulation.RemovePlayer(playerId);
                            
                            // The client will detect the disconnection and query for the new server
                        }
                        else
                        {
                            _logger.LogWarning("No transfer info received for player {PlayerId} at position {Position}", 
                                playerId, playerInfo.Position);
                        }
                    }
                }
                
                // Handle entity transfers (enemies and bullets)
                var entitiesOutside = await _simulation.GetEntitiesOutsideZone();
                foreach (var (entityId, position, type, subType) in entitiesOutside)
                {
                    var targetZone = GridSquare.FromPosition(position);
                    var targetServer = await worldManager.GetActionServerForPosition(position);
                    
                    if (targetServer != null)
                    {
                        // Get the entity's current state for transfer
                        var worldState = _simulation.GetCurrentState();
                        var entity = worldState.Entities.FirstOrDefault(e => e.EntityId == entityId);
                        
                        if (entity != null)
                        {
                            _logger.LogDebug("Transferring {Type} {EntityId} to zone {Zone}", type, entityId, targetZone);
                            
                            // Try to transfer entity to target server
                            var transferred = await TransferEntityToServer(targetServer, entity);
                            if (transferred)
                            {
                                // Remove from current simulation
                                _simulation.RemovePlayer(entityId); // This method works for any entity
                            }
                        }
                    }
                    else
                    {
                        // No server for target zone, remove entity
                        _logger.LogDebug("Removing {Type} {EntityId} - no server for zone {Zone}", type, entityId, targetZone);
                        _simulation.RemovePlayer(entityId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error monitoring zone transitions");
            }
            
            await Task.Delay(250, cancellationToken); // Check 4 times per second
        }
    }
    
    private async Task<bool> TransferEntityToServer(ActionServerInfo targetServer, EntityState entity)
    {
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(targetServer.HttpEndpoint) };
            
            var response = await client.PostAsJsonAsync($"game/transfer-entity", new
            {
                entityId = entity.EntityId,
                type = entity.Type,
                subType = entity.SubType,
                position = entity.Position,
                velocity = entity.Velocity,
                health = entity.Health
            });
            
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to transfer entity {EntityId} to server {ServerId}", 
                entity.EntityId, targetServer.ServerId);
            return false;
        }
    }
}