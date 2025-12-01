using Granville.Rpc.Security;
using Orleans;
using Shooter.Shared.Models;

namespace Shooter.Shared.GrainInterfaces;

/// <summary>
/// Player grain for managing individual player state.
/// Accessible by both clients (players) and servers.
/// </summary>
[ClientAccessible]
[Authorize]
public interface IPlayerGrain : Orleans.IGrainWithStringKey
{
    Task Initialize(string name, Vector2 startPosition);
    Task<PlayerInfo> GetInfo();
    Task UpdatePosition(Vector2 position, Vector2 velocity);
    Task<GridSquare> GetCurrentGridSquare();

    /// <summary>
    /// Apply damage to this player. Server-only operation.
    /// </summary>
    [ServerOnly]
    Task TakeDamage(float damage);

    Task<bool> IsAlive();

    /// <summary>
    /// Update player health. Server-only operation.
    /// </summary>
    [ServerOnly]
    Task UpdateHealth(float health);

    /// <summary>
    /// Notify player of game over. Server-only operation.
    /// </summary>
    [ServerOnly]
    Task NotifyGameOver(GameOverMessage gameOverMessage);

    /// <summary>
    /// Notify player of game restart. Server-only operation.
    /// </summary>
    [ServerOnly]
    Task NotifyGameRestarted();
}