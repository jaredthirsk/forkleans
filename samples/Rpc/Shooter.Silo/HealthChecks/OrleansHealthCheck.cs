using Microsoft.Extensions.Diagnostics.HealthChecks;
using Orleans;
using Shooter.Shared.GrainInterfaces;

namespace Shooter.Silo.HealthChecks;

public class OrleansHealthCheck : IHealthCheck
{
    private readonly Orleans.IGrainFactory _grainFactory;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<OrleansHealthCheck> _logger;

    public OrleansHealthCheck(
        Orleans.IGrainFactory grainFactory,
        IHostApplicationLifetime lifetime,
        ILogger<OrleansHealthCheck> logger)
    {
        _grainFactory = grainFactory;
        _lifetime = lifetime;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if the application has started
            if (!_lifetime.ApplicationStarted.IsCancellationRequested)
            {
                return HealthCheckResult.Degraded("Application is still starting");
            }

            // Try to get the WorldManagerGrain
            var worldManager = _grainFactory.GetGrain<IWorldManagerGrain>(0);
            
            // Perform a simple operation to verify the grain is responsive
            var actionServers = await worldManager.GetAllActionServers();
            
            var data = new Dictionary<string, object>
            {
                { "ActionServerCount", actionServers?.Count ?? 0 },
                { "Status", "Ready" }
            };

            return HealthCheckResult.Healthy("Orleans cluster is healthy and WorldManagerGrain is responsive", data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return HealthCheckResult.Unhealthy("Failed to communicate with Orleans cluster", ex);
        }
    }
}