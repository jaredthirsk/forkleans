using Forkleans;
using Shooter.ActionServer.Services;
using Shooter.ActionServer.Simulation;
using Shooter.Shared.Models;
using Shooter.Shared.RpcInterfaces;

namespace Shooter.ActionServer.Grains;

/// <summary>
/// Grain implementation that exposes game functionality via Forkleans RPC.
/// This grain runs in the ActionServer process and has direct access to game services.
/// Note: This uses Forkleans.Grain, not Orleans.Grain, for RPC compatibility.
/// </summary>
public class GameRpcGrain : Forkleans.Grain, IGameRpcGrain
{
    private readonly GameService _gameService;
    private readonly IWorldSimulation _worldSimulation;
    private readonly ILogger<GameRpcGrain> _logger;

    public GameRpcGrain(
        GameService gameService,
        IWorldSimulation worldSimulation,
        ILogger<GameRpcGrain> logger)
    {
        _gameService = gameService;
        _worldSimulation = worldSimulation;
        _logger = logger;
    }

    public async Task<bool> ConnectPlayer(string playerId)
    {
        _logger.LogInformation("RPC: Player {PlayerId} connecting via Forkleans RPC", playerId);
        return await _gameService.ConnectPlayer(playerId);
    }

    public async Task DisconnectPlayer(string playerId)
    {
        _logger.LogInformation("RPC: Player {PlayerId} disconnecting via Forkleans RPC", playerId);
        await _gameService.DisconnectPlayer(playerId);
    }

    public Task<WorldState> GetWorldState()
    {
        var state = _worldSimulation.GetCurrentState();
        return Task.FromResult(state);
    }

    public async Task UpdatePlayerInput(string playerId, Vector2 moveDirection, bool isShooting)
    {
        await _gameService.UpdatePlayerInput(playerId, moveDirection, isShooting);
    }

    public async Task<bool> TransferEntityIn(string entityId, EntityType type, int subType, Vector2 position, Vector2 velocity, float health)
    {
        _logger.LogInformation("RPC: Transferring entity {EntityId} via Forkleans RPC", entityId);
        return await _worldSimulation.TransferEntityIn(entityId, type, subType, position, velocity, health);
    }
}