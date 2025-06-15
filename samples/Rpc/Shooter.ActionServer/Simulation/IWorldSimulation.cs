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
    
    // Zone management
    void SetAssignedSquare(GridSquare square);
    Task<List<string>> GetPlayersOutsideZone();
    Task<List<(string entityId, Vector2 position, EntityType type, int subType)>> GetEntitiesOutsideZone();
    Task<bool> TransferEntityIn(string entityId, EntityType type, int subType, Vector2 position, Vector2 velocity, float health);
    PlayerInfo? GetPlayerInfo(string playerId);
}