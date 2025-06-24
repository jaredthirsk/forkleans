using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shooter.Client.Common;
using Shooter.Shared.Models;
using Shooter.Shared.Movement;

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
    private readonly int _botIndex;
    
    private WorldState? _lastWorldState;
    private List<GridSquare> _availableZones = new();
    private AutoMoveController? _autoMoveController;
    private DateTime _lastShootTime = DateTime.UtcNow;

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
        
        // Extract bot index from instance ID or bot name
        var instanceId = _configuration.GetValue<string>("ASPIRE_INSTANCE_ID", "0");
        if (!int.TryParse(instanceId, out _botIndex))
        {
            // Try to extract from bot name
            var parts = _botName.Split('_');
            if (parts.Length > 1 && int.TryParse(parts.Last(), out var index))
            {
                _botIndex = index;
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Bot {BotName} starting in {Mode} mode", _botName, _testMode ? "test" : "normal");
            
            // Wait for services to be ready
            _logger.LogInformation("Waiting 5 seconds for services to be ready...");
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
            
            // Create automove controller
            _autoMoveController = new AutoMoveController(_logger, _gameClient.PlayerId ?? _botName, _testMode);
            
            // Subscribe to game events
            _gameClient.WorldStateUpdated += OnWorldStateUpdated;
            _gameClient.AvailableZonesUpdated += OnAvailableZonesUpdated;
            
            // Main bot loop
            while (!stoppingToken.IsCancellationRequested && _gameClient.IsConnected)
            {
                try
                {
                    await RunAutoMove(stoppingToken);
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

    private async Task RunAutoMove(CancellationToken cancellationToken)
    {
        if (_lastWorldState == null || _autoMoveController == null) return;
        
        var player = _lastWorldState.Entities.FirstOrDefault(e => e.EntityId == _gameClient.PlayerId);
        if (player == null) return;
        
        // Get movement decision from automove controller
        var (moveDirection, shootDirection) = _autoMoveController.Update(
            _lastWorldState,
            _availableZones,
            player.Position);
        
        // Send input to server
        await _gameClient.SendPlayerInputEx(moveDirection, shootDirection);
        
        // Log mode changes
        var currentMode = _autoMoveController.CurrentMode;
        _logger.LogDebug("Bot {BotName} in mode {Mode}, move: {Move}, shoot: {Shoot}",
            _botName, currentMode, moveDirection != null, shootDirection != null);
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