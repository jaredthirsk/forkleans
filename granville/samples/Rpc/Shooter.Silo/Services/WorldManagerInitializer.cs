using Orleans;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Shooter.Shared.GrainInterfaces;

namespace Shooter.Silo.Services;

/// <summary>
/// Ensures the WorldManagerGrain is activated on startup to enable timer functionality
/// </summary>
public class WorldManagerInitializer : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WorldManagerInitializer> _logger;

    public WorldManagerInitializer(IServiceProvider serviceProvider, ILogger<WorldManagerInitializer> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Wait longer for the silo to be fully initialized
        await Task.Delay(5000, cancellationToken);
        
        _logger.LogInformation("Initializing WorldManagerGrain to ensure timer functionality");
        
        try
        {
            // Get IGrainFactory from service provider after Orleans is initialized
            var grainFactory = _serviceProvider.GetService<Orleans.IGrainFactory>();
            if (grainFactory == null)
            {
                _logger.LogWarning("IGrainFactory not available yet, skipping initialization");
                return;
            }
            
            // Get the WorldManagerGrain - this will activate it if not already active
            var worldManager = grainFactory.GetGrain<IWorldManagerGrain>(0);
            
            // Call a simple method to ensure it's activated
            var servers = await worldManager.GetAllActionServers();
            var serverCount = servers?.Count ?? 0;
            
            _logger.LogInformation("WorldManagerGrain initialized successfully. Active ActionServers: {Count}", serverCount);
        }
        catch (Exception ex)
        {
            // Log the error but don't fail startup - the grain can be activated later
            _logger.LogWarning(ex, "Failed to initialize WorldManagerGrain on startup, it will be activated on first use");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}