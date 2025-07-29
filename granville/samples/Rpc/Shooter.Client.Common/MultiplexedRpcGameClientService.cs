using Orleans;
using Orleans.Metadata;
using Orleans.Runtime;
using Granville.Rpc;
using Granville.Rpc.Multiplexing;
using Granville.Rpc.Multiplexing.Strategies;
using Granville.Rpc.Transport.LiteNetLib;
using Granville.Rpc.Transport.Ruffles;
using Orleans.Serialization;
using Shooter.Shared.Models;
using Shooter.Shared.RpcInterfaces;
using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Threading;
using System.Collections.Concurrent;
using Granville.Rpc.Hosting;

namespace Shooter.Client.Common;

/// <summary>
/// Game client service that uses RPC multiplexer for managing multiple ActionServer connections.
/// This provides seamless zone transitions without connection drops.
/// </summary>
public class MultiplexedRpcGameClientService : IDisposable
{
    private readonly ILogger<MultiplexedRpcGameClientService> _logger;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private IRpcClientMultiplexer? _rpcMultiplexer;
    private IGameRpcGrain? _gameGrain;
    private Timer? _worldStateTimer;
    private Timer? _heartbeatTimer;
    private Timer? _availableZonesTimer;
    private Timer? _chatPollingTimer;
    private Timer? _networkStatsTimer;
    private Timer? _serverDiscoveryTimer;
    private DateTime _lastChatPollTime = DateTime.UtcNow;
    private DateTime _lastWorldStatePollTime = DateTime.UtcNow;
    private int _worldStatePollFailures = 0;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isTransitioning = false;
    private DateTime _lastZoneBoundaryCheck = DateTime.MinValue;
    private DateTime _lastZoneChangeTime = DateTime.MinValue;
    private GridSquare? _lastDetectedZone = null;
    private readonly object _transitionLock = new object();
    private int _transitionAttempts = 0;
    private DateTime _lastTransitionAttempt = DateTime.MinValue;
    private string? _currentTransitionTarget = null;
    private long _lastSequenceNumber = -1;
    private GridSquare? _currentZone = null;
    private WorldState? _lastWorldState = null;
    private Vector2 _lastInputDirection = Vector2.Zero;
    private bool _lastInputShooting = false;
    private readonly HashSet<string> _visitedZones = new();
    private bool _worldStateErrorLogged = false;
    private DateTime _lastWorldStateError = DateTime.MinValue;
    private bool _playerInputErrorLogged = false;
    private DateTime _lastPlayerInputError = DateTime.MinValue;
    private GameRpcObserver? _observer;
    private List<GridSquare> _cachedAvailableZones = new();
    private NetworkStatistics? _latestNetworkStats = null;
    private readonly Dictionary<string, ActionServerInfo> _discoveredServers = new();
    private ShooterRoutingStrategy? _routingStrategy;
    
    public event Action<WorldState>? WorldStateUpdated;
    public event Action<string>? ServerChanged;
    public event Action<List<GridSquare>>? AvailableZonesUpdated;
    public event Action<ZoneStatistics>? ZoneStatsUpdated;
    public event Action<ScoutAlert>? ScoutAlertReceived;
    public event Action<GameOverMessage>? GameOverReceived;
    public event Action? GameRestartedReceived;
    public event Action<ChatMessage>? ChatMessageReceived;
    public event Action<NetworkStatistics>? NetworkStatsUpdated;
    
    public bool IsConnected { get; private set; }
    public bool IsTransitioning => _isTransitioning;
    public string? PlayerId { get; private set; }
    public string? PlayerName { get; private set; }
    public string? CurrentServerId { get; private set; }
    public string? TransportType { get; private set; }
    
    public MultiplexedRpcGameClientService(
        ILogger<MultiplexedRpcGameClientService> logger,
        HttpClient httpClient,
        IConfiguration configuration,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _httpClient = httpClient;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
    }
    
