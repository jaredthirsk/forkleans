using Forkleans;
using Forkleans.Runtime;
using Microsoft.Extensions.Logging;
using Shooter.Shared.GrainInterfaces;
using Shooter.Shared.Models;

namespace Shooter.Silo.Grains;

public class PlayerGrain : Forkleans.Grain, IPlayerGrain
{
    private readonly IPersistentState<PlayerState> _state;
    private readonly ILogger<PlayerGrain> _logger;

    public PlayerGrain(
        [PersistentState("player", "playerStore")] IPersistentState<PlayerState> state,
        ILogger<PlayerGrain> logger)
    {
        _state = state;
        _logger = logger;
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

    public async Task Initialize(string name, Vector2 startPosition)
    {
        // Only update fields that should be reset on initialization
        // Preserve health if the player already has less than full health (they were damaged)
        var currentHealth = _state.State.Health;
        var preserveHealth = currentHealth > 0 && currentHealth < 1000f;
        
        _state.State.Name = name;
        _state.State.Position = startPosition;
        _state.State.Velocity = Vector2.Zero;
        _state.State.Health = preserveHealth ? currentHealth : 1000f;
        _state.State.LastUpdated = DateTime.UtcNow;
        await _state.WriteStateAsync();
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
    
    public async Task UpdateHealth(float health)
    {
        _state.State.Health = Math.Max(0, health);
        await _state.WriteStateAsync();
    }
    
    public Task NotifyGameOver(GameOverMessage gameOverMessage)
    {
        // For now, just log the game over message
        // In a real implementation, this would notify the connected client
        _logger.LogInformation("Player {PlayerId} notified of game over. Scores: {Scores}", 
            this.GetPrimaryKeyString(), 
            string.Join(", ", gameOverMessage.PlayerScores.Select(s => $"{s.PlayerName}: {s.RespawnCount} respawns")));
        
        // Store the game over message in state if needed
        _state.State.LastGameOverMessage = gameOverMessage;
        _state.State.GamePhase = GamePhase.GameOver;
        return _state.WriteStateAsync();
    }
    
    public Task NotifyGameRestarted()
    {
        _logger.LogInformation("Player {PlayerId} notified of game restart", this.GetPrimaryKeyString());
        
        // Reset game phase
        _state.State.GamePhase = GamePhase.Playing;
        _state.State.LastGameOverMessage = null;
        return _state.WriteStateAsync();
    }
}

[Forkleans.GenerateSerializer]
public class PlayerState
{
    public string Name { get; set; } = "";
    public Vector2 Position { get; set; } = Vector2.Zero;
    public Vector2 Velocity { get; set; } = Vector2.Zero;
    public float Health { get; set; } = 1000f;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public GamePhase GamePhase { get; set; } = GamePhase.Playing;
    public GameOverMessage? LastGameOverMessage { get; set; }
}