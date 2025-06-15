using Shooter.ActionServer.RpcInterfaces;
using Shooter.ActionServer.Simulation;
using Shooter.Shared.Models;

namespace Shooter.ActionServer.Services;

public class GameService : IGameService
{
    private readonly IWorldSimulation _simulation;
    private readonly Orleans.IClusterClient _orleansClient;

    public GameService(IWorldSimulation simulation, Orleans.IClusterClient orleansClient)
    {
        _simulation = simulation;
        _orleansClient = orleansClient;
    }

    public Task<bool> ConnectPlayer(string playerId)
    {
        return _simulation.AddPlayer(playerId);
    }

    public Task DisconnectPlayer(string playerId)
    {
        _simulation.RemovePlayer(playerId);
        return Task.CompletedTask;
    }

    public Task<WorldState> GetWorldState()
    {
        return Task.FromResult(_simulation.GetCurrentState());
    }

    public Task UpdatePlayerInput(string playerId, Vector2 moveDirection, bool isShooting)
    {
        _simulation.UpdatePlayerInput(playerId, moveDirection, isShooting);
        return Task.CompletedTask;
    }
}