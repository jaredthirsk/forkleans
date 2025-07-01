using Orleans;
using Orleans.Runtime;
using Microsoft.Extensions.Logging;
using Shooter.Shared.GrainInterfaces;
using Shooter.Shared.Models;
using Shooter.Silo.Telemetry;
using System.Diagnostics;

namespace Shooter.Silo.Grains;

public class PlayerGrain : Orleans.Grain, IPlayerGrain
{
    private readonly Orleans.Runtime.IPersistentState<PlayerState> _state;
    private readonly ILogger<PlayerGrain> _logger;

    public PlayerGrain(
        [Orleans.Runtime.PersistentState("player", "playerStore")] Orleans.Runtime.IPersistentState<PlayerState> state,
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
        using var activity = SiloTelemetry.ActivitySource.StartActivity(SiloTelemetry.PlayerCreationActivity);
        activity?.SetTag("player.id", this.GetPrimaryKeyString());
        activity?.SetTag("player.name", name);
        activity?.SetTag("start.position.x", startPosition.X);
        activity?.SetTag("start.position.y", startPosition.Y);

        var stopwatch = Stopwatch.StartNew();
        
        // Only update fields that should be reset on initialization
        // Preserve health if the player already has less than full health (they were damaged)
        var currentHealth = _state.State.Health;
        var preserveHealth = currentHealth > 0 && currentHealth < 1000f;
        var isNewPlayer = string.IsNullOrEmpty(_state.State.Name);
        
        _state.State.Name = name;
        _state.State.Position = startPosition;
        _state.State.Velocity = Vector2.Zero;
        _state.State.Health = preserveHealth ? currentHealth : 1000f;
        _state.State.LastUpdated = DateTime.UtcNow;
        await _state.WriteStateAsync();
        
        stopwatch.Stop();
        SiloTelemetry.GrainCallDuration.Record(stopwatch.Elapsed.TotalMilliseconds, 
            new("operation", "player_initialize"),
            new("is_new_player", isNewPlayer.ToString()));
            
        if (isNewPlayer)
        {
            SiloTelemetry.PlayersCreated.Add(1, new KeyValuePair<string, object?>("player.name", name));
            SiloTelemetry.ActivePlayers.Add(1);
        }
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

[Orleans.GenerateSerializer]
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