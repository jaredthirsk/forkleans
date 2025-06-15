using Orleans;
using Shooter.Shared.Models;

namespace Shooter.Shared.GrainInterfaces;

public interface IPlayerGrain : Orleans.IGrainWithStringKey
{
    Task<PlayerInfo> GetInfo();
    Task UpdatePosition(Vector2 position, Vector2 velocity);
    Task<GridSquare> GetCurrentGridSquare();
    Task TakeDamage(float damage);
    Task<bool> IsAlive();
}