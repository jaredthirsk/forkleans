using Shooter.Shared.Models;

namespace Shooter.ActionServer.Simulation;

public interface IWorldSimulation
{
    Task<bool> AddPlayer(string playerId);
    void RemovePlayer(string playerId);
    void UpdatePlayerInput(string playerId, Vector2 moveDirection, bool isShooting);
    void UpdatePlayerInputEx(string playerId, Vector2? moveDirection, Vector2? shootDirection);
    WorldState GetCurrentState();
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    
    // Zone management
    void SetAssignedSquare(GridSquare square);
    GridSquare GetAssignedSquare();
    Task<List<string>> GetPlayersOutsideZone();
    Task<List<(string entityId, Vector2 position, EntityType type, int subType)>> GetEntitiesOutsideZone();
    Task<bool> TransferEntityIn(string entityId, EntityType type, int subType, Vector2 position, Vector2 velocity, float health);
    PlayerInfo? GetPlayerInfo(string playerId);
    
    // Scout alerts
    void ProcessScoutAlert(GridSquare playerZone, Vector2 playerPosition);
    
    // Bullet trajectories
    void ReceiveBulletTrajectory(string bulletId, int subType, Vector2 origin, Vector2 velocity, float spawnTime, float lifespan, string? ownerId, int team = 0);
    
    // Damage tracking
    ZoneDamageReport GetDamageReport();
    
    // Performance tracking
    double GetServerFps();
    
    // Bullet management
    void RemoveBullet(string bulletId);
}