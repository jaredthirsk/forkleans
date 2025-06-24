using Forkleans;
using Forkleans.Concurrency;
using Forkleans.Rpc;
using Shooter.Shared.Models;

namespace Shooter.Shared.RpcInterfaces;

[Forkleans.GenerateSerializer]
[Forkleans.Alias("ConnectPlayerResult")]
public record ConnectPlayerResult
{
    [Forkleans.Id(0)]
    public bool Success { get; init; }
    
    public ConnectPlayerResult(bool success)
    {
        Success = success;
    }
}

/// <summary>
/// Grain interface that can be accessed via Forkleans RPC.
/// Forkleans RPC exposes grain interfaces over UDP/TCP.
/// Uses Forkleans types, not Orleans types, for RPC compatibility.
/// Implements IZoneAwareGrain to enable zone-based routing.
/// </summary>
public interface IGameRpcGrain : Forkleans.Rpc.IRpcGrainInterfaceWithStringKey, Forkleans.Rpc.IZoneAwareGrain
{
    [RpcMethod(DeliveryMode = RpcDeliveryMode.Reliable)]
    Task<string> ConnectPlayer(string playerId);
    
    [RpcMethod(DeliveryMode = RpcDeliveryMode.Reliable)]
    Task DisconnectPlayer(string playerId);
    
    [RpcMethod(DeliveryMode = RpcDeliveryMode.Unreliable)]
    Task<WorldState> GetWorldState();
    
    [RpcMethod(DeliveryMode = RpcDeliveryMode.Unreliable)]
    Task UpdatePlayerInput(string playerId, Vector2 moveDirection, bool isShooting);
    
    [RpcMethod(DeliveryMode = RpcDeliveryMode.Unreliable)]
    Task UpdatePlayerInputEx(string playerId, Vector2? moveDirection, Vector2? shootDirection);
    
    [RpcMethod(DeliveryMode = RpcDeliveryMode.ReliableOrdered)]
    Task<bool> TransferEntityIn(string entityId, EntityType type, int subType, Vector2 position, Vector2 velocity, float health);
    
    [RpcMethod(DeliveryMode = RpcDeliveryMode.Reliable)]
    Task<List<GridSquare>> GetAvailableZones();
    
    [RpcMethod(DeliveryMode = RpcDeliveryMode.Reliable)]
    [OneWay]
    Task ReceiveScoutAlert(GridSquare playerZone, Vector2 playerPosition);
    
    [RpcMethod(DeliveryMode = RpcDeliveryMode.Unreliable)]
    Task<WorldState> GetLocalWorldState();
    
    [RpcMethod(DeliveryMode = RpcDeliveryMode.Reliable)]
    Task<ZoneStats> GetZoneStats();
    
    /// <summary>
    /// Efficiently transfer a bullet trajectory to this zone.
    /// The bullet will be spawned at the calculated position based on current time.
    /// </summary>
    [RpcMethod(DeliveryMode = RpcDeliveryMode.Reliable)]
    [OneWay]
    Task TransferBulletTrajectory(string bulletId, int subType, Vector2 origin, Vector2 velocity, float spawnTime, float lifespan, string? ownerId);
    
    /// <summary>
    /// Subscribe to game updates via observer pattern.
    /// </summary>
    [RpcMethod(DeliveryMode = RpcDeliveryMode.Reliable)]
    Task Subscribe(IGameRpcObserver observer);
    
    /// <summary>
    /// Unsubscribe from game updates.
    /// </summary>
    [RpcMethod(DeliveryMode = RpcDeliveryMode.Reliable)]
    Task Unsubscribe(IGameRpcObserver observer);
    
    /// <summary>
    /// Stream world state updates continuously.
    /// </summary>
    [RpcMethod(DeliveryMode = RpcDeliveryMode.Unreliable)]
    IAsyncEnumerable<WorldState> StreamWorldStateUpdates(CancellationToken cancellationToken);
    
    /// <summary>
    /// Stream zone statistics updates.
    /// </summary>
    [RpcMethod(DeliveryMode = RpcDeliveryMode.Reliable)]
    IAsyncEnumerable<ZoneStatistics> StreamZoneStatistics(CancellationToken cancellationToken);
    
    /// <summary>
    /// Stream entities in adjacent zones.
    /// </summary>
    [RpcMethod(DeliveryMode = RpcDeliveryMode.Unreliable)]
    IAsyncEnumerable<AdjacentZoneEntities> StreamAdjacentZoneEntities(string playerId, CancellationToken cancellationToken);
}

/// <summary>
/// Container for adjacent zone entities to use with IAsyncEnumerable.
/// </summary>
[GenerateSerializer]
public class AdjacentZoneEntities
{
    [Id(0)] public Dictionary<string, List<EntityState>> EntitiesByZone { get; set; } = new();
    [Id(1)] public DateTime Timestamp { get; set; }
}