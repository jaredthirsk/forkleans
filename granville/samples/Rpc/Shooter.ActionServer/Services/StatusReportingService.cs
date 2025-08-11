using Shooter.Shared.GrainInterfaces;
using Shooter.Shared.Models;
using Shooter.ActionServer.Simulation;

namespace Shooter.ActionServer.Services;

public class StatusReportingService : BackgroundService
{
    private readonly Orleans.IClusterClient _orleansClient;
    private readonly IWorldSimulation _worldSimulation;
    private readonly ILogger<StatusReportingService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private string? _serverId;
    private readonly Queue<DateTime> _frameTimestamps = new();
    private readonly object _fpsLock = new();

    public StatusReportingService(
        Orleans.IClusterClient orleansClient,
        IWorldSimulation worldSimulation,
        ILogger<StatusReportingService> logger,
        IServiceProvider serviceProvider)
    {
        _orleansClient = orleansClient;
        _worldSimulation = worldSimulation;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for the world simulation and registration service to initialize
        await Task.Delay(5000, stoppingToken);

        // Try to get server ID from registration service
        var registrationService = _serviceProvider.GetService<ActionServerRegistrationService>();
        
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(5)); // Report every 5 seconds
        
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await ReportStatus();
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in status reporting loop");
        }
        finally
        {
            timer.Dispose();
        }
    }

    private async Task ReportStatus()
    {
        try
        {
            // Get server ID - we'll use a simple approach for now
            if (string.IsNullOrEmpty(_serverId))
            {
                _serverId = Environment.MachineName + "-" + Environment.ProcessId;
            }

            // Get current world state
            var worldState = _worldSimulation.GetCurrentState();
            
            // Calculate entity counts
            var entityCount = worldState.Entities?.Count ?? 0;
            var playerCount = worldState.Entities?.Count(e => e.Type == EntityType.Player && e.State == EntityStateType.Active) ?? 0;
            var enemyCount = worldState.Entities?.Count(e => e.Type == EntityType.Enemy && e.State == EntityStateType.Active) ?? 0;
            var factoryCount = worldState.Entities?.Count(e => e.Type == EntityType.Factory && e.State == EntityStateType.Active) ?? 0;
            
            // Calculate FPS (simple moving average)
            var fps = CalculateFPS();
            
            // Get memory usage
            var memoryUsage = GC.GetTotalMemory(false);
            
            var status = new ActionServerStatus(
                _serverId,
                entityCount,
                playerCount,
                enemyCount,
                factoryCount,
                fps,
                memoryUsage,
                DateTime.UtcNow
            );

            // Report to WorldManager
            var worldManager = _orleansClient.GetGrain<IWorldManagerGrain>(0);
            await worldManager.UpdateActionServerStatus(status);
            
            _logger.LogTrace("Status reported: {EntityCount} entities, {PlayerCount} players, {EnemyCount} enemies, {FactoryCount} factories, {FPS:F1} FPS",
                entityCount, playerCount, enemyCount, factoryCount, fps);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to report status");
        }
    }

    private double CalculateFPS()
    {
        lock (_fpsLock)
        {
            var now = DateTime.UtcNow;
            
            // Add current frame timestamp
            _frameTimestamps.Enqueue(now);
            
            // Remove timestamps older than 1 second
            while (_frameTimestamps.Count > 0 && (now - _frameTimestamps.Peek()).TotalSeconds > 1.0)
            {
                _frameTimestamps.Dequeue();
            }
            
            // Calculate FPS based on frames in the last second
            return _frameTimestamps.Count;
        }
    }

    public void RecordFrame()
    {
        // This can be called by WorldSimulation to record frame updates
        lock (_fpsLock)
        {
            _frameTimestamps.Enqueue(DateTime.UtcNow);
        }
    }
}