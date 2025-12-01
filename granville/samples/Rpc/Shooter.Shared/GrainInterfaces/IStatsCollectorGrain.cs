using Granville.Rpc.Security;
using Orleans;
using Shooter.Shared.Models;

namespace Shooter.Shared.GrainInterfaces;

/// <summary>
/// Stats collector grain for game statistics.
/// Server-only for reporting, client-accessible for reading.
/// </summary>
[Authorize]
public interface IStatsCollectorGrain : IGrainWithIntegerKey
{
    /// <summary>
    /// Report zone damage stats - server only.
    /// </summary>
    [ServerOnly]
    Task ReportZoneDamageStats(string serverId, ZoneDamageReport report);

    /// <summary>
    /// Get all zone reports - client accessible for spectators.
    /// </summary>
    [ClientAccessible]
    Task<Dictionary<string, ZoneDamageReport>> GetAllZoneReports();

    /// <summary>
    /// Get player stats - client accessible for scoreboard.
    /// </summary>
    [ClientAccessible]
    Task<PlayerDamageStats> GetPlayerStats(string playerId);

    /// <summary>
    /// Get top players by damage dealt - client accessible.
    /// </summary>
    [ClientAccessible]
    Task<List<PlayerDamageStats>> GetTopPlayersByDamageDealt(int count = 10);

    /// <summary>
    /// Get top players by damage received - client accessible.
    /// </summary>
    [ClientAccessible]
    Task<List<PlayerDamageStats>> GetTopPlayersByDamageReceived(int count = 10);

    /// <summary>
    /// Clear all stats - server only, admin operation.
    /// </summary>
    [ServerOnly]
    Task ClearStats();
}