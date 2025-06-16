using Shooter.ActionServer.RpcInterfaces;
using Shooter.ActionServer.Simulation;
using Shooter.Shared.Models;
using Shooter.Shared.GrainInterfaces;
using Shooter.Shared.RpcInterfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Forkleans;
using Forkleans.Rpc;
using Forkleans.Rpc.Hosting;
using Forkleans.Rpc.Transport.LiteNetLib;
using Forkleans.Serialization;

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
    
    public Task UpdatePlayerInputEx(string playerId, Vector2? moveDirection, Vector2? shootDirection)
    {
        _simulation.UpdatePlayerInputEx(playerId, moveDirection, shootDirection);
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
        
        _logger.LogInformation("Zone monitoring started for zone {Zone}", _simulation.GetAssignedSquare());
        
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogDebug("Zone monitor checking for transfers...");
                
                // Handle player transfers
                var playersOutside = await _simulation.GetPlayersOutsideZone();
                
                if (playersOutside.Count > 0)
                {
                    _logger.LogInformation("Zone monitor found {Count} players outside zone", playersOutside.Count);
                }
                
                foreach (var playerId in playersOutside)
                {
                    var playerInfo = _simulation.GetPlayerInfo(playerId);
                    if (playerInfo != null)
                    {
                        _logger.LogInformation("Player {PlayerId} at position {Position} is outside zone, initiating transfer", 
                            playerId, playerInfo.Position);
                        
                        // Check the player's zone again to ensure they're still outside
                        var currentZone = GridSquare.FromPosition(playerInfo.Position);
                        var assignedZone = _simulation.GetAssignedSquare();
                        
                        if (currentZone == assignedZone)
                        {
                            _logger.LogWarning("Player {PlayerId} is back in assigned zone {Zone}, skipping transfer", 
                                playerId, assignedZone);
                            continue;
                        }
                        
                        // Initiate transfer with WorldManager - it will update the position internally
                        var transferInfo = await worldManager.InitiatePlayerTransfer(playerId, playerInfo.Position);
                        if (transferInfo?.NewServer != null)
                        {
                            _logger.LogInformation("Transferring player {PlayerId} from position {Position} to server {ServerId} for zone {Zone}", 
                                playerId, playerInfo.Position, transferInfo.NewServer.ServerId, transferInfo.NewServer.AssignedSquare);
                            
                            // Try to transfer player to the new server
                            var worldState = _simulation.GetCurrentState();
                            var playerEntity = worldState.Entities.FirstOrDefault(e => e.EntityId == playerId);
                            
                            if (playerEntity != null)
                            {
                                var transferred = await TransferEntityToServer(transferInfo.NewServer, playerEntity);
                                if (transferred)
                                {
                                    _logger.LogInformation("Successfully transferred player {PlayerId} to new server", playerId);
                                    // Remove player from current simulation
                                    _simulation.RemovePlayer(playerId);
                                }
                                else
                                {
                                    _logger.LogError("Failed to transfer player {PlayerId} to new server", playerId);
                                }
                            }
                            else
                            {
                                _logger.LogError("Could not find player entity {PlayerId} for transfer", playerId);
                            }
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
            // Use RPC for entity transfers
            if (targetServer.RpcPort <= 0)
            {
                _logger.LogError("Target server {ServerId} does not have RPC port configured", targetServer.ServerId);
                return false;
            }

            _logger.LogInformation("Attempting RPC transfer of {Type} {EntityId} to server {ServerId} at {Host}:{Port}", 
                entity.Type, entity.EntityId, targetServer.ServerId, targetServer.IpAddress, targetServer.RpcPort);
            
            // Create RPC client to target server
            var hostBuilder = Host.CreateDefaultBuilder()
                .UseOrleansRpcClient(rpcBuilder =>
                {
                    // Resolve hostname if needed
                    var host = targetServer.IpAddress;
                    if (!System.Net.IPAddress.TryParse(host, out _) && host != "localhost")
                    {
                        try
                        {
                            var hostEntry = System.Net.Dns.GetHostEntry(host);
                            var ipAddress = hostEntry.AddressList.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                            if (ipAddress != null)
                            {
                                host = ipAddress.ToString();
                            }
                        }
                        catch
                        {
                            host = "127.0.0.1";
                        }
                    }
                    
                    rpcBuilder.ConnectTo(host == "localhost" ? "127.0.0.1" : host, targetServer.RpcPort);
                    rpcBuilder.UseLiteNetLib();
                })
                .ConfigureServices(services =>
                {
                    services.AddSerializer(serializer =>
                    {
                        serializer.AddAssembly(typeof(IGameRpcGrain).Assembly);
                    });
                })
                .Build();
                
            await hostBuilder.StartAsync();
            
            try
            {
                var rpcClient = hostBuilder.Services.GetRequiredService<Forkleans.IClusterClient>();
                
                // Wait for connection
                await Task.Delay(500);
                
                var gameGrain = rpcClient.GetGrain<IGameRpcGrain>("game");
                var transferred = await gameGrain.TransferEntityIn(
                    entity.EntityId, 
                    entity.Type, 
                    entity.SubType, 
                    entity.Position, 
                    entity.Velocity, 
                    entity.Health);
                    
                _logger.LogInformation("RPC transfer result for {EntityId}: {Result}", entity.EntityId, transferred);
                return transferred;
            }
            finally
            {
                await hostBuilder.StopAsync();
                hostBuilder.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to transfer entity {EntityId} to server {ServerId}", 
                entity.EntityId, targetServer.ServerId);
            return false;
        }
    }
}