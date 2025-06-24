using Forkleans;
using Shooter.Shared.Models;

namespace Shooter.Shared.RpcInterfaces;

/// <summary>
/// Observer interface for receiving game updates from the server.
/// </summary>
public interface IGameRpcObserver : IGrainObserver
{
    /// <summary>
    /// Called when zone statistics are updated.
    /// </summary>
    void OnZoneStatsUpdated(ZoneStatistics stats);
    
    /// <summary>
    /// Called when the list of available zones changes.
    /// </summary>
    void OnAvailableZonesChanged(List<GridSquare> availableZones);
    
    /// <summary>
    /// Called when entities in adjacent zones are updated.
    /// </summary>
    void OnAdjacentEntitiesUpdated(Dictionary<string, List<EntityState>> entitiesByZone);
    
    /// <summary>
    /// Called when a scout alert is triggered.
    /// </summary>
    void OnScoutAlert(ScoutAlert alert);
}

/// <summary>
/// Zone statistics for push updates.
/// </summary>
[GenerateSerializer]
public class ZoneStatistics
{
    [Id(0)] public GridSquare Zone { get; set; }
    [Id(1)] public int PlayerCount { get; set; }
    [Id(2)] public int EntityCount { get; set; }
    [Id(3)] public int BulletCount { get; set; }
    [Id(4)] public float AverageUpdateTime { get; set; }
    [Id(5)] public DateTime LastUpdate { get; set; }
}

/// <summary>
/// Scout alert for push notifications.
/// </summary>
[GenerateSerializer]
public class ScoutAlert
{
    [Id(0)] public string AlertId { get; set; }
    [Id(1)] public GridSquare FromZone { get; set; }
    [Id(2)] public GridSquare ToZone { get; set; }
    [Id(3)] public string EntityId { get; set; }
    [Id(4)] public EntityType EntityType { get; set; }
    [Id(5)] public Vector2 Position { get; set; }
    [Id(6)] public DateTime Timestamp { get; set; }
}