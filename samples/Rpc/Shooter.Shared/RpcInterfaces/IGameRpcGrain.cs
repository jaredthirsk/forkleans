using Forkleans;
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
/// </summary>
public interface IGameRpcGrain : Forkleans.IGrainWithStringKey
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
    Task ReceiveScoutAlert(GridSquare playerZone, Vector2 playerPosition);
    
    [RpcMethod(DeliveryMode = RpcDeliveryMode.Unreliable)]
    Task<WorldState> GetLocalWorldState();
    
    [RpcMethod(DeliveryMode = RpcDeliveryMode.Reliable)]
    Task<ZoneStats> GetZoneStats();
}