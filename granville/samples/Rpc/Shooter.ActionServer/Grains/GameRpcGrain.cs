using Orleans;
using Orleans.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Shooter.ActionServer.Services;
using Shooter.ActionServer.Simulation;
using Shooter.Shared.Models;
using Shooter.Shared.RpcInterfaces;
using Shooter.Shared.GrainInterfaces;
using System.Threading;

namespace Shooter.ActionServer.Grains;

/// <summary>
/// Grain implementation that exposes game functionality via Orleans RPC.
/// This grain runs in the ActionServer process and has direct access to game services.
/// Note: This uses Orleans.Grain for RPC compatibility.
/// </summary>
[GrainType("game-rpc-grain")]
public class GameRpcGrain : Orleans.Grain, IGameRpcGrain
{
    private readonly GameService _gameService;
    private readonly IWorldSimulation _worldSimulation;
    private readonly ILogger<GameRpcGrain> _logger;
    private readonly Orleans.IClusterClient _orleansClient;
    private readonly ObserverManager<IGameRpcObserver> _observers;
    private readonly GameEventBroker _gameEventBroker;
    private readonly CrossZoneRpcService _crossZoneRpc;
    private readonly List<ChatMessage> _recentChatMessages = new();
    private readonly TimeSpan _chatMessageRetention = TimeSpan.FromMinutes(5);
    private Timer? _networkStatsTimer;
    private readonly NetworkStatisticsTracker _networkStatsTracker;