    public async Task<bool> ConnectAsync(string playerName)
    {
        try
        {
            PlayerName = playerName;
            _cancellationTokenSource = new CancellationTokenSource();
            
            // Get service catalog from Silo
            _logger.LogInformation("Getting available action servers from Silo");
            var actionServersResponse = await _httpClient.GetAsync($"{_configuration["SiloApiUrl"]}/api/game/actionservers");
            if (!actionServersResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to get action servers from Silo. Status: {Status}", actionServersResponse.StatusCode);
                return false;
            }
            
            var actionServers = await actionServersResponse.Content.ReadFromJsonAsync<List<ActionServerInfo>>();
            if (actionServers == null || actionServers.Count == 0)
            {
                _logger.LogError("No action servers available");
                return false;
            }
            
            _logger.LogInformation("Found {Count} action servers", actionServers.Count);
            
            // Create services for multiplexer
            var services = new ServiceCollection();
            services.AddLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Debug);
                logging.AddConsole();
                logging.AddDebug();
            });
            
            // Configure RPC client services needed by multiplexer
            services.AddSerializer(serializer =>
            {
                serializer.AddAssembly(typeof(IGameRpcGrain).Assembly);
                serializer.AddAssembly(typeof(Granville.Rpc.Protocol.RpcMessage).Assembly);
                serializer.AddAssembly(typeof(PlayerInfo).Assembly);
            });
            
            // Add RPC client multiplexer with shooter-specific routing
            services.AddRpcClientMultiplexerWithBuilder(options =>
            {
                options.EagerConnect = false; // Connect on demand
                options.EnableHealthChecks = true;
                options.HealthCheckInterval = TimeSpan.FromSeconds(30);
                options.ConnectionTimeout = TimeSpan.FromSeconds(10);
                options.MaxConnectionRetries = 3;
            })
            .UseCompositeRouting(composite =>
            {
                // Create Shooter-specific routing strategy
                _routingStrategy = new ShooterRoutingStrategy(_logger);
                
                // Add zone-based routing for IGameRpcGrain
                var zoneStrategy = new ZoneBasedRoutingStrategy(_logger);
                composite.AddStrategy(
                    type => typeof(IZoneAwareGrain).IsAssignableFrom(type),
                    zoneStrategy);
                
                // Set default to primary server
                composite.SetDefaultStrategy(_routingStrategy);
            });
            
            var provider = services.BuildServiceProvider();
            _rpcMultiplexer = provider.GetRequiredService<IRpcClientMultiplexer>();
            
            // Register all discovered servers
            foreach (var server in actionServers)
            {
                var serverDescriptor = new ServerDescriptor
                {
                    ServerId = server.ServerId,
                    HostName = server.Host,
                    Port = server.RpcPort,
                    IsPrimary = server.IsPrimary,
                    Metadata = new Dictionary<string, string>
                    {
                        ["zone"] = server.Zone,
                        ["httpPort"] = server.HttpPort.ToString()
                    }
                };
                
                _rpcMultiplexer.RegisterServer(serverDescriptor);
                _discoveredServers[server.ServerId] = server;
                
                // Update routing strategy with zone mapping
                if (_routingStrategy != null && !string.IsNullOrEmpty(server.Zone))
                {
                    _routingStrategy.MapZoneToServer(server.Zone, server.ServerId);
                }
            }
            
            _logger.LogInformation("Registered {Count} servers with multiplexer", actionServers.Count);
            
            // Find primary server for initial connection
            var primaryServer = actionServers.FirstOrDefault(s => s.IsPrimary);
            if (primaryServer == null)
            {
                _logger.LogWarning("No primary server found, using first available");
                primaryServer = actionServers[0];
            }
            
            CurrentServerId = primaryServer.ServerId;
            
            // Get initial game grain from multiplexer
            try
            {
                _logger.LogInformation("Getting initial game grain for primary server {ServerId}", primaryServer.ServerId);
                _gameGrain = await _rpcMultiplexer.GetGrainAsync<IGameRpcGrain>("game");
                
                if (_gameGrain == null)
                {
                    _logger.LogError("Failed to get game grain from multiplexer");
                    return false;
                }
                
                _logger.LogInformation("Successfully obtained game grain from multiplexer");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get initial game grain");
                return false;
            }
            
            // Connect player
            var playerConnectResponse = await _httpClient.PostAsJsonAsync(
                $"{_configuration["SiloApiUrl"]}/api/game/connect",
                new { playerName = playerName });
            
            if (!playerConnectResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to connect player. Status: {Status}", playerConnectResponse.StatusCode);
                return false;
            }
            
            var playerInfo = await playerConnectResponse.Content.ReadFromJsonAsync<PlayerInfo>();
            if (playerInfo == null)
            {
                _logger.LogError("Failed to get player info");
                return false;
            }
            
            PlayerId = playerInfo.Id;
            _logger.LogInformation("Connected as player {PlayerId} with name {PlayerName}", PlayerId, PlayerName);
            
            // Set up timers
            _worldStateTimer = new Timer(PollWorldState, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(50));
            _heartbeatTimer = new Timer(SendHeartbeat, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
            _availableZonesTimer = new Timer(PollAvailableZones, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));
            _chatPollingTimer = new Timer(PollChatMessages, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            _networkStatsTimer = new Timer(PollNetworkStats, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
            _serverDiscoveryTimer = new Timer(DiscoverServers, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));
            
            // Create observer for push notifications
            _observer = new GameRpcObserver(
                onScoutAlert: alert => ScoutAlertReceived?.Invoke(alert),
                onGameOver: msg => GameOverReceived?.Invoke(msg),
                onGameRestarted: () => GameRestartedReceived?.Invoke(),
                onChatMessage: msg => ChatMessageReceived?.Invoke(msg));
            
            // Subscribe to observer if supported
            try
            {
                var observerRef = _rpcMultiplexer?.CreateObjectReference<IGameRpcObserver>(_observer);
                if (observerRef != null && _gameGrain != null)
                {
                    await _gameGrain.Subscribe(observerRef);
                    _logger.LogInformation("Successfully subscribed to game observer");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to subscribe to observer, will use polling");
            }
            
            IsConnected = true;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect");
            return false;
        }
    }
    
    public async Task UpdatePlayerInputAsync(Vector2 moveDirection, bool isShooting)
    {
        if (!IsConnected || string.IsNullOrEmpty(PlayerId) || _gameGrain == null)
            return;
        
        try
        {
            // Store last input for retry
            _lastInputDirection = moveDirection;
            _lastInputShooting = isShooting;
            
            await _gameGrain.UpdatePlayerInput(PlayerId, moveDirection, isShooting);
            _playerInputErrorLogged = false;
        }
        catch (Exception ex)
        {
            if (!_playerInputErrorLogged || (DateTime.UtcNow - _lastPlayerInputError).TotalSeconds > 5)
            {
                _logger.LogError(ex, "Failed to update player input");
                _playerInputErrorLogged = true;
                _lastPlayerInputError = DateTime.UtcNow;
            }
        }
    }
    
    private async void PollWorldState(object? state)
    {
        if (!IsConnected || _gameGrain == null || _isTransitioning)
            return;
        
        try
        {
            _lastWorldStatePollTime = DateTime.UtcNow;
            var worldState = await _gameGrain.GetWorldState();
            
            if (worldState != null)
            {
                _worldStatePollFailures = 0;
                _worldStateErrorLogged = false;
                _lastWorldState = worldState;
                
                // Check for zone change
                CheckForZoneTransition(worldState);
                
                WorldStateUpdated?.Invoke(worldState);
            }
        }
        catch (Exception ex)
        {
            _worldStatePollFailures++;
            
            if (!_worldStateErrorLogged || (DateTime.UtcNow - _lastWorldStateError).TotalSeconds > 5)
            {
                _logger.LogError(ex, "Failed to get world state (failure count: {FailureCount})", 
                    _worldStatePollFailures);
                _worldStateErrorLogged = true;
                _lastWorldStateError = DateTime.UtcNow;
            }
            
            // Handle connection issues by getting grain from multiplexer
            if (_worldStatePollFailures > 10)
            {
                _logger.LogWarning("Too many world state poll failures, attempting to refresh grain reference");
                _ = RefreshGrainReference();
            }
        }
    }
    
    private async Task RefreshGrainReference()
    {
        try
        {
            if (_rpcMultiplexer == null)
            {
                _logger.LogError("Cannot refresh grain reference - multiplexer is null");
                return;
            }
            
            // Update routing context if we have zone information
            if (_currentZone != null)
            {
                var context = new RoutingContext();
                context.SetProperty("Zone", $"{_currentZone.X},{_currentZone.Y}");
                _gameGrain = await _rpcMultiplexer.GetGrainAsync<IGameRpcGrain>("game", context);
            }
            else
            {
                _gameGrain = await _rpcMultiplexer.GetGrainAsync<IGameRpcGrain>("game");
            }
            
            _worldStatePollFailures = 0;
            _logger.LogInformation("Successfully refreshed grain reference");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh grain reference");
        }
    }
    
    private void CheckForZoneTransition(WorldState worldState)
    {
        if (string.IsNullOrEmpty(PlayerId))
            return;
        
        var player = worldState.Players.FirstOrDefault(p => p.Id == PlayerId);
        if (player == null)
            return;
        
        var playerZone = GridSquare.FromPosition(player.Position);
        
        // Update zone in routing strategy
        if (_currentZone == null || _currentZone.X != playerZone.X || _currentZone.Y != playerZone.Y)
        {
            _logger.LogInformation("Player moved from zone {OldZone} to {NewZone}",
                _currentZone != null ? $"{_currentZone.X},{_currentZone.Y}" : "unknown",
                $"{playerZone.X},{playerZone.Y}");
            
            _currentZone = playerZone;
            _visitedZones.Add($"{playerZone.X},{playerZone.Y}");
            
            // Get zone-appropriate grain from multiplexer
            _ = Task.Run(async () =>
            {
                try
                {
                    var context = new RoutingContext();
                    context.SetProperty("Zone", $"{playerZone.X},{playerZone.Y}");
                    
                    var newGrain = await _rpcMultiplexer!.GetGrainAsync<IGameRpcGrain>("game", context);
                    if (newGrain != null)
                    {
                        _gameGrain = newGrain;
                        _logger.LogInformation("Updated grain reference for zone {Zone}", $"{playerZone.X},{playerZone.Y}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to update grain for zone transition");
                }
            });
        }
    }
    
    private async void SendHeartbeat(object? state)
    {
        if (!IsConnected || string.IsNullOrEmpty(PlayerId))
            return;
        
        try
        {
            await _httpClient.PostAsync(
                $"{_configuration["SiloApiUrl"]}/api/game/heartbeat/{PlayerId}",
                null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send heartbeat");
        }
    }
    
    private async void PollAvailableZones(object? state)
    {
        if (!IsConnected || _gameGrain == null)
            return;
        
        try
        {
            var zones = await _gameGrain.GetAvailableZones();
            if (zones != null && zones.Count > 0)
            {
                _cachedAvailableZones = zones;
                AvailableZonesUpdated?.Invoke(zones);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get available zones");
        }
    }
    
    private async void PollChatMessages(object? state)
    {
        if (!IsConnected || _gameGrain == null)
            return;
        
        try
        {
            var messages = await _gameGrain.GetRecentChatMessages(_lastChatPollTime);
            if (messages != null && messages.Count > 0)
            {
                foreach (var msg in messages.OrderBy(m => m.Timestamp))
                {
                    ChatMessageReceived?.Invoke(msg);
                    _lastChatPollTime = msg.Timestamp;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to poll chat messages");
        }
    }
    
    private async void PollNetworkStats(object? state)
    {
        if (!IsConnected || _gameGrain == null)
            return;
        
        try
        {
            var stats = await _gameGrain.GetNetworkStatistics();
            if (stats != null)
            {
                _latestNetworkStats = stats;
                NetworkStatsUpdated?.Invoke(stats);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get network statistics");
        }
    }
    
    private async void DiscoverServers(object? state)
    {
        try
        {
            var actionServersResponse = await _httpClient.GetAsync($"{_configuration["SiloApiUrl"]}/api/game/actionservers");
            if (actionServersResponse.IsSuccessStatusCode)
            {
                var actionServers = await actionServersResponse.Content.ReadFromJsonAsync<List<ActionServerInfo>>();
                if (actionServers != null)
                {
                    foreach (var server in actionServers)
                    {
                        if (!_discoveredServers.ContainsKey(server.ServerId))
                        {
                            _logger.LogInformation("Discovered new server: {ServerId} for zone {Zone}",
                                server.ServerId, server.Zone);
                            
                            var serverDescriptor = new ServerDescriptor
                            {
                                ServerId = server.ServerId,
                                HostName = server.Host,
                                Port = server.RpcPort,
                                IsPrimary = server.IsPrimary,
                                Metadata = new Dictionary<string, string>
                                {
                                    ["zone"] = server.Zone,
                                    ["httpPort"] = server.HttpPort.ToString()
                                }
                            };
                            
                            _rpcMultiplexer?.RegisterServer(serverDescriptor);
                            _discoveredServers[server.ServerId] = server;
                            
                            if (_routingStrategy != null && !string.IsNullOrEmpty(server.Zone))
                            {
                                _routingStrategy.MapZoneToServer(server.Zone, server.ServerId);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to discover new servers");
        }
    }
    
    public async Task SendChatMessageAsync(string message)
    {
        if (!IsConnected || string.IsNullOrEmpty(PlayerId) || string.IsNullOrEmpty(PlayerName) || _gameGrain == null)
            return;
        
        try
        {
            var chatMessage = new ChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                PlayerId = PlayerId,
                PlayerName = PlayerName,
                Message = message,
                Timestamp = DateTime.UtcNow
            };
            
            await _gameGrain.SendChatMessage(chatMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send chat message");
        }
    }
    
    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        
        _worldStateTimer?.Dispose();
        _heartbeatTimer?.Dispose();
        _availableZonesTimer?.Dispose();
        _chatPollingTimer?.Dispose();
        _networkStatsTimer?.Dispose();
        _serverDiscoveryTimer?.Dispose();
        
        if (_rpcMultiplexer != null)
        {
            _ = _rpcMultiplexer.DisposeAsync();
        }
        
        _cancellationTokenSource?.Dispose();
    }
    
    /// <summary>
    /// Shooter-specific routing strategy that maps zones to servers.
    /// </summary>
    private class ShooterRoutingStrategy : IGrainRoutingStrategy
    {
        private readonly ILogger _logger;
        private readonly Dictionary<string, string> _zoneToServerMapping = new();
        
        public ShooterRoutingStrategy(ILogger logger)
        {
            _logger = logger;
        }
        
        public void MapZoneToServer(string zone, string serverId)
        {
            _zoneToServerMapping[zone] = serverId;
            _logger.LogDebug("Mapped zone {Zone} to server {ServerId}", zone, serverId);
        }
        
        public Task<string> SelectServerAsync(
            Type grainInterface,
            string grainKey,
            IReadOnlyDictionary<string, IServerDescriptor> servers,
            IRoutingContext context)
        {
            // For game grains, check if we have zone context
            if (grainInterface == typeof(IGameRpcGrain))
            {
                var zone = context?.GetProperty<string>("Zone");
                if (!string.IsNullOrEmpty(zone) && _zoneToServerMapping.TryGetValue(zone, out var serverId))
                {
                    if (servers.ContainsKey(serverId))
                    {
                        _logger.LogDebug("Routing to server {ServerId} for zone {Zone}", serverId, zone);
                        return Task.FromResult(serverId);
                    }
                }
            }
            
            // Default to primary server
            var primary = servers.Values.FirstOrDefault(s => s.IsPrimary);
            if (primary != null)
            {
                return Task.FromResult(primary.ServerId);
            }
            
            // Any server
            var anyServer = servers.Values.FirstOrDefault();
            return Task.FromResult(anyServer?.ServerId);
        }
    }
}