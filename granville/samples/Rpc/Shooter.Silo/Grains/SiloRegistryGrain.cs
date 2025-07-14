using Orleans;
using Orleans.Runtime;
using Shooter.Shared.GrainInterfaces;
using System.Collections.Concurrent;

namespace Shooter.Silo.Grains;

/// <summary>
/// Grain implementation for tracking active Orleans silos and their SignalR endpoints.
/// </summary>
public class SiloRegistryGrain : Grain, ISiloRegistryGrain
{
    private readonly ILogger<SiloRegistryGrain> _logger;
    private readonly ConcurrentDictionary<string, SiloInfo> _silos = new();
    private readonly Random _random = new();
    private readonly TimeSpan _heartbeatTimeout = TimeSpan.FromMinutes(2);

    public SiloRegistryGrain(ILogger<SiloRegistryGrain> logger)
    {
        _logger = logger;
    }

    public Task RegisterSilo(SiloInfo info)
    {
        info.LastHeartbeat = DateTime.UtcNow;
        _silos[info.SiloId] = info;
        
        _logger.LogInformation(
            "Registered silo {SiloId} at {HttpsEndpoint} (SignalR: {SignalRUrl}), Primary: {IsPrimary}", 
            info.SiloId, info.HttpsEndpoint, info.SignalRUrl, info.IsPrimary);
        
        return Task.CompletedTask;
    }

    public Task UnregisterSilo(string siloId)
    {
        if (_silos.TryRemove(siloId, out var removed))
        {
            _logger.LogInformation("Unregistered silo {SiloId} at {HttpsEndpoint}", 
                siloId, removed.HttpsEndpoint);
        }
        
        return Task.CompletedTask;
    }

    public Task UpdateHeartbeat(string siloId)
    {
        if (_silos.TryGetValue(siloId, out var silo))
        {
            silo.LastHeartbeat = DateTime.UtcNow;
            _logger.LogDebug("Updated heartbeat for silo {SiloId}", siloId);
        }
        
        return Task.CompletedTask;
    }

    public Task<List<SiloInfo>> GetActiveSilos()
    {
        // Clean up stale silos
        var cutoff = DateTime.UtcNow - _heartbeatTimeout;
        var staleSilos = _silos.Where(kvp => kvp.Value.LastHeartbeat < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();
        
        foreach (var siloId in staleSilos)
        {
            if (_silos.TryRemove(siloId, out var removed))
            {
                _logger.LogWarning("Removed stale silo {SiloId} (last heartbeat: {LastHeartbeat})", 
                    siloId, removed.LastHeartbeat);
            }
        }
        
        var activeSilos = _silos.Values.OrderBy(s => s.SiloId).ToList();
        _logger.LogDebug("Returning {Count} active silos", activeSilos.Count);
        
        return Task.FromResult(activeSilos);
    }

    public async Task<SiloInfo?> GetRandomSilo()
    {
        var activeSilos = await GetActiveSilos();
        
        if (activeSilos.Count == 0)
        {
            _logger.LogWarning("No active silos available");
            return null;
        }
        
        var index = _random.Next(activeSilos.Count);
        var selected = activeSilos[index];
        
        _logger.LogDebug("Selected random silo {SiloId} from {Count} active silos", 
            selected.SiloId, activeSilos.Count);
        
        return selected;
    }

    public Task<SiloInfo?> GetSilo(string siloId)
    {
        _silos.TryGetValue(siloId, out var silo);
        return Task.FromResult(silo);
    }
}