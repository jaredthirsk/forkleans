using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shooter.ActionServer.Simulation;
using Shooter.Shared.GrainInterfaces;

namespace Shooter.ActionServer.Services;

public class StatsReportingService : BackgroundService
{
    private readonly ILogger<StatsReportingService> _logger;
    private readonly IWorldSimulation _worldSimulation;
    private readonly Forkleans.IClusterClient _orleansClient;
    private readonly string _serverId;
    private readonly TimeSpan _reportInterval = TimeSpan.FromSeconds(30); // Report every 30 seconds

    public StatsReportingService(
        ILogger<StatsReportingService> logger,
        IWorldSimulation worldSimulation,
        Forkleans.IClusterClient orleansClient,
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
                // Get damage report from world simulation
                var damageReport = _worldSimulation.GetDamageReport();
                
                if (damageReport.DamageEvents.Count > 0 || damageReport.PlayerStats.Count > 0)
                {
                    _logger.LogInformation("Reporting stats to Silo: {EventCount} damage events, {PlayerCount} players",
                        damageReport.DamageEvents.Count, damageReport.PlayerStats.Count);
                    
                    // Get the stats collector grain
                    var statsCollector = _orleansClient.GetGrain<IStatsCollectorGrain>(0);
                    
                    // Report the stats
                    await statsCollector.ReportZoneDamageStats(_serverId, damageReport);
                }
                else
                {
                    _logger.LogDebug("No damage events to report");
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