using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shooter.ActionServer.Simulation;
using Shooter.Shared.GrainInterfaces;
using Shooter.Shared.Models;

namespace Shooter.ActionServer.Services;

public class StatsReportingService : BackgroundService
{
    private readonly ILogger<StatsReportingService> _logger;
    private readonly IWorldSimulation _worldSimulation;
    private readonly Orleans.IClusterClient _orleansClient;
    private readonly string _serverId;
    private readonly TimeSpan _reportInterval = TimeSpan.FromSeconds(30); // Report every 30 seconds

    public StatsReportingService(
        ILogger<StatsReportingService> logger,
        IWorldSimulation worldSimulation,
        Orleans.IClusterClient orleansClient,
        IConfiguration configuration)
    {
        _logger = logger;
        _worldSimulation = worldSimulation;
        _orleansClient = orleansClient;
        
        // Generate server ID from configuration
        var instanceId = configuration["ASPIRE_INSTANCE_ID"] ?? "0";
        _serverId = $"ActionServer-{instanceId}";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Stats reporting service started for server {ServerId}", _serverId);
        
        // Wait for world simulation to be ready
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Get zone stats from world simulation
                var worldState = _worldSimulation.GetCurrentState();
                var zone = _worldSimulation.GetAssignedSquare();
                
                // Count entities by type
                var factoryCount = worldState.Entities.Count(e => e.Type == EntityType.Factory && e.State != EntityStateType.Dead);
                var enemyCount = worldState.Entities.Count(e => e.Type == EntityType.Enemy && e.State != EntityStateType.Dead);
                var playerCount = worldState.Entities.Count(e => e.Type == EntityType.Player && e.SubType == 0 && e.State != EntityStateType.Dead);
                
                // Report zone stats to WorldManager
                var worldManager = _orleansClient.GetGrain<IWorldManagerGrain>(0);
                var zoneStats = new Shooter.Shared.Models.ZoneStats(factoryCount, enemyCount, playerCount);
                await worldManager.ReportZoneStats(zone, zoneStats);
                
                _logger.LogDebug("Reported zone stats for ({X},{Y}): {Players} players, {Enemies} enemies, {Factories} factories",
                    zone.X, zone.Y, playerCount, enemyCount, factoryCount);
                
                // Get damage report from world simulation
                var damageReport = _worldSimulation.GetDamageReport();
                
                if (damageReport.DamageEvents.Count > 0 || damageReport.PlayerStats.Count > 0)
                {
                    _logger.LogInformation("Reporting damage stats to Silo: {EventCount} damage events, {PlayerCount} players",
                        damageReport.DamageEvents.Count, damageReport.PlayerStats.Count);
                    
                    // Get the stats collector grain
                    var statsCollector = _orleansClient.GetGrain<IStatsCollectorGrain>(0);
                    
                    // Report the damage stats
                    await statsCollector.ReportZoneDamageStats(_serverId, damageReport);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reporting stats to Silo");
            }
            
            await Task.Delay(_reportInterval, stoppingToken);
        }
        
        _logger.LogInformation("Stats reporting service stopped");
    }
}