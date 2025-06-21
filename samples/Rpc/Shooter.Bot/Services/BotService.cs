using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shooter.Client.Common;
using Shooter.Shared.Models;

namespace Shooter.Bot.Services;

/// <summary>
/// Bot service that connects to the game using the same RPC client as the Blazor client.
/// Runs a single bot per process for simplicity.
/// </summary>
public class BotService : BackgroundService
{
    private readonly ILogger<BotService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ForkleansRpcGameClientService _gameClient;
    private readonly bool _testMode;
    private readonly string _botName;
    
    private WorldState? _lastWorldState;
    private List<GridSquare> _availableZones = new();
    private int _currentZoneIndex = 0;
    private DateTime _lastShootTime = DateTime.UtcNow;
    private Vector2 _currentMoveDirection = Vector2.Zero;

    public BotService(
        ILogger<BotService> logger,
        IConfiguration configuration,
        ForkleansRpcGameClientService gameClient)
    {
        _logger = logger;
        _configuration = configuration;
        _gameClient = gameClient;
        
        _testMode = _configuration.GetValue<bool>("TestMode", true);
        _botName = _configuration.GetValue<string>("BotName", $"Bot_{Guid.NewGuid():N}".Substring(0, 12));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Bot {BotName} starting in {Mode} mode", _botName, _testMode ? "test" : "normal");
            
            // Wait for services to be ready
            _logger.LogInformation("Waiting 10 seconds for services to be ready...");
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            
            // Connect to the game
            _logger.LogInformation("Connecting to game...");
            var connected = await _gameClient.ConnectAsync(_botName);
            
            if (!connected)
            {
                _logger.LogError("Failed to connect to game");
                return;
            }
            
            _logger.LogInformation("Bot {BotName} connected as player {PlayerId}", _botName, _gameClient.PlayerId);
            
            // Subscribe to game events
            _gameClient.WorldStateUpdated += OnWorldStateUpdated;
            _gameClient.AvailableZonesUpdated += OnAvailableZonesUpdated;
            
            // Main bot loop
            while (!stoppingToken.IsCancellationRequested && _gameClient.IsConnected)
            {
                try
                {
                    if (_testMode)
                    {
                        await RunTestMode(stoppingToken);
                    }
                    else
                    {
                        await RunNormalMode(stoppingToken);
                    }
                    
                    await Task.Delay(100, stoppingToken); // Update rate
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in bot loop");
                    await Task.Delay(1000, stoppingToken); // Back off on error
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Bot {BotName} cancelled", _botName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bot {BotName} crashed", _botName);
        }
        finally
        {
            // Disconnect cleanly
            if (_gameClient.IsConnected)
            {
                await _gameClient.DisconnectAsync();
            }
        }
    }

    private async Task RunTestMode(CancellationToken cancellationToken)
    {
        // Test mode: Methodically visit every zone
        if (_availableZones.Count == 0) return;
        
        var player = _lastWorldState?.Entities.FirstOrDefault(e => e.EntityId == _gameClient.PlayerId);
        if (player == null) return;
        
        // Get target zone
        var targetZone = _availableZones[_currentZoneIndex % _availableZones.Count];
        var targetPos = targetZone.GetCenter();
        var distance = player.Position.DistanceTo(targetPos);
        
        if (distance < 100f) // Close enough to zone center
        {
            // Move to next zone
            _currentZoneIndex++;
            _logger.LogInformation("Bot {BotName} reached zone {Zone}, moving to next", _botName, targetZone);
        }
        else
        {
            // Move towards target zone
            var direction = (targetPos - player.Position).Normalized();
            await _gameClient.SendPlayerInputEx(direction * 100f, null);
        }
        
        // Shoot occasionally in test mode (once per 2 seconds)
        if ((DateTime.UtcNow - _lastShootTime).TotalSeconds >= 2.0)
        {
            await _gameClient.SendPlayerInputEx(null, new Vector2(1, 0)); // Shoot right
            await Task.Delay(200, cancellationToken);
            await _gameClient.SendPlayerInputEx(null, null); // Stop shooting
            _lastShootTime = DateTime.UtcNow;
        }
    }

    private async Task RunNormalMode(CancellationToken cancellationToken)
    {
        // Normal mode: Act like a human player
        if (_lastWorldState == null) return;
        
        var player = _lastWorldState.Entities.FirstOrDefault(e => e.EntityId == _gameClient.PlayerId);
        if (player == null) return;
        
        // Find nearest enemy or asteroid
        var targets = _lastWorldState.Entities
            .Where(e => (e.Type == EntityType.Enemy || e.Type == EntityType.Asteroid) && e.Health > 0)
            .OrderBy(e => player.Position.DistanceTo(e.Position))
            .ToList();
        
        if (targets.Any())
        {
            var target = targets.First();
            var distance = player.Position.DistanceTo(target.Position);
            var direction = (target.Position - player.Position).Normalized();
            
            // Combat behavior
            Vector2? moveDir = null;
            Vector2? shootDir = null;
            
            if (distance > 200f)
            {
                // Move closer
                moveDir = direction * 100f;
            }
            else if (distance < 100f)
            {
                // Too close, back away
                moveDir = direction * -100f;
            }
            else
            {
                // Good distance, strafe
                var strafeDir = new Vector2(-direction.Y, direction.X);
                moveDir = strafeDir * 100f;
            }
            
            // Shoot at target if in range
            if (distance < 300f)
            {
                shootDir = direction;
            }
            
            await _gameClient.SendPlayerInputEx(moveDir, shootDir);
        }
        else
        {
            // No targets, explore
            var randomAngle = Random.Shared.NextSingle() * MathF.PI * 2;
            var randomDir = new Vector2(MathF.Cos(randomAngle), MathF.Sin(randomAngle));
            await _gameClient.SendPlayerInputEx(randomDir * 100f, null);
        }
    }

    private void OnWorldStateUpdated(WorldState worldState)
    {
        _lastWorldState = worldState;
    }

    private void OnAvailableZonesUpdated(List<GridSquare> zones)
    {
        _availableZones = zones;
        _logger.LogDebug("Bot {BotName} found {ZoneCount} available zones", _botName, zones.Count);
    }
}