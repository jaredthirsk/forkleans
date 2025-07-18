using Shooter.ActionServer.RpcInterfaces;
using Shooter.ActionServer.Simulation;
using Shooter.ActionServer.Telemetry;
using Shooter.Shared.Models;
using Shooter.Shared.GrainInterfaces;
using Shooter.Shared.RpcInterfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Granville.Rpc;
using Granville.Rpc.Hosting;
using Granville.Rpc.Transport.LiteNetLib;
using Orleans.Serialization;
using System.Diagnostics;

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
        
        // Set up callback for when players are removed due to timeout
        if (_simulation is WorldSimulation worldSim)
        {
            worldSim.SetPlayerTimeoutCallback(playerId =>
            {
                _logger.LogInformation("Player {PlayerId} timed out, updating telemetry", playerId);
                ActionServerTelemetry.PlayerDisconnections.Add(1, new KeyValuePair<string, object?>("player.id", playerId), new KeyValuePair<string, object?>("reason", "timeout"));
                ActionServerTelemetry.ActivePlayers.Add(-1);
            });
        }
    }

    public async Task<bool> ConnectPlayer(string playerId)
    {
        // Validate input
        if (string.IsNullOrEmpty(playerId))
        {
            _logger.LogError("ConnectPlayer called with null or empty playerId");
            return false;
        }
        
        using var activity = ActionServerTelemetry.ActivitySource.StartActivity(ActionServerTelemetry.PlayerConnectionActivity);
        activity?.SetTag("player.id", playerId);
        activity?.SetTag("zone.x", _simulation.GetAssignedSquare().X);
        activity?.SetTag("zone.y", _simulation.GetAssignedSquare().Y);

        var result = await _simulation.AddPlayer(playerId);
        
        ActionServerTelemetry.PlayerConnections.Add(1, 
            new KeyValuePair<string, object?>("player.id", playerId), 
            new KeyValuePair<string, object?>("success", result.ToString()));
        if (result)
        {
            ActionServerTelemetry.ActivePlayers.Add(1);
        }
        
        return result;
    }

    public Task DisconnectPlayer(string playerId)
    {
        using var activity = ActionServerTelemetry.ActivitySource.StartActivity("ActionServer.PlayerDisconnection");
        activity?.SetTag("player.id", playerId);
        
        _simulation.RemovePlayer(playerId);
        
        ActionServerTelemetry.PlayerDisconnections.Add(1, new KeyValuePair<string, object?>("player.id", playerId));
        ActionServerTelemetry.ActivePlayers.Add(-1);
        
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
        
        _logger.LogInformation("[ZONE_TRANSITION_SERVER] Zone monitoring started for zone ({X},{Y})", 
            _simulation.GetAssignedSquare().X, _simulation.GetAssignedSquare().Y);
        
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var checkStart = DateTime.UtcNow;
                _logger.LogDebug("[ZONE_TRANSITION_SERVER] Zone monitor checking for transfers at {Time}", checkStart);
                
                // Handle player transfers
                var playersOutside = await _simulation.GetPlayersOutsideZone();
                
                if (playersOutside.Count > 0)
                {
                    _logger.LogInformation("[ZONE_TRANSITION_SERVER] Zone monitor found {Count} players outside zone at {Time}", 
                        playersOutside.Count, DateTime.UtcNow);
                }
                
                foreach (var playerId in playersOutside)
                {
                    var playerInfo = _simulation.GetPlayerInfo(playerId);
                    if (playerInfo != null)
                    {
                        var transferStart = DateTime.UtcNow;
                        _logger.LogInformation("[ZONE_TRANSITION_SERVER] Player {PlayerId} at position {Position} is outside zone, initiating transfer", 
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
                        using var transferActivity = ActionServerTelemetry.ActivitySource.StartActivity(ActionServerTelemetry.ZoneTransferActivity);
                        transferActivity?.SetTag("player.id", playerId);
                        transferActivity?.SetTag("from.zone.x", assignedZone.X);
                        transferActivity?.SetTag("from.zone.y", assignedZone.Y);
                        transferActivity?.SetTag("to.zone.x", currentZone.X);
                        transferActivity?.SetTag("to.zone.y", currentZone.Y);
                        
                        var grainStart = DateTime.UtcNow;
                        var transferInfo = await worldManager.InitiatePlayerTransfer(playerId, playerInfo.Position);
                        var grainDuration = (DateTime.UtcNow - grainStart).TotalMilliseconds;
                        
                        ActionServerTelemetry.ZoneTransfers.Add(1, 
                            new KeyValuePair<string, object?>("player.id", playerId),
                            new KeyValuePair<string, object?>("from.zone", $"{assignedZone.X},{assignedZone.Y}"),
                            new KeyValuePair<string, object?>("to.zone", $"{currentZone.X},{currentZone.Y}"));
                        ActionServerTelemetry.ZoneTransferDuration.Record(grainDuration, new KeyValuePair<string, object?>("operation", "initiate"));
                        
                        _logger.LogInformation("[ZONE_TRANSITION_SERVER] InitiatePlayerTransfer took {Duration}ms for player {PlayerId}", 
                            grainDuration, playerId);
                        _logger.LogInformation("[ZONE_TRANSITION_SERVER] InitiatePlayerTransfer grain call took {Duration}ms", grainDuration);
                        if (transferInfo?.NewServer != null)
                        {
                            _logger.LogInformation("[ZONE_TRANSITION_SERVER] Transferring player {PlayerId} from position {Position} to server {ServerId} for zone {Zone}", 
                                playerId, playerInfo.Position, transferInfo.NewServer.ServerId, transferInfo.NewServer.AssignedSquare);
                            
                            // Try to transfer player to the new server
                            var worldState = _simulation.GetCurrentState();
                            var playerEntity = worldState.Entities.FirstOrDefault(e => e.EntityId == playerId);
                            
                            if (playerEntity != null)
                            {
                                var transferred = await TransferEntityToServer(transferInfo.NewServer, playerEntity);
                                if (transferred)
                                {
                                    var totalDuration = (DateTime.UtcNow - transferStart).TotalMilliseconds;
                                    _logger.LogInformation("[ZONE_TRANSITION_SERVER] Successfully transferred player {PlayerId} to new server, total duration: {Duration}ms", 
                                        playerId, totalDuration);
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
                
                // Handle entity transfers (enemies only - bullets are handled via trajectory transfer)
                var entitiesOutside = await _simulation.GetEntitiesOutsideZone();
                foreach (var (entityId, position, type, subType) in entitiesOutside)
                {
                    // Skip bullets - they're handled via trajectory transfer
                    if (type == EntityType.Bullet)
                    {
                        // Just remove the bullet from this zone - it will appear in the other zone via trajectory transfer
                        _simulation.RemovePlayer(entityId);
                        continue;
                    }
                    
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
            
            await Task.Delay(Shooter.Shared.GameConstants.ZoneTransferCheckInterval, cancellationToken); // Check at configurable interval for faster transfers
        }
    }
    
    private async Task<bool> TransferEntityToServer(ActionServerInfo targetServer, EntityState entity)
    {
        using var activity = ActionServerTelemetry.ActivitySource.StartActivity(ActionServerTelemetry.EntityTransferActivity);
        activity?.SetTag("entity.id", entity.EntityId);
        activity?.SetTag("entity.type", entity.Type.ToString());
        activity?.SetTag("target.server", targetServer.ServerId);
        
        var stopwatch = Stopwatch.StartNew();
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
                        // Add RPC protocol assembly for RPC message serialization
                        serializer.AddAssembly(typeof(Granville.Rpc.Protocol.RpcMessage).Assembly);
                    });
                })
                .Build();
                
            await hostBuilder.StartAsync();
            
            try
            {
                var rpcClient = hostBuilder.Services.GetRequiredService<Orleans.IClusterClient>();
                
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
                
                ActionServerTelemetry.EntityTransfers.Add(1,
                    new KeyValuePair<string, object?>("entity.type", entity.Type.ToString()),
                    new KeyValuePair<string, object?>("target.server", targetServer.ServerId),
                    new KeyValuePair<string, object?>("success", transferred.ToString()));
                
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
            
            ActionServerTelemetry.EntityTransfers.Add(1,
                new KeyValuePair<string, object?>("entity.type", entity.Type.ToString()),
                new KeyValuePair<string, object?>("target.server", targetServer.ServerId),
                new KeyValuePair<string, object?>("success", "false"),
                new KeyValuePair<string, object?>("error", ex.GetType().Name));
            
            return false;
        }
        finally
        {
            stopwatch.Stop();
            ActionServerTelemetry.RpcCallDuration.Record(stopwatch.Elapsed.TotalMilliseconds, 
                new KeyValuePair<string, object?>("operation", "entity_transfer"),
                new KeyValuePair<string, object?>("target.server", targetServer.ServerId));
        }
    }

    public ZoneInfo? GetZoneInfo()
    {
        var square = _simulation.GetAssignedSquare();
        if (square.X == 0 && square.Y == 0)
        {
            // Zone not yet assigned
            return null;
        }

        return new ZoneInfo
        {
            ZoneId = square.X * 1000 + square.Y, // Simple zone ID calculation
            X = square.X,
            Y = square.Y
        };
    }
}

public class ZoneInfo
{
    public int ZoneId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
}