using Forkleans;
using Shooter.Shared.Models;

namespace Shooter.Shared.GrainInterfaces;

public interface IPlayerGrain : Forkleans.IGrainWithStringKey
{
    Task Initialize(string name, Vector2 startPosition);
    Task<PlayerInfo> GetInfo();
    Task UpdatePosition(Vector2 position, Vector2 velocity);
    Task<GridSquare> GetCurrentGridSquare();
    Task TakeDamage(float damage);
    Task<bool> IsAlive();
    Task UpdateHealth(float health);
    Task NotifyGameOver(GameOverMessage gameOverMessage);
    Task NotifyGameRestarted();
}