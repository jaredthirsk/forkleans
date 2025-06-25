using Forkleans;
using Forkleans.Runtime;
using Microsoft.Extensions.Logging;
using Shooter.Shared.GrainInterfaces;
using Shooter.Shared.Models;

namespace Shooter.Silo.Grains;

public class StatsCollectorGrain : Forkleans.Grain, IStatsCollectorGrain
{
    private readonly IPersistentState<StatsCollectorState> _state;
    private readonly ILogger<StatsCollectorGrain> _logger;

    public StatsCollectorGrain(
        [PersistentState("statsCollector", "statsStore")] IPersistentState<StatsCollectorState> state,
        ILogger<StatsCollectorGrain> logger)
    {
        _state = state;
        _logger = logger;
    }

    public async Task ReportZoneDamageStats(string serverId, ZoneDamageReport report)
    {
        _logger.LogInformation("Received damage report from server {ServerId} for zone ({X},{Y}) with {PlayerCount} players and {EventCount} damage events",
            serverId, report.Zone.X, report.Zone.Y, report.PlayerStats.Count, report.DamageEvents.Count);

        // Store the latest report for each server
        _state.State.ZoneReports[serverId] = report;

        // Merge player stats into global stats
        foreach (var (playerId, stats) in report.PlayerStats)
        {
            if (_state.State.GlobalPlayerStats.TryGetValue(playerId, out var existingStats))
            {
                // Merge stats
                var mergedDamageDealt = MergeDictionaries(existingStats.DamageDealtByWeapon, stats.DamageDealtByWeapon);
                var mergedDamageReceivedByEnemy = MergeDictionaries(existingStats.DamageReceivedByEnemyType, stats.DamageReceivedByEnemyType);
                var mergedDamageReceivedByWeapon = MergeDictionaries(existingStats.DamageReceivedByWeapon, stats.DamageReceivedByWeapon);

                _state.State.GlobalPlayerStats[playerId] = new PlayerDamageStats(
                    playerId,
                    stats.PlayerName, // Use latest name
                    mergedDamageDealt,
                    mergedDamageReceivedByEnemy,
                    mergedDamageReceivedByWeapon,
                    existingStats.TotalDamageDealt + stats.TotalDamageDealt,
                    existingStats.TotalDamageReceived + stats.TotalDamageReceived
                );
            }
            else
            {
                _state.State.GlobalPlayerStats[playerId] = stats;
            }
        }

        await _state.WriteStateAsync();
    }

    public Task<Dictionary<string, ZoneDamageReport>> GetAllZoneReports()
    {
        return Task.FromResult(new Dictionary<string, ZoneDamageReport>(_state.State.ZoneReports));
    }

    public Task<PlayerDamageStats> GetPlayerStats(string playerId)
    {
        if (_state.State.GlobalPlayerStats.TryGetValue(playerId, out var stats))
        {
            return Task.FromResult(stats);
        }

        // Return empty stats if player not found
        return Task.FromResult(new PlayerDamageStats(
            playerId,
            "Unknown",
            new Dictionary<string, float>(),
            new Dictionary<string, float>(),
            new Dictionary<string, float>(),
            0,
            0
        ));
    }

    public Task<List<PlayerDamageStats>> GetTopPlayersByDamageDealt(int count = 10)
    {
        var topPlayers = _state.State.GlobalPlayerStats.Values
            .OrderByDescending(p => p.TotalDamageDealt)
            .Take(count)
            .ToList();

        return Task.FromResult(topPlayers);
    }

    public Task<List<PlayerDamageStats>> GetTopPlayersByDamageReceived(int count = 10)
    {
        var topPlayers = _state.State.GlobalPlayerStats.Values
            .OrderByDescending(p => p.TotalDamageReceived)
            .Take(count)
            .ToList();

        return Task.FromResult(topPlayers);
    }

    public async Task ClearStats()
    {
        _logger.LogInformation("Clearing all damage statistics");
        _state.State.ZoneReports.Clear();
        _state.State.GlobalPlayerStats.Clear();
        await _state.WriteStateAsync();
    }

    private Dictionary<string, float> MergeDictionaries(Dictionary<string, float> dict1, Dictionary<string, float> dict2)
    {
        var result = new Dictionary<string, float>(dict1);
        foreach (var (key, value) in dict2)
        {
            if (result.ContainsKey(key))
                result[key] += value;
            else
                result[key] = value;
        }
        return result;
    }
}

[Forkleans.GenerateSerializer]
public class StatsCollectorState
{
    public Dictionary<string, ZoneDamageReport> ZoneReports { get; set; } = new();
    public Dictionary<string, PlayerDamageStats> GlobalPlayerStats { get; set; } = new();
}