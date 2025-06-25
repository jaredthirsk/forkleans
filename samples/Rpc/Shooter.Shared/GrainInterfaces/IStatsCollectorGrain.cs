using Forkleans;
using Shooter.Shared.Models;

namespace Shooter.Shared.GrainInterfaces;

public interface IStatsCollectorGrain : IGrainWithIntegerKey
{
    Task ReportZoneDamageStats(string serverId, ZoneDamageReport report);
    Task<Dictionary<string, ZoneDamageReport>> GetAllZoneReports();
    Task<PlayerDamageStats> GetPlayerStats(string playerId);
    Task<List<PlayerDamageStats>> GetTopPlayersByDamageDealt(int count = 10);
    Task<List<PlayerDamageStats>> GetTopPlayersByDamageReceived(int count = 10);
    Task ClearStats();
}