using Orleans;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Shooter.Shared.GrainInterfaces;
using Orleans.Runtime;
using Orleans.Runtime.MembershipService;

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
        _logger.LogInformation("Starting WorldManagerInitializer - waiting for Orleans silo to be ready");
        
        try
        {
            // Wait for Orleans silo to be fully ready with proper lifecycle integration
            await WaitForOrleansReadyAsync(cancellationToken);
            
            _logger.LogInformation("Orleans silo is ready, initializing WorldManagerGrain");
            
            // Get IGrainFactory from service provider after Orleans is initialized
            var grainFactory = _serviceProvider.GetService<Orleans.IGrainFactory>();
            if (grainFactory == null)
            {
                _logger.LogWarning("IGrainFactory not available after Orleans startup, skipping initialization");
                return;
            }
            
            // Initialize WorldManagerGrain with retry logic
            await InitializeWorldManagerWithRetryAsync(grainFactory, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("WorldManagerInitializer startup was cancelled");
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
    
    private async Task WaitForOrleansReadyAsync(CancellationToken cancellationToken)
    {
        const int maxRetries = 30;
        const int retryDelayMs = 1000;
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Check if Orleans silo is ready by verifying key services
                var grainFactory = _serviceProvider.GetService<Orleans.IGrainFactory>();
                var siloStatusOracle = _serviceProvider.GetService<ISiloStatusOracle>();
                var membershipService = _serviceProvider.GetService<IMembershipService>();
                
                if (grainFactory != null && siloStatusOracle != null && membershipService != null)
                {
                    // Check if silo is active and membership service has active silos
                    var currentStatus = siloStatusOracle.CurrentStatus;
                    if (currentStatus == SiloStatus.Active)
                    {
                        // Wait for membership service to report at least one active silo
                        var activeSilos = siloStatusOracle.GetApproximateSiloStatuses(onlyActive: true);
                        if (activeSilos.Count > 0)
                        {
                            _logger.LogInformation("Orleans silo is ready (attempt {Attempt}/{MaxRetries}). Active silos: {Count}", 
                                attempt, maxRetries, activeSilos.Count);
                            return;
                        }
                    }
                }
                
                _logger.LogDebug("Orleans silo not ready yet (attempt {Attempt}/{MaxRetries}). Current status: {Status}", 
                    attempt, maxRetries, siloStatusOracle?.CurrentStatus?.ToString() ?? "Unknown");
                    
                await Task.Delay(retryDelayMs, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error checking Orleans readiness (attempt {Attempt}/{MaxRetries})", attempt, maxRetries);
                await Task.Delay(retryDelayMs, cancellationToken);
            }
        }
        
        throw new TimeoutException($"Orleans silo did not become ready within {maxRetries * retryDelayMs / 1000} seconds");
    }
    
    private async Task InitializeWorldManagerWithRetryAsync(IGrainFactory grainFactory, CancellationToken cancellationToken)
    {
        const int maxRetries = 3;
        const int baseDelayMs = 1000;
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                _logger.LogInformation("Attempting to initialize WorldManagerGrain (attempt {Attempt}/{MaxRetries})", attempt, maxRetries);
                
                // Get the WorldManagerGrain - this will activate it if not already active
                var worldManager = grainFactory.GetGrain<IWorldManagerGrain>(0);
                
                // Call a simple method to ensure it's activated
                var servers = await worldManager.GetAllActionServers();
                var serverCount = servers?.Count ?? 0;
                
                _logger.LogInformation("WorldManagerGrain initialized successfully. Active ActionServers: {Count}", serverCount);
                return;
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                var delayMs = baseDelayMs * (int)Math.Pow(2, attempt - 1); // Exponential backoff
                _logger.LogWarning(ex, "Failed to initialize WorldManagerGrain (attempt {Attempt}/{MaxRetries}). Retrying in {DelayMs}ms", 
                    attempt, maxRetries, delayMs);
                await Task.Delay(delayMs, cancellationToken);
            }
        }
        
        // If we get here, all retries failed
        throw new InvalidOperationException($"Failed to initialize WorldManagerGrain after {maxRetries} attempts");
    }
}