using Forkleans;
using Forkleans.Rpc;
using Shooter.Shared.Models;

namespace Shooter.Shared.RpcInterfaces;

/// <summary>
/// Grain interface that can be accessed via Forkleans RPC.
/// Forkleans RPC exposes grain interfaces over UDP/TCP.
/// Uses Forkleans types, not Orleans types, for RPC compatibility.
/// </summary>
public interface IGameRpcGrain : Forkleans.IGrainWithStringKey
{
    [RpcMethod(DeliveryMode = RpcDeliveryMode.Reliable)]
    Task<bool> ConnectPlayer(string playerId);
    
    [RpcMethod(DeliveryMode = RpcDeliveryMode.Reliable)]
    Task DisconnectPlayer(string playerId);
    
    [RpcMethod(DeliveryMode = RpcDeliveryMode.Unreliable)]
    Task<WorldState> GetWorldState();
    
    [RpcMethod(DeliveryMode = RpcDeliveryMode.Unreliable)]
    Task UpdatePlayerInput(string playerId, Vector2 moveDirection, bool isShooting);
    
    [RpcMethod(DeliveryMode = RpcDeliveryMode.ReliableOrdered)]
    Task<bool> TransferEntityIn(string entityId, EntityType type, int subType, Vector2 position, Vector2 velocity, float health);
}