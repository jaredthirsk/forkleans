using Orleans;
using Orleans.Runtime;
using Shooter.Shared.GrainInterfaces;
using Shooter.Shared.Models;

namespace Shooter.Silo.Grains;

public class PlayerGrain : Grain, IPlayerGrain
{
    private readonly IPersistentState<PlayerState> _state;

    public PlayerGrain(
        [PersistentState("player", "playerStore")] IPersistentState<PlayerState> state)
    {
        _state = state;
    }

    public Task<PlayerInfo> GetInfo()
    {
        return Task.FromResult(new PlayerInfo(
            this.GetPrimaryKeyString(),
            _state.State.Name,
            _state.State.Position,
            _state.State.Velocity,
            _state.State.Health));
    }

    public async Task UpdatePosition(Vector2 position, Vector2 velocity)
    {
        _state.State.Position = position;
        _state.State.Velocity = velocity;
        _state.State.LastUpdated = DateTime.UtcNow;
        await _state.WriteStateAsync();
    }

    public Task<GridSquare> GetCurrentGridSquare()
    {
        return Task.FromResult(GridSquare.FromPosition(_state.State.Position));
    }

    public async Task TakeDamage(float damage)
    {
        _state.State.Health = Math.Max(0, _state.State.Health - damage);
        await _state.WriteStateAsync();
    }

    public Task<bool> IsAlive()
    {
        return Task.FromResult(_state.State.Health > 0);
    }
}

[GenerateSerializer]
public class PlayerState
{
    public string Name { get; set; } = "";
    public Vector2 Position { get; set; } = Vector2.Zero;
    public Vector2 Velocity { get; set; } = Vector2.Zero;
    public float Health { get; set; } = 100f;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}