    public GameRpcGrain(
        GameService gameService,
        IWorldSimulation worldSimulation,
        ILogger<GameRpcGrain> logger,
        Orleans.IClusterClient orleansClient,
        GameEventBroker gameEventBroker,
        CrossZoneRpcService crossZoneRpc)
    {
        _gameService = gameService;
        _worldSimulation = worldSimulation;
        _logger = logger;
        _orleansClient = orleansClient;
        _gameEventBroker = gameEventBroker;
        _crossZoneRpc = crossZoneRpc;
        _observers = new ObserverManager<IGameRpcObserver>(TimeSpan.FromMinutes(5), logger);
        
        // Subscribe to GameEventBroker events
        _gameEventBroker.SubscribeToGameOver(NotifyObserversGameOver);
        _gameEventBroker.SubscribeToVictoryPause(NotifyObserversVictoryPause);
        _gameEventBroker.SubscribeToGameRestart(NotifyObserversGameRestarted);
        _gameEventBroker.SubscribeToChatMessage(NotifyObserversChat);
        _logger.LogInformation("GameRpcGrain subscribed to GameEventBroker events");
        
        // Initialize network stats tracker
        _networkStatsTracker = new NetworkStatisticsTracker(_worldSimulation.GetAssignedSquare().ToString());
        
        // Start network stats timer
        _networkStatsTimer = new Timer(SendNetworkStats, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public async Task<string> ConnectPlayer(string playerId)
    {
        try
        {
            // Validate input
            if (string.IsNullOrEmpty(playerId))
            {
                _logger.LogError("RPC: ConnectPlayer called with null or empty playerId");
                return "FAILED";
            }
            
            _logger.LogInformation("RPC: Player {PlayerId} connecting via Orleans RPC", playerId);
            
            var result = await _gameService.ConnectPlayer(playerId);
            
            _logger.LogInformation("RPC: ConnectPlayer returning {Result} for player {PlayerId}", result, playerId);
            return result ? "SUCCESS" : "FAILED";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RPC: Exception in ConnectPlayer for player {PlayerId}: {Message}", playerId, ex.Message);
            return "FAILED";
        }
    }

    public async Task DisconnectPlayer(string playerId)
    {
        _logger.LogInformation("RPC: Player {PlayerId} disconnecting via Orleans RPC", playerId);
        await _gameService.DisconnectPlayer(playerId);
    }

    public async Task<WorldState> GetWorldState()
    {
        return await Task.FromResult(_worldSimulation.GetCurrentState());
    }

    public async Task UpdatePlayerInput(string playerId, Vector2 moveDirection, bool isShooting)
    {
        // Check if we're in victory pause mode - block all input during pause
        if (_worldSimulation.GetCurrentPhase() == GamePhase.VictoryPause)
        {
            _logger.LogDebug("[RPC_INPUT] Blocked input for player {PlayerId} - Victory pause active", playerId);
            return;
        }
        
        await _gameService.UpdatePlayerInput(playerId, moveDirection, isShooting);
    }
    
    public async Task UpdatePlayerInputEx(string playerId, Vector2? moveDirection, Vector2? shootDirection)
    {
        // Check if we're in victory pause mode - block all input during pause
        if (_worldSimulation.GetCurrentPhase() == GamePhase.VictoryPause)
        {
            _logger.LogDebug("[RPC_INPUT] Blocked input for player {PlayerId} - Victory pause active", playerId);
            return;
        }
        
        // Log input receipt to help debug cross-control issues
        if (moveDirection.HasValue || shootDirection.HasValue)
        {
            _logger.LogDebug("[RPC_INPUT] Received input for player {PlayerId} - Move: {Move}, Shoot: {Shoot}", 
                playerId, moveDirection.HasValue, shootDirection.HasValue);
        }
        
        await _gameService.UpdatePlayerInputEx(playerId, moveDirection, shootDirection);
    }
    
    public async Task UpdatePlayerInputSimple(string playerId, double moveX, double moveY, bool isShooting)
    {
        // Check if we're in victory pause mode - block all input during pause
        if (_worldSimulation.GetCurrentPhase() == GamePhase.VictoryPause)
        {
            _logger.LogDebug("[RPC_INPUT] Blocked input for player {PlayerId} - Victory pause active", playerId);
            return;
        }
        
        // Convert simple doubles to Vector2 for game service
        var moveDirection = (moveX != 0 || moveY != 0) ? new Vector2((float)moveX, (float)moveY) : Vector2.Zero;
        
        _logger.LogDebug("[RPC_INPUT_SIMPLE] Received simple input for player {PlayerId} - Move: ({X}, {Y}), Shoot: {Shoot}", 
            playerId, moveX, moveY, isShooting);
        
        await _gameService.UpdatePlayerInput(playerId, moveDirection, isShooting);
    }

    public async Task<bool> TransferEntityIn(string entityId, EntityType type, int subType, Vector2 position, Vector2 velocity, float health)
    {
        _logger.LogInformation("RPC: Transferring entity {EntityId} via Orleans RPC", entityId);
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
    
    public Task<double> GetServerFps()
    {
        return Task.FromResult(_worldSimulation.GetServerFps());
    }
    
    public Task TransferBulletTrajectory(string bulletId, int subType, Vector2 origin, Vector2 velocity, float spawnTime, float lifespan, string? ownerId, int team = 0)
    {
        var currentZone = _worldSimulation.GetAssignedSquare();
        _logger.LogDebug("RPC: Zone ({ZoneX},{ZoneY}) receiving bullet trajectory {BulletId} with origin {Origin}, velocity {Velocity}, lifespan {Lifespan}, team {Team}", 
            currentZone.X, currentZone.Y, bulletId, origin, velocity, lifespan, team);
        
        // Transfer the bullet trajectory to the world simulation
        _worldSimulation.ReceiveBulletTrajectory(bulletId, subType, origin, velocity, spawnTime, lifespan, ownerId, team);
        
        return Task.CompletedTask;
    }
    
    public Task Subscribe(IGameRpcObserver observer)
    {
        _observers.Subscribe(observer, observer);
        _logger.LogInformation("[CHAT_DEBUG] RPC: Observer subscribed to game updates. Total observers: {ObserverCount}", _observers.Count);
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
    public async IAsyncEnumerable<WorldState> StreamWorldStateUpdates()
    {
        _logger.LogInformation("RPC: Starting world state stream");
        
        while (true)
        {
            var state = _worldSimulation.GetCurrentState();
            yield return state;
            
            // Stream at 60 FPS
            await Task.Delay(16);
        }
    }
    
    public async IAsyncEnumerable<ZoneStatistics> StreamZoneStatistics()
    {
        _logger.LogInformation("RPC: Starting zone statistics stream");
        
        while (true)
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
            await Task.Delay(1000);
        }
    }
    
    public async IAsyncEnumerable<AdjacentZoneEntities> StreamAdjacentZoneEntities(string playerId)
    {
        _logger.LogInformation("RPC: Starting adjacent zone entities stream for player {PlayerId}", playerId);
        
        while (true)
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
            await Task.Delay(100);
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
    /// Called by WorldSimulation to notify all connected clients about victory pause.
    /// </summary>
    public void NotifyObserversVictoryPause(VictoryPauseMessage victoryPauseMessage)
    {
        _logger.LogInformation("Notifying {ObserverCount} observers about victory pause", _observers.Count);
        _observers.Notify(observer => observer.OnVictoryPause(victoryPauseMessage));
    }
    
    /// <summary>
    /// Called by WorldSimulation to notify all connected clients about game restart.
    /// </summary>
    public void NotifyObserversGameRestarted()
    {
        _logger.LogInformation("Notifying {ObserverCount} observers about game restart", _observers.Count);
        _observers.Notify(observer => observer.OnGameRestarted());
    }
    
    public async Task SendChatMessage(ChatMessage message)
    {
        _logger.LogInformation("[CHAT_DEBUG] RPC: Received chat message from {Sender}: {Message}", 
            message.SenderName, message.Message);
        
        // Store message for polling fallback
        lock (_recentChatMessages)
        {
            _recentChatMessages.Add(message);
            
            // Clean up old messages
            var cutoff = DateTime.UtcNow - _chatMessageRetention;
            _recentChatMessages.RemoveAll(m => m.Timestamp < cutoff);
            
            _logger.LogInformation("[CHAT_DEBUG] Stored message in history. Total messages: {Count}", _recentChatMessages.Count);
        }
        
        // Notify all observers (connected clients) in this zone
        NotifyObserversChat(message);
        
        // Also raise the chat message event for cross-zone broadcasting via SignalR
        _gameEventBroker.RaiseChatMessage(message);
        
        // Forward the message to all other ActionServers so players in different zones can see it
        await ForwardChatMessageToAllZones(message);
    }
    
    /// <summary>
    /// Forwards a chat message to all other ActionServers so players in different zones can see it.
    /// </summary>
    private async Task ForwardChatMessageToAllZones(ChatMessage message)
    {
        try
        {
            _logger.LogInformation("[CHAT_DEBUG] Forwarding chat message to all zones from {Sender}", message.SenderName);
            
            // Get all ActionServers from the WorldManager
            var worldManager = _orleansClient.GetGrain<IWorldManagerGrain>(0);
            var allServers = await worldManager.GetAllActionServers();
            
            // Get our current server info to avoid forwarding to ourselves
            var currentZone = _worldSimulation.GetAssignedSquare();
            var forwardTasks = new List<Task>();
            
            foreach (var server in allServers)
            {
                // Skip our own server
                if (server.AssignedSquare.Equals(currentZone))
                {
                    continue;
                }
                
                // Forward the message to this server
                var forwardTask = ForwardChatMessageToZone(server, message);
                forwardTasks.Add(forwardTask);
            }
            
            // Wait for all forwards to complete, but don't block indefinitely
            await Task.WhenAll(forwardTasks.ToArray()).WaitAsync(TimeSpan.FromSeconds(5));
            
            _logger.LogInformation("[CHAT_DEBUG] Successfully forwarded chat message to {Count} other zones", forwardTasks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CHAT_DEBUG] Failed to forward chat message to other zones");
        }
    }
    
    /// <summary>
    /// Forwards a chat message to a specific zone.
    /// </summary>
    private async Task ForwardChatMessageToZone(ActionServerInfo targetServer, ChatMessage message)
    {
        try
        {
            _logger.LogDebug("[CHAT_DEBUG] Forwarding message to zone ({ZoneX},{ZoneY}) server {ServerId}", 
                targetServer.AssignedSquare.X, targetServer.AssignedSquare.Y, targetServer.ServerId);
            
            // Get the game grain for the target server
            var targetGameGrain = await _crossZoneRpc.GetGameGrainForServer(targetServer);
            
            // Call ReceiveChatMessageFromOtherZone instead of SendChatMessage to avoid infinite recursion
            await targetGameGrain.ReceiveChatMessageFromOtherZone(message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[CHAT_DEBUG] Failed to forward message to zone ({ZoneX},{ZoneY})", 
                targetServer.AssignedSquare.X, targetServer.AssignedSquare.Y);
        }
    }
    
    /// <summary>
    /// Receives a chat message forwarded from another zone/server.
    /// This method prevents infinite recursion by not forwarding the message again.
    /// </summary>
    public Task ReceiveChatMessageFromOtherZone(ChatMessage message)
    {
        _logger.LogInformation("[CHAT_DEBUG] RPC: Received forwarded chat message from {Sender} (other zone): {Message}", 
            message.SenderName, message.Message);
        
        // Store message for polling fallback
        lock (_recentChatMessages)
        {
            _recentChatMessages.Add(message);
            
            // Clean up old messages
            var cutoff = DateTime.UtcNow - _chatMessageRetention;
            _recentChatMessages.RemoveAll(m => m.Timestamp < cutoff);
        }
        
        // Notify observers (connected clients) in this zone about the forwarded message
        NotifyObserversChat(message);
        
        // Also raise the chat message event for local GameEventBroker (for SignalR etc.)
        _gameEventBroker.RaiseChatMessage(message);
        
        // Important: Do NOT call ForwardChatMessageToAllZones() here to avoid infinite recursion
        
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// Called to notify all connected clients about a chat message.
    /// </summary>
    public void NotifyObserversChat(ChatMessage message)
    {
        _logger.LogInformation("[CHAT_DEBUG] Notifying {ObserverCount} observers about chat message from {Sender}", _observers.Count, message.SenderName);
        
        if (_observers.Count == 0)
        {
            _logger.LogWarning("[CHAT_DEBUG] No observers to notify about chat message!");
        }
        
        _observers.Notify(observer => observer.OnChatMessage(message));
    }
    
    public Task<List<ChatMessage>> GetRecentChatMessages(DateTime since)
    {
        lock (_recentChatMessages)
        {
            var messages = _recentChatMessages
                .Where(m => m.Timestamp > since)
                .OrderBy(m => m.Timestamp)
                .ToList();
                
            _logger.LogDebug("[CHAT_DEBUG] Returning {Count} messages since {Since}", messages.Count, since);
            return Task.FromResult(messages);
        }
    }
    
    public Task NotifyBulletDestroyed(string bulletId)
    {
        _logger.LogTrace("RPC: Received notification to destroy bullet {BulletId}", bulletId);
        
        // Immediately remove the bullet from our world simulation
        _worldSimulation.RemoveBullet(bulletId);
        
        return Task.CompletedTask;
    }
    
    public Task<NetworkStatistics> GetNetworkStatistics()
    {
        var stats = _networkStatsTracker.GetStats();
        stats.ServerId = $"Zone({_worldSimulation.GetAssignedSquare().X},{_worldSimulation.GetAssignedSquare().Y})";
        return Task.FromResult(stats);
    }
    
    public Task<DateTime> GetServerTime()
    {
        // Simple lightweight method that returns server time
        // Used for heartbeat/connectivity checks
        return Task.FromResult(DateTime.UtcNow);
    }
    
    private void SendNetworkStats(object? state)
    {
        try
        {
            var stats = _networkStatsTracker.GetStats();
            stats.ServerId = $"Zone({_worldSimulation.GetAssignedSquare().X},{_worldSimulation.GetAssignedSquare().Y})";
            
            // Only log if there are observers to notify
            if (_observers.Count > 0)
            {
                _logger.LogDebug("[NETWORK_STATS] Sending network stats to {ObserverCount} observers. Stats: Sent={Sent}, Recv={Recv}", 
                    _observers.Count, stats.PacketsSent, stats.PacketsReceived);
            }
            
            // Notify all observers about network stats
            _observers.Notify(observer => observer.OnNetworkStatsUpdated(stats));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send network statistics");
        }
    }
    
    public void Dispose()
    {
        _networkStatsTimer?.Dispose();
    }
}