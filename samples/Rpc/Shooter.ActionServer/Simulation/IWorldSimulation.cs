using Shooter.Shared.Models;

namespace Shooter.ActionServer.Simulation;

public interface IWorldSimulation
{
    Task<bool> AddPlayer(string playerId);
    void RemovePlayer(string playerId);
    void UpdatePlayerInput(string playerId, Vector2 moveDirection, bool isShooting);
    WorldState GetCurrentState();
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}