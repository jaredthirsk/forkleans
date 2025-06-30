using Orleans;

namespace Shooter.Shared.Models;

/// <summary>
/// Aggregated statistics for all zones in the game world.
/// </summary>
[GenerateSerializer]
public class GlobalZoneStats
{
    /// <summary>
    /// Statistics for each active zone, keyed by zone coordinates.
    /// </summary>
    [Id(0)]
    public Dictionary<string, ZoneStatsEntry> ZoneStats { get; set; } = new();
    
    /// <summary>
    /// Total number of players across all zones.
    /// </summary>
    [Id(1)]
    public int TotalPlayers { get; set; }
    
    /// <summary>
    /// Total number of enemies across all zones.
    /// </summary>
    [Id(2)]
    public int TotalEnemies { get; set; }
    
    /// <summary>
    /// Total number of active factories across all zones.
    /// </summary>
    [Id(3)]
    public int TotalFactories { get; set; }
    
    /// <summary>
    /// Number of active zones (zones with at least one entity).
    /// </summary>
    [Id(4)]
    public int ActiveZoneCount { get; set; }
    
    /// <summary>
    /// Timestamp when these statistics were generated.
    /// </summary>
    [Id(5)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Current game phase across all zones.
    /// </summary>
    [Id(6)]
    public GamePhase GlobalGamePhase { get; set; } = GamePhase.Playing;
}

/// <summary>
/// Statistics for a single zone.
/// </summary>
[GenerateSerializer]
public class ZoneStatsEntry
{
    [Id(0)]
    public GridSquare Zone { get; set; } = new GridSquare(0, 0);
    
    [Id(1)]
    public int PlayerCount { get; set; }
    
    [Id(2)]
    public int EnemyCount { get; set; }
    
    [Id(3)]
    public int FactoryCount { get; set; }
    
    [Id(4)]
    public string ServerId { get; set; } = string.Empty;
    
    [Id(5)]
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
}

// GamePhase enum is already defined in WorldModels.cs