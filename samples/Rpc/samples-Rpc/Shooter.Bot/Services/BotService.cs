using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shooter.Bot.Telemetry;
using Shooter.Client.Common;
using Shooter.Shared.Models;
using Shooter.Shared.Movement;
using System.Diagnostics;

namespace Shooter.Bot.Services;

/// <summary>
/// Bot service that connects to the game using the same RPC client as the Blazor client.
/// Runs a single bot per process for simplicity.
/// </summary>
public class BotService : BackgroundService
{
    private readonly ILogger<BotService> _logger;
    private readonly IConfiguration _configuration;
    private readonly GranvilleRpcGameClientService _gameClient;
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
        GranvilleRpcGameClientService gameClient)
    {
        _logger = logger;
        _configuration = configuration;
        _gameClient = gameClient;
        
        _testMode = _configuration.GetValue<bool>("TestMode", true);
        
        // Extract bot index from instance ID or configuration
        var instanceId = _configuration.GetValue<string>("ASPIRE_INSTANCE_ID", "0");
        if (!int.TryParse(instanceId, out _botIndex))
        {
            _botIndex = 0;
        }
        
        // Get transport type from configuration
        var transportType = _configuration.GetValue<string>("RpcTransport", "litenetlib");
        var transportName = transportType.ToLowerInvariant() switch
        {
            "ruffles" => "Ruffles",
            "litenetlib" => "LiteNetLib",
            _ => "LiteNetLib"
        };
        
        // Build bot name: [TransportName][Test?][BotId]
        var testIndicator = _testMode ? "Test" : "";
        _botName = $"{transportName}{testIndicator}{_botIndex}";
        
        // Allow override from configuration
        var configuredBotName = _configuration.GetValue<string>("BotName");
        if (!string.IsNullOrEmpty(configuredBotName))
        {
            _botName = configuredBotName;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var transportType = _configuration.GetValue<string>("RpcTransport", "litenetlib");
            _logger.LogInformation("Bot {BotName} starting in {Mode} mode using {Transport} transport", 
                _botName, _testMode ? "test" : "normal", transportType);
            
            // Wait for services to be ready
            _logger.LogInformation("Waiting 5 seconds for services to be ready...");
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            
            // Connect to the game
            _logger.LogInformation("Bot {BotName} connecting to game...", _botName);
            var connected = await _gameClient.ConnectAsync(_botName);
            
            if (!connected)
            {
                _logger.LogError("Bot {BotName} failed to connect to game", _botName);
                return;
            }
            
            _logger.LogInformation("Bot {BotName} connected as player {PlayerId}, checking connection status...", 
                _botName, _gameClient.PlayerId);
            
            // Verify connection
            if (!_gameClient.IsConnected)
            {
                _logger.LogError("Bot {BotName} connection check failed - IsConnected is false", _botName);
                return;
            }
            
            // Create automove controller
            _autoMoveController = new AutoMoveController(_logger, _gameClient.PlayerId ?? _botName, _testMode);
            
            // Subscribe to game events
            _gameClient.WorldStateUpdated += OnWorldStateUpdated;
            _gameClient.AvailableZonesUpdated += OnAvailableZonesUpdated;
            
            // Main bot loop
            var loopCount = 0;
            var reconnectAttempts = 0;
            const int maxReconnectAttempts = 5;
            
            while (!stoppingToken.IsCancellationRequested)
            {
                // Check if we need to reconnect
                if (!_gameClient.IsConnected)
                {
                    if (reconnectAttempts >= maxReconnectAttempts)
                    {
                        _logger.LogError("Bot {BotName} failed to reconnect after {Attempts} attempts, giving up", 
                            _botName, maxReconnectAttempts);
                        break;
                    }
                    
                    _logger.LogWarning("Bot {BotName} lost connection, attempting to reconnect (attempt {Attempt}/{Max})", 
                        _botName, reconnectAttempts + 1, maxReconnectAttempts);
                    
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    
                    // Try to reconnect
                    var reconnected = await _gameClient.ConnectAsync(_botName);
                    if (reconnected)
                    {
                        _logger.LogInformation("Bot {BotName} reconnected successfully", _botName);
                        reconnectAttempts = 0;
                        
                        // Re-subscribe to events
                        _gameClient.WorldStateUpdated -= OnWorldStateUpdated;
                        _gameClient.AvailableZonesUpdated -= OnAvailableZonesUpdated;
                        _gameClient.WorldStateUpdated += OnWorldStateUpdated;
                        _gameClient.AvailableZonesUpdated += OnAvailableZonesUpdated;
                    }
                    else
                    {
                        reconnectAttempts++;
                        continue;
                    }
                }
                
                try
                {
                    await RunAutoMove(stoppingToken);
                    await Task.Delay(100, stoppingToken); // Update rate
                    
                    // Log status every 5 seconds
                    loopCount++;
                    if (loopCount % 50 == 0)
                    {
                        _logger.LogInformation("Bot {BotName} status - Connected: {Connected}, WorldState: {HasWorldState}, Entities: {EntityCount}",
                            _botName, _gameClient.IsConnected, _lastWorldState != null, _lastWorldState?.Entities?.Count ?? 0);
                    }
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
        if (_lastWorldState == null || _autoMoveController == null)
        {
            _logger.LogDebug("Bot {BotName}: Skipping automove - worldState: {HasWorldState}, controller: {HasController}",
                _botName, _lastWorldState != null, _autoMoveController != null);
            return;
        }
        
        var player = _lastWorldState.Entities.FirstOrDefault(e => e.EntityId == _gameClient.PlayerId);
        if (player == null)
        {
            _logger.LogWarning("Bot {BotName}: Player entity not found in world state (PlayerId: {PlayerId})",
                _botName, _gameClient.PlayerId);
            return;
        }
        
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
        _logger.LogDebug("Bot {BotName}: World state updated with {EntityCount} entities (sequence: {Sequence})",
            _botName, worldState?.Entities?.Count ?? 0, worldState?.SequenceNumber ?? -1);
    }

    private void OnAvailableZonesUpdated(List<GridSquare> zones)
    {
        _availableZones = zones;
        _logger.LogDebug("Bot {BotName} found {ZoneCount} available zones", _botName, zones.Count);
    }

    public bool IsConnected => _gameClient?.IsConnected ?? false;

    public BotInfo? GetBotInfo()
    {
        if (!IsConnected || string.IsNullOrEmpty(_gameClient.PlayerId))
        {
            return null;
        }

        return new BotInfo
        {
            BotId = _gameClient.PlayerId,
            BotName = _botName,
            Index = _botIndex
        };
    }
}

public class BotInfo
{
    public string BotId { get; set; } = string.Empty;
    public string BotName { get; set; } = string.Empty;
    public int Index { get; set; }
}