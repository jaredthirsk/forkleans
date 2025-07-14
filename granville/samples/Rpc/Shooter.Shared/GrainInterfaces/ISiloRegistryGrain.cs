using Orleans;

namespace Shooter.Shared.GrainInterfaces;

/// <summary>
/// Grain interface for tracking active Orleans silos and their SignalR endpoints.
/// </summary>
public interface ISiloRegistryGrain : IGrainWithIntegerKey
{
    /// <summary>
    /// Register a silo with its connection information.
    /// </summary>
    Task RegisterSilo(SiloInfo info);
    
    /// <summary>
    /// Unregister a silo when it shuts down.
    /// </summary>
    Task UnregisterSilo(string siloId);
    
    /// <summary>
    /// Update heartbeat timestamp for a silo.
    /// </summary>
    Task UpdateHeartbeat(string siloId);
    
    /// <summary>
    /// Get list of all active silos.
    /// </summary>
    Task<List<SiloInfo>> GetActiveSilos();
    
    /// <summary>
    /// Get a random active silo for load balancing.
    /// </summary>
    Task<SiloInfo?> GetRandomSilo();
    
    /// <summary>
    /// Get a specific silo by ID.
    /// </summary>
    Task<SiloInfo?> GetSilo(string siloId);
}

/// <summary>
/// Information about a registered Orleans silo.
/// </summary>
[GenerateSerializer]
public class SiloInfo
{
    [Id(0)] public string SiloId { get; set; } = string.Empty;
    [Id(1)] public string HttpEndpoint { get; set; } = string.Empty;
    [Id(2)] public string HttpsEndpoint { get; set; } = string.Empty;
    [Id(3)] public string SignalRUrl { get; set; } = string.Empty;
    [Id(4)] public DateTime LastHeartbeat { get; set; }
    [Id(5)] public string IpAddress { get; set; } = string.Empty;
    [Id(6)] public int HttpPort { get; set; }
    [Id(7)] public int HttpsPort { get; set; }
    [Id(8)] public bool IsPrimary { get; set; }
}