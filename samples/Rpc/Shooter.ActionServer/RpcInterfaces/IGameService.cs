using Shooter.Shared.Models;

namespace Shooter.ActionServer.RpcInterfaces;

// Simple interface for RPC service - not using grain interface
public interface IGameService
{
    Task<bool> ConnectPlayer(string playerId);
    Task DisconnectPlayer(string playerId);
    Task<WorldState> GetWorldState();
    Task UpdatePlayerInput(string playerId, Vector2 moveDirection, bool isShooting);
}