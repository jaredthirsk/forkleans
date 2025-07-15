using Shooter.Shared.Models;
using Shooter.Shared.RpcInterfaces;
using Microsoft.Extensions.Logging;

namespace Shooter.Client.Common;

/// <summary>
/// Client-side implementation of the game observer for receiving push updates.
/// </summary>
public class GameRpcObserver : IGameRpcObserver
{
    private readonly ILogger<GameRpcObserver> _logger;
    private readonly GranvilleRpcGameClientService _clientService;
    
    public GameRpcObserver(ILogger<GameRpcObserver> logger, GranvilleRpcGameClientService clientService)
    {
        _logger = logger;
        _clientService = clientService;
    }
    
    public void OnZoneStatsUpdated(ZoneStatistics stats)
    {
        _logger.LogDebug("Received zone stats update for zone ({X},{Y}): {PlayerCount} players, {EntityCount} entities", 
            stats.Zone.X, stats.Zone.Y, stats.PlayerCount, stats.EntityCount);
        
        // Update client service with new stats
        _clientService.UpdateZoneStats(stats);
    }
    
    public void OnAvailableZonesChanged(List<GridSquare> availableZones)
    {
        _logger.LogDebug("Received available zones update: {Count} zones", availableZones.Count);
        
        // Update client service with new available zones
        _clientService.UpdateAvailableZones(availableZones);
    }
    
    public void OnAdjacentEntitiesUpdated(Dictionary<string, List<EntityState>> entitiesByZone)
    {
        _logger.LogDebug("Received adjacent entities update for {Count} zones", entitiesByZone.Count);
        
        // Update client service with new adjacent entities
        _clientService.UpdateAdjacentEntities(entitiesByZone);
    }
    
    public void OnScoutAlert(ScoutAlert alert)
    {
        _logger.LogInformation("Received scout alert: Entity {EntityId} of type {EntityType} at ({X},{Y}) from zone ({FromX},{FromY}) to ({ToX},{ToY})", 
            alert.EntityId, alert.EntityType, alert.Position.X, alert.Position.Y,
            alert.FromZone.X, alert.FromZone.Y, alert.ToZone.X, alert.ToZone.Y);
        
        // Handle scout alert
        _clientService.HandleScoutAlert(alert);
    }
    
    public void OnGameOver(GameOverMessage gameOverMessage)
    {
        _logger.LogInformation("Received game over notification. Scores: {Scores}",
            string.Join(", ", gameOverMessage.PlayerScores.Select(s => $"{s.PlayerName}: {s.RespawnCount} deaths")));
        
        // Handle game over
        _clientService.HandleGameOver(gameOverMessage);
    }
    
    public void OnGameRestarted()
    {
        _logger.LogInformation("Received game restart notification");
        
        // Handle game restart
        _clientService.HandleGameRestarted();
    }
    
    public void OnChatMessage(ChatMessage message)
    {
        _logger.LogInformation("[CHAT_DEBUG] Observer received chat message from {Sender}: {Message}", 
            message.SenderName, message.Message);
        
        // Handle chat message
        _clientService.HandleChatMessage(message);
    }
    
    public void OnNetworkStatsUpdated(NetworkStatistics stats)
    {
        _logger.LogDebug("Received network stats: Packets sent={Sent}, received={Received}, latency={Latency}ms", 
            stats.PacketsSent, stats.PacketsReceived, stats.AverageLatency);
        
        // Handle network stats update
        _clientService.HandleNetworkStats(stats);
    }
}