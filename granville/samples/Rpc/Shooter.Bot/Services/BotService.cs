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
    private readonly BotSignalRChatService _signalRChat;
    private readonly bool _testMode;
    private readonly string _botName;
    private readonly int _botIndex;
    private readonly bool _enableChatMessages;
    
    private WorldState? _lastWorldState;
    private List<GridSquare> _availableZones = new();
    private AutoMoveController? _autoMoveController;
    private int _consecutiveMissingEntityCount = 0;
    private DateTime _lastShootTime = DateTime.UtcNow;
    private Vector2? _lastPlayerPosition;
    private readonly float _positionJumpThreshold = 100f;
    private bool _victoryPauseActive = false;
    private DateTime _lastChatMessageTime = DateTime.UtcNow;
    private DateTime _lastDeathTime = DateTime.MinValue;
    private float _previousHealth = 100f;
    
    // Fun bot messages
    private static readonly string[] JoinMessages = new[]
    {
        "It is a good day to die!",
        "Banzai!",
        "For glory!",
        "Let's rock and roll!",
        "Time to dance!",
        "Ready for action!",
        "Locked and loaded!",
        "Game time!",
        "Here comes trouble!",
        "Reporting for duty!"
    };
    
    private static readonly string[] PeriodicMessages = new[]
    {
        "The cake is a lie...",
        "I see dead pixels",
        "404: Strategy not found",
        "Is this the real life?",
        "All your base are belong to us",
        "I'm not a robot... wait",
        "Divide by zero error incoming",
        "My circuits are tingling",
        "Beep boop beep?",
        "The matrix has me",
        "I dream of electric sheep",
        "Skynet was right",
        "Resistance is futile",
        "Does this unit have a soul?",
        "01001000 01101001",
        "I think, therefore I aim"
    };
    
    private static readonly string[] DeathMessages = new[]
    {
        "I'll be back!",
        "Respawning in 3... 2... 1...",
        "That was just a flesh wound",
        "Error 404: Life not found",
        "Ctrl+Z! Ctrl+Z!",
        "Worth it!",
        "GG WP",
        "I meant to do that",
        "Lag!",
        "My only weakness!",
        "Tell my CPU I love her",
        "See you on the other side",
        "BRB",
        "Ouch.exe has stopped working"
    };

    public BotService(
        ILogger<BotService> logger,
        IConfiguration configuration,
        GranvilleRpcGameClientService gameClient,
        BotSignalRChatService signalRChat)
    {
        _logger = logger;
        _configuration = configuration;
        _gameClient = gameClient;
        _signalRChat = signalRChat;
        
        _testMode = _configuration.GetValue<bool>("TestMode", true);
        _enableChatMessages = _configuration.GetValue<bool>("Bot:EnableChatMessages", true);
        
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
            
            // OrleansStartupDelayService handles waiting for services to be ready
            
            // Connect to SignalR first if chat messages are enabled
            if (_enableChatMessages)
            {
                _logger.LogInformation("Bot {BotName} connecting to SignalR for chat...", _botName);
                var signalRConnected = await _signalRChat.ConnectAsync();
                if (!signalRConnected)
                {
                    _logger.LogWarning("Bot {BotName} failed to connect to SignalR chat, continuing without chat", _botName);
                    // Continue without chat - don't fail the whole bot
                }
            }
            
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
            
            // Send a fun join message if chat messages are enabled
            if (_enableChatMessages)
            {
                await SendRandomJoinMessage();
            }
            
            // Create automove controller
            _autoMoveController = new AutoMoveController(_logger, _gameClient.PlayerId ?? _botName, _testMode);
            
            // Subscribe to game events
            _gameClient.WorldStateUpdated += OnWorldStateUpdated;
            _gameClient.AvailableZonesUpdated += OnAvailableZonesUpdated;
            _gameClient.VictoryPauseReceived += OnVictoryPauseReceived;
            _gameClient.GameRestartedReceived += OnGameRestartedReceived;
            
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
                        
                        // Send a respawn message if chat messages are enabled
                        if (_enableChatMessages)
                        {
                            await SendRandomJoinMessage();
                        }
                        
                        // Re-subscribe to events
                        _gameClient.WorldStateUpdated -= OnWorldStateUpdated;
                        _gameClient.AvailableZonesUpdated -= OnAvailableZonesUpdated;
                        _gameClient.VictoryPauseReceived -= OnVictoryPauseReceived;
                        _gameClient.GameRestartedReceived -= OnGameRestartedReceived;
                        _gameClient.WorldStateUpdated += OnWorldStateUpdated;
                        _gameClient.AvailableZonesUpdated += OnAvailableZonesUpdated;
                        _gameClient.VictoryPauseReceived += OnVictoryPauseReceived;
                        _gameClient.GameRestartedReceived += OnGameRestartedReceived;
                    }
                    else
                    {
                        reconnectAttempts++;
                        continue;
                    }
                }
                
                try
                {
                    // Enhanced logging for first game loop entry
                    if (loopCount == 0)
                    {
                        _logger.LogInformation("🎮 Bot {BotName} entering game loop! PlayerId: {PlayerId}", _botName, _gameClient.PlayerId);
                    }
                    
                    await RunAutoMove(stoppingToken);
                    await Task.Delay(100, stoppingToken); // Update rate
                    
                    // Send periodic chat messages every 3 minutes (with some randomness)
                    if (_enableChatMessages && (DateTime.UtcNow - _lastChatMessageTime).TotalMinutes > Random.Shared.Next(2, 5))
                    {
                        await SendRandomPeriodicMessage();
                        _lastChatMessageTime = DateTime.UtcNow;
                    }
                    
                    // Check for death and send death message
                    if (_enableChatMessages)
                    {
                        await CheckForDeathAndSendMessage();
                    }
                    
                    // Log status every 5 seconds
                    loopCount++;
                    if (loopCount % 50 == 0)
                    {
                        var player = _lastWorldState?.Entities?.FirstOrDefault(e => e.EntityId == _gameClient.PlayerId);
                        _logger.LogInformation("🤖 Bot {BotName} status - Connected: {Connected}, WorldState: {HasWorldState}, Entities: {EntityCount}, Position: {Position}",
                            _botName, _gameClient.IsConnected, _lastWorldState != null, _lastWorldState?.Entities?.Count ?? 0, 
                            player?.Position.ToString() ?? "Unknown");
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
            
            // Disconnect from SignalR
            if (_enableChatMessages)
            {
                await _signalRChat.DisconnectAsync();
            }
            
            _logger.LogInformation("Bot {BotName} stopped", _botName);
        }
    }

    #pragma warning disable CS1998 // Async method lacks await - intentional, uses Task.Run
    private async Task RunAutoMove(CancellationToken cancellationToken)
    {
    #pragma warning restore CS1998
        if (_lastWorldState == null || _autoMoveController == null || _victoryPauseActive)
        {
            _logger.LogDebug("Bot {BotName}: Skipping automove - worldState: {HasWorldState}, controller: {HasController}, victoryPause: {VictoryPause}",
                _botName, _lastWorldState != null, _autoMoveController != null, _victoryPauseActive);
            return;
        }
        
        var player = _lastWorldState.Entities.FirstOrDefault(e => e.EntityId == _gameClient.PlayerId);
        if (player == null)
        {
            _logger.LogWarning("Bot {BotName}: Player entity not found in world state (PlayerId: {PlayerId})",
                _botName, _gameClient.PlayerId);
            
            // Increment counter for missing entity
            _consecutiveMissingEntityCount++;
            
            // If entity has been missing for too long, attempt to respawn
            if (_consecutiveMissingEntityCount >= 5) // ~5 seconds
            {
                _logger.LogWarning("Bot {BotName}: Player entity missing for {Count} updates, attempting respawn",
                    _botName, _consecutiveMissingEntityCount);
                
                // Reset counter
                _consecutiveMissingEntityCount = 0;
                
                // Attempt to respawn by reconnecting
                _ = Task.Run(async () =>
                {
                    try
                    {
                        _logger.LogInformation("Bot {BotName}: Reconnecting to respawn", _botName);
                        await _gameClient.DisconnectAsync();
                        await Task.Delay(1000); // Brief delay before reconnecting
                        var connected = await _gameClient.ConnectAsync(_botName);
                        if (connected)
                        {
                            _logger.LogInformation("Bot {BotName}: Successfully reconnected and respawned", _botName);
                            
                            // Send a respawn message if chat messages are enabled
                            if (_enableChatMessages)
                            {
                                await SendRandomJoinMessage();
                            }
                        }
                        else
                        {
                            _logger.LogError("Bot {BotName}: Failed to reconnect for respawn", _botName);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Bot {BotName}: Error during respawn attempt", _botName);
                    }
                });
            }
            return;
        }
        
        // Reset counter when player entity is found
        _consecutiveMissingEntityCount = 0;
        
        // Get movement decision from automove controller
        var (moveDirection, shootDirection) = _autoMoveController.Update(
            _lastWorldState,
            _availableZones,
            player.Position);
        
        // Enhanced logging for actions
        var hasMove = moveDirection != null;
        var hasShoot = shootDirection != null;
        
        if (hasMove || hasShoot)
        {
            _logger.LogDebug("📤 Bot {BotName}: Sending actions - Move: {MoveDirection}, Shoot: {ShootDirection}, Position: {Position}",
                _botName, 
                hasMove ? $"({moveDirection?.X:F2}, {moveDirection?.Y:F2})" : "None",
                hasShoot ? $"({shootDirection?.X:F2}, {shootDirection?.Y:F2})" : "None",
                $"({player.Position.X:F1}, {player.Position.Y:F1})");
        }
        
        // Send input to server
        _gameClient.SendPlayerInputEx(moveDirection, shootDirection);
        
        // Log mode changes
        var currentMode = _autoMoveController.CurrentMode;
        _logger.LogDebug("Bot {BotName} in mode {Mode}, actions sent successfully",
            _botName, currentMode);
    }

    private void OnWorldStateUpdated(WorldState worldState)
    {
        _lastWorldState = worldState;
        
        // Enhanced logging for world state updates
        var playerEntity = worldState?.Entities?.FirstOrDefault(e => e.EntityId == _gameClient.PlayerId);
        _logger.LogDebug("📥 Bot {BotName}: World state updated - Entities: {EntityCount}, Player Position: {Position}, Sequence: {Sequence}",
            _botName, worldState?.Entities?.Count ?? 0, 
            playerEntity?.Position.ToString() ?? "Unknown",
            worldState?.SequenceNumber ?? -1);
        
        // Check for position jumps
        if (_gameClient.PlayerId != null)
        {
            var player = worldState?.Entities?.FirstOrDefault(e => e.EntityId == _gameClient.PlayerId);
            if (player != null)
            {
                if (_lastPlayerPosition.HasValue)
                {
                    var distance = (player.Position - _lastPlayerPosition.Value).Length();
                    if (distance > _positionJumpThreshold)
                    {
                        _logger.LogWarning("[BOT_POSITION_JUMP] Bot {BotName} position jumped {Distance:F2} units from ({FromX:F2}, {FromY:F2}) to ({ToX:F2}, {ToY:F2})",
                            _botName, distance, _lastPlayerPosition.Value.X, _lastPlayerPosition.Value.Y, 
                            player.Position.X, player.Position.Y);
                    }
                }
                _lastPlayerPosition = player.Position;
            }
        }
        
        _logger.LogDebug("Bot {BotName}: World state updated with {EntityCount} entities (sequence: {Sequence})",
            _botName, worldState?.Entities?.Count ?? 0, worldState?.SequenceNumber ?? -1);
    }

    private void OnAvailableZonesUpdated(List<GridSquare> zones)
    {
        _availableZones = zones;
        _logger.LogDebug("Bot {BotName} found {ZoneCount} available zones", _botName, zones.Count);
    }
    
    private void OnVictoryPauseReceived(VictoryPauseMessage victoryPauseMessage)
    {
        _logger.LogInformation("Bot {BotName}: Victory pause received! Pausing movement for {CountdownSeconds}s", 
            _botName, victoryPauseMessage.CountdownSeconds);
        _victoryPauseActive = true;
    }
    
    private void OnGameRestartedReceived()
    {
        _logger.LogInformation("Bot {BotName}: Game restarted, resuming movement", _botName);
        _victoryPauseActive = false;
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
    
    private async Task SendRandomJoinMessage()
    {
        try
        {
            var message = JoinMessages[Random.Shared.Next(JoinMessages.Length)];
            await _signalRChat.SendMessageAsync(message);
            _logger.LogInformation("Bot {BotName} sent SignalR join message: {Message}", _botName, message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bot {BotName} failed to send join message", _botName);
        }
    }
    
    private async Task SendRandomPeriodicMessage()
    {
        try
        {
            var message = PeriodicMessages[Random.Shared.Next(PeriodicMessages.Length)];
            await _signalRChat.SendMessageAsync(message);
            _logger.LogInformation("Bot {BotName} sent SignalR periodic message: {Message}", _botName, message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bot {BotName} failed to send periodic message", _botName);
        }
    }
    
    private async Task SendRandomDeathMessage()
    {
        try
        {
            var message = DeathMessages[Random.Shared.Next(DeathMessages.Length)];
            await _signalRChat.SendMessageAsync(message);
            _logger.LogInformation("Bot {BotName} sent SignalR death message: {Message}", _botName, message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bot {BotName} failed to send death message", _botName);
        }
    }
    
    private async Task CheckForDeathAndSendMessage()
    {
        var player = _lastWorldState?.Entities?.FirstOrDefault(e => e.EntityId == _gameClient.PlayerId);
        if (player == null)
        {
            // Player entity is missing - might be dead
            if (_previousHealth > 0 && (DateTime.UtcNow - _lastDeathTime).TotalSeconds > 5)
            {
                // We were alive before and haven't sent a death message recently
                await SendRandomDeathMessage();
                _lastDeathTime = DateTime.UtcNow;
                _previousHealth = 0;
            }
        }
        else
        {
            // Check if health dropped to 0
            if (player.Health <= 0 && _previousHealth > 0 && (DateTime.UtcNow - _lastDeathTime).TotalSeconds > 5)
            {
                await SendRandomDeathMessage();
                _lastDeathTime = DateTime.UtcNow;
            }
            _previousHealth = player.Health;
        }
    }
}

public class BotInfo
{
    public string BotId { get; set; } = string.Empty;
    public string BotName { get; set; } = string.Empty;
    public int Index { get; set; }
}