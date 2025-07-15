using Orleans;
using Orleans.Metadata;
using Orleans.Runtime;
using Granville.Rpc;
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

namespace Shooter.Client.Common;

/// <summary>
/// Game client service that uses Orleans RPC for all communication with ActionServers.
/// This provides a single LiteNetLib UDP connection for all game operations.
/// </summary>
public class GranvilleRpcGameClientService : IDisposable
{
    private readonly ILogger<GranvilleRpcGameClientService> _logger;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private Granville.Rpc.IRpcClient? _rpcClient;
    private IHost? _rpcHost;
    private IGameRpcGrain? _gameGrain;
    private Timer? _worldStateTimer;
    private Timer? _heartbeatTimer;
    private Timer? _availableZonesTimer;
    private Timer? _chatPollingTimer;
    private DateTime _lastChatPollTime = DateTime.UtcNow;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isTransitioning = false;
    private DateTime _lastZoneBoundaryCheck = DateTime.MinValue;
    private DateTime _lastZoneChangeTime = DateTime.MinValue;
    private GridSquare? _lastDetectedZone = null;
    private long _lastSequenceNumber = -1;
    private readonly Dictionary<string, PreEstablishedConnection> _preEstablishedConnections = new();
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
    private Dictionary<string, List<EntityState>> _cachedAdjacentEntities = new();
    
    public event Action<WorldState>? WorldStateUpdated;
    public event Action<string>? ServerChanged;
    public event Action<List<GridSquare>>? AvailableZonesUpdated;
    public event Action<Dictionary<string, (bool isConnected, bool isNeighbor, bool isConnecting)>>? PreEstablishedConnectionsUpdated;
    public event Action<ZoneStatistics>? ZoneStatsUpdated;
    public event Action<ScoutAlert>? ScoutAlertReceived;
    public event Action<GameOverMessage>? GameOverReceived;
    public event Action? GameRestartedReceived;
    public event Action<ChatMessage>? ChatMessageReceived;
    
    public bool IsConnected { get; private set; }
    public string? PlayerId { get; private set; }
    public string? PlayerName { get; private set; }
    public string? CurrentServerId { get; private set; }
    public string? TransportType { get; private set; }
    
    public GranvilleRpcGameClientService(
        ILogger<GranvilleRpcGameClientService> logger,
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _logger = logger;
        _httpClient = httpClient;
        _configuration = configuration;
    }
    
    public async Task<bool> ConnectAsync(string playerName)
    {
        try
        {
            _cancellationTokenSource = new CancellationTokenSource();
            
            // Log the instance hash code to verify we have separate instances
            _logger.LogInformation("[CONNECT] GranvilleRpcGameClientService instance {InstanceId} connecting for player {PlayerName}", 
                this.GetHashCode(), playerName);
            
            // First register with HTTP to get server info and player ID
            var registrationResponse = await RegisterWithHttpAsync(playerName);
            if (registrationResponse == null) return false;
            
            PlayerId = registrationResponse.PlayerInfo?.PlayerId ?? string.Empty;
            PlayerName = registrationResponse.PlayerInfo?.Name ?? playerName;
            CurrentServerId = registrationResponse.ActionServer?.ServerId ?? "Unknown";
            _currentZone = registrationResponse.ActionServer?.AssignedSquare;
            
            // Mark initial zone as visited
            if (_currentZone != null)
            {
                _visitedZones.Add($"{_currentZone.X},{_currentZone.Y}");
            }
            
            _logger.LogInformation("[CONNECT] Instance {InstanceId} - Player registered with ID {PlayerId} on server {ServerId} in zone ({X}, {Y})", 
                this.GetHashCode(), PlayerId, CurrentServerId, 
                registrationResponse.ActionServer?.AssignedSquare.X ?? -1, 
                registrationResponse.ActionServer?.AssignedSquare.Y ?? -1);
            
            if (registrationResponse.ActionServer == null)
            {
                _logger.LogError("No action server assigned");
                return false;
            }
            
            // Extract host and RPC port from ActionServer info
            var serverHost = registrationResponse.ActionServer.IpAddress;
            var rpcPort = registrationResponse.ActionServer.RpcPort;
            
            if (rpcPort == 0)
            {
                _logger.LogError("ActionServer did not report RPC port");
                return false;
            }
            
            // Resolve hostname to IP address if needed
            string resolvedHost = serverHost;
            try
            {
                // Check if it's already an IP address
                if (!System.Net.IPAddress.TryParse(serverHost, out _))
                {
                    // Resolve hostname to IP
                    var hostEntry = await System.Net.Dns.GetHostEntryAsync(serverHost);
                    var ipAddress = hostEntry.AddressList.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                    if (ipAddress != null)
                    {
                        resolvedHost = ipAddress.ToString();
                        _logger.LogInformation("Resolved hostname {Host} to IP {IP}", serverHost, resolvedHost);
                    }
                    else
                    {
                        // Fallback to localhost IP
                        resolvedHost = "127.0.0.1";
                        _logger.LogWarning("Could not resolve {Host}, using localhost IP", serverHost);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve hostname {Host}, using localhost IP", serverHost);
                resolvedHost = "127.0.0.1";
            }
            
            _logger.LogInformation("Connecting to Orleans RPC server at {Host}:{Port}", resolvedHost, rpcPort);
            
            // Create RPC client
            var hostBuilder = Host.CreateDefaultBuilder()
                .UseOrleansRpcClient(rpcBuilder =>
                {
                    rpcBuilder.ConnectTo(resolvedHost, rpcPort);
                    // Configure transport based on configuration
                    var transportType = _configuration["RpcTransport"] ?? "litenetlib";
                    TransportType = transportType.ToLowerInvariant();
                    switch (TransportType)
                    {
                        case "ruffles":
                            _logger.LogInformation("Using Ruffles UDP transport");
                            rpcBuilder.UseRuffles();
                            break;
                        case "litenetlib":
                        default:
                            _logger.LogInformation("Using LiteNetLib UDP transport");
                            rpcBuilder.UseLiteNetLib();
                            TransportType = "litenetlib"; // Ensure we always have a value
                            break;
                    }
                })
                .ConfigureServices(services =>
                {
                    // Add serialization for the grain interfaces and shared models
                    services.AddSerializer(serializer =>
                    {
                        serializer.AddAssembly(typeof(IGameRpcGrain).Assembly);
                    });
                })
                .Build();
                
            await hostBuilder.StartAsync();
            
            _rpcHost = hostBuilder;
            _rpcClient = hostBuilder.Services.GetRequiredService<Granville.Rpc.IRpcClient>();
            
            // Debug: check what manifest provider we have
            try
            {
                var manifestProvider = hostBuilder.Services.GetKeyedService<IClusterManifestProvider>("rpc");
                _logger.LogInformation("RPC manifest provider type: {Type}", manifestProvider?.GetType().FullName ?? "NULL");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not get keyed manifest provider: {Error}", ex.Message);
            }
            
            // Wait for the handshake and manifest exchange to complete
            // Try to get the grain with retries to ensure manifest is ready
            const int maxRetries = 10;
            const int retryDelayMs = 500;
            
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    // Get the game grain - use a fixed key since this represents the server itself
                    // In RPC, grains are essentially singleton services per server
                    _gameGrain = _rpcClient.GetGrain<IGameRpcGrain>("game");
                    _logger.LogInformation("Successfully obtained game grain on attempt {Attempt}", i + 1);
                    break;
                }
                catch (ArgumentException ex) when (ex.Message.Contains("Could not find an implementation"))
                {
                    if (i < maxRetries - 1)
                    {
                        _logger.LogDebug("Waiting for manifest update, attempt {Attempt}/{MaxRetries}", i + 1, maxRetries);
                        await Task.Delay(retryDelayMs);
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"Failed to get game grain after {maxRetries} attempts. " +
                            "The RPC server may not have registered the grain implementation.", ex);
                    }
                }
            }
            
            if (_gameGrain == null)
            {
                throw new InvalidOperationException("Failed to obtain game grain reference");
            }
            
            // Connect via RPC
            _logger.LogInformation("Connecting player {PlayerId} via RPC", PlayerId);
            var result = await _gameGrain.ConnectPlayer(PlayerId);
            _logger.LogInformation("RPC ConnectPlayer returned: {Result}", result);
            
            if (result != "SUCCESS")
            {
                _logger.LogError("Failed to connect player {PlayerId} to server", PlayerId);
                return false;
            }
            
            IsConnected = true;
            _logger.LogInformation("Player {PlayerId} successfully connected to server {ServerId}", PlayerId, CurrentServerId);
            
            // Create and subscribe observer for push updates
            try
            {
                var loggerFactory = _rpcHost.Services.GetRequiredService<ILoggerFactory>();
                _observer = new GameRpcObserver(loggerFactory.CreateLogger<GameRpcObserver>(), this);
                
                // Create an observer reference
                var observerRef = _rpcClient.CreateObjectReference<IGameRpcObserver>(_observer);
                await _gameGrain.Subscribe(observerRef);
                
                _logger.LogInformation("[CHAT_DEBUG] Successfully subscribed to game updates via observer. Observer created: {ObserverCreated}", _observer != null);
            }
            catch (NotSupportedException nse)
            {
                _logger.LogWarning(nse, "[CHAT_DEBUG] Observer pattern not supported by RPC transport, falling back to polling.");
                // Start chat polling as fallback
                StartChatPolling();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[CHAT_DEBUG] Failed to subscribe observer, falling back to polling. Exception type: {ExceptionType}", ex.GetType().Name);
                // Start chat polling as fallback
                StartChatPolling();
            }
            
            // Start polling for world state
            _worldStateTimer = new Timer(async _ => await PollWorldState(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(16));
            
            // Start heartbeat
            _heartbeatTimer = new Timer(async _ => await SendHeartbeat(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
            
            // Start polling for available zones
            _availableZonesTimer = new Timer(async _ => await PollAvailableZones(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
            
            _logger.LogInformation("Connected to game via Orleans RPC");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to game server");
            return false;
        }
    }
    
    private bool IsBotName(string playerName)
    {
        // Bot names follow patterns like "LiteNetLibTest0", "RufflesTest1", etc.
        // They contain transport name + optional "Test" + number
        // This method helps identify bots so they can use predictable player IDs
        // instead of random GUIDs, preventing duplicate bot ships when multiple
        // bot instances connect with the same bot name
        return System.Text.RegularExpressions.Regex.IsMatch(playerName, @"^(LiteNetLib|Ruffles)(Test)?\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
    
    private async Task<PlayerRegistrationResponse?> RegisterWithHttpAsync(string playerName)
    {
        try
        {
            // Always generate a unique player ID to prevent conflicts
            // Even for bots, we need unique IDs when multiple instances run
            string playerId = Guid.NewGuid().ToString();
            
            if (IsBotName(playerName))
            {
                _logger.LogInformation("Registering bot {BotName} with unique ID: {PlayerId}", playerName, playerId);
            }
            else
            {
                _logger.LogInformation("Registering player {PlayerName} with ID: {PlayerId}", playerName, playerId);
            }
            
            var response = await _httpClient.PostAsJsonAsync("api/world/players/register", 
                new { PlayerId = playerId, Name = playerName });
            
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<PlayerRegistrationResponse>();
            }
            
            _logger.LogError("Failed to register player: {StatusCode}", response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering player");
            return null;
        }
    }
    
    public async Task DisconnectAsync()
    {
        if (_gameGrain != null && IsConnected)
        {
            try
            {
                await _gameGrain.DisconnectPlayer(PlayerId!);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting player");
            }
        }
        
        Cleanup();
    }
    
    public async Task SendPlayerInput(Vector2 moveDirection, bool isShooting)
    {
        if (_gameGrain == null || !IsConnected || string.IsNullOrEmpty(PlayerId))
        {
            return;
        }
        
        // Track last input for zone transitions
        _lastInputDirection = moveDirection;
        _lastInputShooting = isShooting;
        
        try
        {
            await _gameGrain.UpdatePlayerInput(PlayerId, moveDirection, isShooting);
        }
        catch (Exception ex)
        {
            // Throttle error logging to avoid spamming logs during connection issues
            if (!_playerInputErrorLogged || (DateTime.UtcNow - _lastPlayerInputError).TotalSeconds > 5)
            {
                _logger.LogError(ex, "Failed to send player input");
                _playerInputErrorLogged = true;
                _lastPlayerInputError = DateTime.UtcNow;
            }
        }
    }
    
    public async Task SendPlayerInputEx(Vector2? moveDirection, Vector2? shootDirection)
    {
        if (_gameGrain == null || !IsConnected || string.IsNullOrEmpty(PlayerId))
        {
            return;
        }
        
        // Track last input for zone transitions
        if (moveDirection.HasValue)
        {
            _lastInputDirection = moveDirection.Value;
        }
        _lastInputShooting = shootDirection.HasValue;
        
        try
        {
            // Add timeout to prevent 30-second hangs
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            
            // Debug log to track which player is sending input
            if (moveDirection.HasValue || shootDirection.HasValue)
            {
                _logger.LogDebug("[INPUT_SEND] Instance {InstanceId} - Player {PlayerId} sending input - Move: {Move}, Shoot: {Shoot}", 
                    this.GetHashCode(), PlayerId, moveDirection.HasValue, shootDirection.HasValue);
            }
            
            await _gameGrain.UpdatePlayerInputEx(PlayerId, moveDirection, shootDirection).WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Player input timed out after 5 seconds, marking connection as lost");
            IsConnected = false;
            // Fire connection lost event
            ServerChanged?.Invoke("Connection lost");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send player input");
            // Check if this is a connection error
            if (ex.Message.Contains("Not connected") || ex.Message.Contains("RPC client is not connected"))
            {
                IsConnected = false;
                ServerChanged?.Invoke("Connection lost");
            }
        }
    }
    
    private async Task PollWorldState()
    {
        if (_gameGrain == null || !IsConnected || _cancellationTokenSource?.Token.IsCancellationRequested == true || _isTransitioning)
        {
            return;
        }
        
        try
        {
            var worldState = await _gameGrain.GetWorldState();
            if (worldState != null)
            {
                // Check sequence number to discard out-of-order updates
                if (worldState.SequenceNumber <= _lastSequenceNumber)
                {
                    _logger.LogDebug("Discarding out-of-order world state (seq: {Current} <= {Last})", 
                        worldState.SequenceNumber, _lastSequenceNumber);
                    return;
                }
                _lastSequenceNumber = worldState.SequenceNumber;
                _lastWorldState = worldState;
                
                var entityCount = worldState.Entities?.Count ?? 0;
                _logger.LogTrace("Received world state with {Count} entities (seq: {Sequence})", 
                    entityCount, worldState.SequenceNumber);
                
                // Log entity breakdown
                if (entityCount > 0 && worldState.Entities != null)
                {
                    var entityTypes = worldState.Entities.GroupBy(e => e.Type)
                        .ToDictionary(g => g.Key, g => g.Count());
                    _logger.LogTrace("Entity breakdown: Players={Players}, Enemies={Enemies}, Bullets={Bullets}, Explosions={Explosions}",
                        entityTypes.GetValueOrDefault(EntityType.Player, 0),
                        entityTypes.GetValueOrDefault(EntityType.Enemy, 0),
                        entityTypes.GetValueOrDefault(EntityType.Bullet, 0),
                        entityTypes.GetValueOrDefault(EntityType.Explosion, 0));
                }
                
                // Fetch entities from adjacent zones if player is near borders
                var adjacentEntities = await FetchAdjacentZoneEntities();
                if (adjacentEntities.Any())
                {
                    // Merge adjacent entities with current state
                    var allEntities = worldState.Entities?.ToList() ?? new List<EntityState>();
                    allEntities.AddRange(adjacentEntities);
                    worldState = new WorldState(allEntities, worldState.Timestamp, worldState.SequenceNumber);
                    
                    _logger.LogTrace("Added {Count} entities from adjacent zones, total: {Total}", 
                        adjacentEntities.Count, worldState.Entities.Count);
                }
                else if (_currentZone != null)
                {
                    // Check if player is near borders but no adjacent entities were fetched
                    var player = worldState.Entities?.FirstOrDefault(e => e.EntityId == PlayerId);
                    if (player != null)
                    {
                        var (min, max) = _currentZone.GetBounds();
                        var pos = player.Position;
                        
                        bool nearLeftEdge = pos.X <= min.X + 150;
                        bool nearRightEdge = pos.X >= max.X - 150;
                        bool nearTopEdge = pos.Y <= min.Y + 150;
                        bool nearBottomEdge = pos.Y >= max.Y - 150;
                        
                        if (nearLeftEdge || nearRightEdge || nearTopEdge || nearBottomEdge)
                        {
                            _logger.LogTrace("Player near borders but no adjacent entities fetched. Position: {Position}, Near edges: L={Left} R={Right} T={Top} B={Bottom}", 
                                pos, nearLeftEdge, nearRightEdge, nearTopEdge, nearBottomEdge);
                        }
                    }
                }
                
                // Check if our player still exists in the world state
                var playerExists = worldState.Entities?.Any(e => e.EntityId == PlayerId) ?? false;
                if (!playerExists && !string.IsNullOrEmpty(PlayerId))
                {
                    _logger.LogWarning("Player {PlayerId} not found in world state (total entities: {Count}), checking for server transition", 
                        PlayerId, worldState.Entities?.Count ?? 0);
                    
                    // Log all player entities for debugging
                    var playerEntities = worldState.Entities?.Where(e => e.Type == EntityType.Player).ToList() ?? new List<EntityState>();
                    _logger.LogInformation("Players in world state: {Players}", 
                        string.Join(", ", playerEntities.Select(p => p.EntityId)));
                    
                    await CheckForServerTransition();
                }
                else
                {
                    // Check if player has changed zones
                    var playerEntity = worldState.Entities?.FirstOrDefault(e => e.EntityId == PlayerId);
                    if (playerEntity != null)
                    {
                        var playerZone = GridSquare.FromPosition(playerEntity.Position);
                        
                        // Check if player's actual zone differs from the server's zone
                        if (_currentZone != null && (playerZone.X != _currentZone.X || playerZone.Y != _currentZone.Y))
                        {
                            // Debounce zone changes - only process if this is a new zone change or enough time has passed
                            var now = DateTime.UtcNow;
                            var timeSinceLastChange = (now - _lastZoneChangeTime).TotalSeconds;
                            
                            if (_lastDetectedZone == null || 
                                _lastDetectedZone.X != playerZone.X || 
                                _lastDetectedZone.Y != playerZone.Y ||
                                timeSinceLastChange > 2.0) // Allow re-processing after 2 seconds
                            {
                                _lastZoneChangeTime = now;
                                _lastDetectedZone = playerZone;
                                
                                _logger.LogInformation("[CLIENT_ZONE_CHANGE] Player moved from zone ({OldX},{OldY}) to ({NewX},{NewY}) at position {Position}", 
                                    _currentZone.X, _currentZone.Y, playerZone.X, playerZone.Y, playerEntity.Position);
                                
                                // Immediately check for server transition
                                _ = Task.Run(async () => {
                                    try {
                                        await CheckForServerTransition();
                                    } catch (Exception ex) {
                                        _logger.LogError(ex, "Failed to check for server transition after zone change");
                                    }
                                });
                            }
                            else
                            {
                                _logger.LogDebug("[CLIENT_ZONE_CHANGE] Ignoring duplicate zone change detection to ({X},{Y}) - last change was {Seconds}s ago", 
                                    playerZone.X, playerZone.Y, timeSinceLastChange);
                            }
                        }
                        else
                        {
                            // Also check if near zone boundary for proactive connection establishment
                            var (min, max) = playerZone.GetBounds();
                            var distToEdge = Math.Min(
                                Math.Min(playerEntity.Position.X - min.X, max.X - playerEntity.Position.X),
                                Math.Min(playerEntity.Position.Y - min.Y, max.Y - playerEntity.Position.Y)
                            );
                            
                            if (distToEdge < 50)
                            {
                                // Throttle zone boundary checks using configurable interval
                                var now = DateTime.UtcNow;
                                if ((now - _lastZoneBoundaryCheck).TotalSeconds >= Shooter.Shared.GameConstants.ZoneBoundaryCheckInterval)
                                {
                                    _lastZoneBoundaryCheck = now;
                                    _logger.LogDebug("Player {PlayerId} near zone boundary (distance: {Distance}) at position {Position}", 
                                        PlayerId, distToEdge, playerEntity.Position);
                                    
                                    // Proactively check for server transition
                                    _ = Task.Run(async () => {
                                        try {
                                            await CheckForServerTransition();
                                        } catch (Exception ex) {
                                            _logger.LogError(ex, "Failed to check for server transition");
                                        }
                                    });
                                }
                            }
                        }
                    }
                    
                    WorldStateUpdated?.Invoke(worldState);
                }
            }
            else
            {
                _logger.LogWarning("Received null world state from server");
            }
        }
        catch (Exception ex)
        {
            // Throttle error logging to avoid spamming logs during connection issues
            if (!_worldStateErrorLogged || (DateTime.UtcNow - _lastWorldStateError).TotalSeconds > 5)
            {
                _logger.LogError(ex, "Failed to get world state");
                _worldStateErrorLogged = true;
                _lastWorldStateError = DateTime.UtcNow;
            }
        }
    }
    
    private async Task SendHeartbeat()
    {
        if (_gameGrain == null || !IsConnected || _cancellationTokenSource?.Token.IsCancellationRequested == true)
        {
            return;
        }
        
        try
        {
            // The UpdatePlayerInput with zero movement acts as a heartbeat
            await _gameGrain.UpdatePlayerInput(PlayerId!, Vector2.Zero, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send heartbeat");
        }
    }
    
    private async Task PollAvailableZones()
    {
        if (_gameGrain == null || !IsConnected || _cancellationTokenSource?.Token.IsCancellationRequested == true)
        {
            return;
        }
        
        try
        {
            var zones = await _gameGrain.GetAvailableZones();
            if (zones != null)
            {
                AvailableZonesUpdated?.Invoke(zones);
                
                // Pre-establish connections to neighboring zones
                await PreEstablishNeighborConnections(zones);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get available zones");
        }
    }
    
    private async Task CheckForServerTransition()
    {
        if (string.IsNullOrEmpty(PlayerId) || _isTransitioning)
        {
            return;
        }
        
        // Get current player position from last world state
        var playerEntity = _lastWorldState?.Entities?.FirstOrDefault(e => e.EntityId == PlayerId);
        if (playerEntity == null)
        {
            return;
        }
        
        var playerZone = GridSquare.FromPosition(playerEntity.Position);
        
        // If player is in current zone, no transition needed
        if (_currentZone != null && playerZone.X == _currentZone.X && playerZone.Y == _currentZone.Y)
        {
            return;
        }
        
        _isTransitioning = true;
        
        try
        {
            _logger.LogTrace("Checking for server transition for player {PlayerId} at position {Position} in zone ({ZoneX},{ZoneY})", 
                PlayerId, playerEntity.Position, playerZone.X, playerZone.Y);
            
            // Query the Orleans silo for the correct server
            var response = await _httpClient.GetFromJsonAsync<Shooter.Shared.Models.ActionServerInfo>(
                $"api/world/players/{PlayerId}/server");
                
            if (response != null && response.ServerId != CurrentServerId)
            {
                _logger.LogInformation("[ZONE_TRANSITION] Player {PlayerId} needs to transition from server {OldServer} to {NewServer} for zone ({ZoneX},{ZoneY})", 
                    PlayerId, CurrentServerId, response.ServerId, response.AssignedSquare.X, response.AssignedSquare.Y);
                
                var cleanupStart = DateTime.UtcNow;
                
                // Stop timers before disconnecting
                _worldStateTimer?.Dispose();
                _heartbeatTimer?.Dispose();
                _availableZonesTimer?.Dispose();
                _worldStateTimer = null;
                _heartbeatTimer = null;
                _availableZonesTimer = null;
                
                // Explicitly disconnect player from old server before cleanup
                if (_gameGrain != null && !string.IsNullOrEmpty(PlayerId))
                {
                    try
                    {
                        _logger.LogInformation("[ZONE_TRANSITION] Disconnecting player {PlayerId} from old server", PlayerId);
                        await _gameGrain.DisconnectPlayer(PlayerId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[ZONE_TRANSITION] Failed to disconnect player from old server");
                    }
                }
                
                // Disconnect from current server
                Cleanup();
                
                var cleanupDuration = (DateTime.UtcNow - cleanupStart).TotalMilliseconds;
                _logger.LogInformation("[ZONE_TRANSITION] Cleanup took {Duration}ms", cleanupDuration);
                
                // Reconnect to new server
                var connectStart = DateTime.UtcNow;
                await ConnectToActionServer(response);
                var connectDuration = (DateTime.UtcNow - connectStart).TotalMilliseconds;
                _logger.LogInformation("[ZONE_TRANSITION] ConnectToActionServer took {Duration}ms", connectDuration);
                
                // Reset zone change detection state after successful transition
                _lastDetectedZone = null;
                _lastZoneChangeTime = DateTime.MinValue;
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Player {PlayerId} not found in any server, may have been removed", PlayerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for server transition");
        }
        finally
        {
            _isTransitioning = false;
        }
    }
    
    private async Task ConnectToActionServer(Shooter.Shared.Models.ActionServerInfo serverInfo)
    {
        CurrentServerId = serverInfo.ServerId;
        _currentZone = serverInfo.AssignedSquare;
        
        // Mark zone as visited
        _visitedZones.Add($"{serverInfo.AssignedSquare.X},{serverInfo.AssignedSquare.Y}");
        
        // Create new cancellation token source for the new connection
        _cancellationTokenSource = new CancellationTokenSource();
        
        // Check if we have a pre-established connection for this zone
        var connectionKey = $"{serverInfo.AssignedSquare.X},{serverInfo.AssignedSquare.Y}";
        _logger.LogInformation("[ZONE_TRANSITION] Checking for pre-established connection with key {Key}. Available keys: {Keys}", 
            connectionKey, string.Join(", ", _preEstablishedConnections.Keys));
        
        if (_preEstablishedConnections.TryGetValue(connectionKey, out var preEstablished) && preEstablished.IsConnected)
        {
            // Verify the connection is still alive by attempting a simple operation
            bool connectionStillValid = false;
            try
            {
                _logger.LogInformation("[ZONE_TRANSITION] Testing pre-established connection for zone {Key}", connectionKey);
                var testState = await preEstablished.GameGrain!.GetWorldState();
                connectionStillValid = true;
                _logger.LogInformation("[ZONE_TRANSITION] Pre-established connection test successful");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[ZONE_TRANSITION] Pre-established connection for zone {Key} is no longer valid: {Error}", 
                    connectionKey, ex.Message);
                preEstablished.IsConnected = false;
            }
            
            if (connectionStillValid)
            {
                _logger.LogInformation("[ZONE_TRANSITION] Using pre-established connection for zone {Key}", connectionKey);
                
                _rpcHost = preEstablished.RpcHost;
                _rpcClient = preEstablished.RpcClient;
                _gameGrain = preEstablished.GameGrain;
                
                // Don't remove from pre-established connections yet - let the cleanup handle it
                // This prevents race conditions where the connection might be needed again soon
                
                // Reconnect player
                _logger.LogInformation("Calling ConnectPlayer for {PlayerId} on pre-established connection", PlayerId);
                var result = await _gameGrain!.ConnectPlayer(PlayerId!);
                _logger.LogInformation("ConnectPlayer returned: {Result}", result);
                
                if (result != "SUCCESS")
                {
                    _logger.LogError("Failed to reconnect player {PlayerId} to pre-established server", PlayerId);
                    return;
                }
            }
            else
            {
                // Connection is dead, remove it and fall through to create a new one
                _logger.LogInformation("[ZONE_TRANSITION] Removing dead pre-established connection for zone {Key}", connectionKey);
                await CleanupPreEstablishedConnection(connectionKey);
                _preEstablishedConnections.Remove(connectionKey);
            }
        }
        
        if (_rpcHost == null)
        {
            _logger.LogInformation("[ZONE_TRANSITION] No valid pre-established connection for zone {Key}, creating new connection", connectionKey);
            
            // Extract host and RPC port
            var serverHost = serverInfo.IpAddress;
            var rpcPort = serverInfo.RpcPort;
            
            if (rpcPort == 0)
            {
                _logger.LogError("ActionServer did not report RPC port");
                return;
            }
            
            // Resolve hostname to IP address if needed
            string resolvedHost = serverHost;
            try
            {
                if (!System.Net.IPAddress.TryParse(serverHost, out _))
                {
                    var hostEntry = await System.Net.Dns.GetHostEntryAsync(serverHost);
                    var ipAddress = hostEntry.AddressList.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                    if (ipAddress != null)
                    {
                        resolvedHost = ipAddress.ToString();
                    }
                    else
                    {
                        resolvedHost = "127.0.0.1";
                    }
                }
            }
            catch
            {
                resolvedHost = "127.0.0.1";
            }
            
            _logger.LogInformation("Connecting to new Orleans RPC server at {Host}:{Port}", resolvedHost, rpcPort);
            
            // Create new RPC client
            var hostBuilder = Host.CreateDefaultBuilder()
                .UseOrleansRpcClient(rpcBuilder =>
                {
                    rpcBuilder.ConnectTo(resolvedHost, rpcPort);
                    // Configure transport based on configuration
                    var transportType = _configuration["RpcTransport"] ?? "litenetlib";
                    TransportType = transportType.ToLowerInvariant();
                    switch (TransportType)
                    {
                        case "ruffles":
                            _logger.LogInformation("Using Ruffles UDP transport");
                            rpcBuilder.UseRuffles();
                            break;
                        case "litenetlib":
                        default:
                            _logger.LogInformation("Using LiteNetLib UDP transport");
                            rpcBuilder.UseLiteNetLib();
                            TransportType = "litenetlib"; // Ensure we always have a value
                            break;
                    }
                })
                .ConfigureServices(services =>
                {
                    services.AddSerializer(serializer =>
                    {
                        serializer.AddAssembly(typeof(IGameRpcGrain).Assembly);
                    });
                })
                .Build();
                
            await hostBuilder.StartAsync();
            
            _rpcHost = hostBuilder;
            _rpcClient = hostBuilder.Services.GetRequiredService<Granville.Rpc.IRpcClient>();
            
            // Wait for handshake and manifest exchange to complete with retry logic
            _logger.LogInformation("[ZONE_TRANSITION] Waiting for RPC handshake...");
            
            int retryCount = 0;
            const int maxRetries = 3;
            Exception? lastException = null;
            
            while (retryCount < maxRetries)
            {
                await Task.Delay(500 + (300 * retryCount)); // Progressive delay: 500ms, 800ms, 1100ms
                
                try
                {
                    // Get the game grain
                    _gameGrain = _rpcClient.GetGrain<IGameRpcGrain>("game");
                    _logger.LogInformation("[ZONE_TRANSITION] Successfully obtained game grain on attempt {Attempt}", retryCount + 1);
                    break;
                }
                catch (ArgumentException ex) when (ex.Message.Contains("Could not find an implementation"))
                {
                    lastException = ex;
                    retryCount++;
                    if (retryCount < maxRetries)
                    {
                        _logger.LogWarning("[ZONE_TRANSITION] Manifest not ready, retry {Retry}/{Max}", retryCount, maxRetries);
                    }
                    else
                    {
                        _logger.LogError(ex, "[ZONE_TRANSITION] Failed to get game grain after {Max} retries", maxRetries);
                        throw;
                    }
                }
            }
            
            // Reconnect player
            if (_gameGrain == null)
            {
                _logger.LogError("[ZONE_TRANSITION] Game grain is null after all retries");
                return;
            }
            
            _logger.LogInformation("Calling ConnectPlayer for {PlayerId} on new server", PlayerId);
            var result = await _gameGrain.ConnectPlayer(PlayerId!);
            _logger.LogInformation("ConnectPlayer returned: {Result}", result);
            
            if (result != "SUCCESS")
            {
                _logger.LogError("Failed to reconnect player {PlayerId} to new server", PlayerId);
                return;
            }
        }
        
        IsConnected = true;
        
        // Test the connection with a simple call before starting timers
        try
        {
            _logger.LogInformation("Testing connection with GetWorldState call");
            var testState = _gameGrain != null ? await _gameGrain.GetWorldState() : null;
            _logger.LogInformation("Test GetWorldState succeeded, got {Count} entities", testState?.Entities?.Count ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test GetWorldState failed after reconnection");
            IsConnected = false;
            return;
        }
        
        // Restore player velocity after zone transition
        if (_lastInputDirection.Length() > 0)
        {
            try
            {
                _logger.LogInformation("[ZONE_TRANSITION] Restoring player velocity: direction={Direction}, shooting={Shooting}", 
                    _lastInputDirection, _lastInputShooting);
                await _gameGrain!.UpdatePlayerInput(PlayerId!, _lastInputDirection, _lastInputShooting);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ZONE_TRANSITION] Failed to restore player velocity");
            }
        }
        
        // Restart timers with minimal initial delay for smooth transition
        _worldStateTimer = new Timer(async _ => await PollWorldState(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(16));
        _heartbeatTimer = new Timer(async _ => await SendHeartbeat(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
        _availableZonesTimer = new Timer(async _ => await PollAvailableZones(), null, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(2));
        
        // Restart chat polling if it was active
        if (_observer == null)
        {
            StartChatPolling();
        }
        
        // Reset sequence number for new server
        _lastSequenceNumber = -1;
        
        // Notify about server change
        ServerChanged?.Invoke(CurrentServerId);
        
        _logger.LogInformation("Successfully reconnected to new server {ServerId}", CurrentServerId);
        
        // Mark this pre-established connection as recently used to prevent cleanup
        var currentZoneKey = $"{_currentZone.X},{_currentZone.Y}";
        if (_preEstablishedConnections.TryGetValue(currentZoneKey, out var conn))
        {
            conn.EstablishedAt = DateTime.UtcNow; // Reset the timestamp to prevent immediate cleanup
        }
    }
    
    private async Task PreEstablishNeighborConnections(List<GridSquare> zones)
    {
        if (_currentZone == null || zones == null || zones.Count == 0)
        {
            return;
        }
        
        _logger.LogDebug("Pre-establishing connections for current zone ({X},{Y}). Current connections: {Count} [{Keys}]", 
            _currentZone.X, _currentZone.Y, _preEstablishedConnections.Count, string.Join(", ", _preEstablishedConnections.Keys));
        
        // Skip health check - it's causing too many GetAvailableZones requests
        // await CheckPreEstablishedConnectionHealth();
        
        // Get player position from last world state
        var playerEntity = _lastWorldState?.Entities?.FirstOrDefault(e => e.EntityId == PlayerId);
        if (playerEntity == null)
        {
            _logger.LogDebug("No player position available for distance-based connections");
            return;
        }
        
        // Get server information for each zone
        var siloUrl = _configuration["SiloUrl"] ?? "https://localhost:7071/";
        if (!siloUrl.EndsWith("/")) siloUrl += "/";
        
        try
        {
            var response = await _httpClient.GetFromJsonAsync<List<Shooter.Shared.Models.ActionServerInfo>>(
                $"{siloUrl}api/world/action-servers");
            
            if (response == null) return;
            
            // Create a map of zones to servers
            var zoneToServer = response.ToDictionary(s => s.AssignedSquare, s => s);
            
            // Check each available zone to see if we're within 150 units
            var zonesWithinRange = new List<GridSquare>();
            
            foreach (var zone in zones)
            {
                // Calculate distance from player to zone
                var (min, max) = zone.GetBounds();
                float distToZone = 0;
                
                // X-axis distance
                if (playerEntity.Position.X < min.X)
                    distToZone = Math.Max(distToZone, min.X - playerEntity.Position.X);
                else if (playerEntity.Position.X > max.X)
                    distToZone = Math.Max(distToZone, playerEntity.Position.X - max.X);
                
                // Y-axis distance
                if (playerEntity.Position.Y < min.Y)
                    distToZone = Math.Max(distToZone, min.Y - playerEntity.Position.Y);
                else if (playerEntity.Position.Y > max.Y)
                    distToZone = Math.Max(distToZone, playerEntity.Position.Y - max.Y);
                
                // Include zones within 150 units
                if (distToZone <= 150)
                {
                    zonesWithinRange.Add(zone);
                    _logger.LogDebug("Zone ({X},{Y}) is within range - distance: {Distance} units", 
                        zone.X, zone.Y, distToZone);
                }
            }
            
            // Establish connections to zones within range
            var connectionStatus = new Dictionary<GridSquare, bool>();
            
            foreach (var zone in zonesWithinRange)
            {
                if (zoneToServer.TryGetValue(zone, out var serverInfo))
                {
                    // Check if we already have a pre-established connection
                    var connectionKey = $"{zone.X},{zone.Y}";
                    
                    if (!_preEstablishedConnections.ContainsKey(connectionKey))
                    {
                        // Pre-establish connection to this server
                        _logger.LogInformation("Pre-establishing connection to zone ({X},{Y}) server {ServerId}", 
                            zone.X, zone.Y, serverInfo.ServerId);
                        
                        var success = await EstablishConnection(connectionKey, serverInfo);
                        connectionStatus[zone] = success;
                    }
                    else
                    {
                        _logger.LogDebug("Already have pre-established connection to zone {Key}, connected: {IsConnected}", 
                            connectionKey, _preEstablishedConnections[connectionKey].IsConnected);
                        connectionStatus[zone] = _preEstablishedConnections[connectionKey].IsConnected;
                    }
                }
            }
            
            // Clean up connections to zones that are out of range
            var validKeys = zonesWithinRange.Select(z => $"{z.X},{z.Y}").ToHashSet();
            validKeys.Add($"{_currentZone.X},{_currentZone.Y}"); // Always keep current zone connection
            
            var keysToRemove = _preEstablishedConnections
                .Where(kvp => !validKeys.Contains(kvp.Key))
                .Select(kvp => kvp.Key)
                .ToList();
            
            if (keysToRemove.Count > 0)
            {
                _logger.LogInformation("Cleaning up {Count} old pre-established connections: {Keys}", 
                    keysToRemove.Count, string.Join(", ", keysToRemove));
            }
            
            // Log current pre-connection status
            _logger.LogInformation("Pre-connection status after update: Total={Count}, Zones={Zones}", 
                _preEstablishedConnections.Count, 
                string.Join(", ", _preEstablishedConnections.Select(kvp => $"{kvp.Key}:{(kvp.Value.IsConnected ? "OK" : "DEAD")}")));
            
            foreach (var key in keysToRemove)
            {
                await CleanupPreEstablishedConnection(key);
            }
            
            // Notify about ALL pre-established connections
            var allConnectionStatus = new Dictionary<string, (bool isConnected, bool isNeighbor, bool isConnecting)>();
            
            foreach (var kvp in _preEstablishedConnections)
            {
                // All connections are now based on distance, so they're all "neighbors"
                allConnectionStatus[kvp.Key] = (kvp.Value.IsConnected, true, kvp.Value.IsConnecting);
            }
            
            PreEstablishedConnectionsUpdated?.Invoke(allConnectionStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pre-establish neighbor connections");
        }
    }
    
    private List<GridSquare> GetNeighboringZones(GridSquare currentZone)
    {
        var neighbors = new List<GridSquare>();
        
        // Get player position from last world state
        var playerEntity = _lastWorldState?.Entities?.FirstOrDefault(e => e.EntityId == PlayerId);
        if (playerEntity == null)
        {
            // If we don't have player position, return all 8 neighbors as before
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue; // Skip current zone
                    neighbors.Add(new GridSquare(currentZone.X + dx, currentZone.Y + dy));
                }
            }
            return neighbors;
        }
        
        // Check each potential neighboring zone
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue; // Skip current zone
                
                var neighborZone = new GridSquare(currentZone.X + dx, currentZone.Y + dy);
                var (min, max) = neighborZone.GetBounds();
                
                // Calculate distance from player to the nearest edge of the neighboring zone
                float distToZone = 0;
                
                // X-axis distance
                if (playerEntity.Position.X < min.X)
                    distToZone = Math.Max(distToZone, min.X - playerEntity.Position.X);
                else if (playerEntity.Position.X > max.X)
                    distToZone = Math.Max(distToZone, playerEntity.Position.X - max.X);
                
                // Y-axis distance
                if (playerEntity.Position.Y < min.Y)
                    distToZone = Math.Max(distToZone, min.Y - playerEntity.Position.Y);
                else if (playerEntity.Position.Y > max.Y)
                    distToZone = Math.Max(distToZone, playerEntity.Position.Y - max.Y);
                
                // Include zones within peek distance (200 units) to ensure smooth transitions
                const float PRE_CONNECTION_DISTANCE = 200f; // Match peek distance
                if (distToZone <= PRE_CONNECTION_DISTANCE)
                {
                    neighbors.Add(neighborZone);
                    _logger.LogDebug("Including neighbor zone ({X},{Y}) - distance: {Distance} units", 
                        neighborZone.X, neighborZone.Y, distToZone);
                }
                else
                {
                    _logger.LogDebug("Excluding neighbor zone ({X},{Y}) - distance: {Distance} units (> {MaxDist})", 
                        neighborZone.X, neighborZone.Y, distToZone, PRE_CONNECTION_DISTANCE);
                }
            }
        }
        
        return neighbors;
    }
    
    private async Task CheckPreEstablishedConnectionHealth()
    {
        var keysToCheck = _preEstablishedConnections.Keys.ToList();
        
        foreach (var key in keysToCheck)
        {
            if (_preEstablishedConnections.TryGetValue(key, out var connection))
            {
                try
                {
                    // Send a lightweight request to check if connection is alive
                    var zones = await connection.GameGrain!.GetAvailableZones();
                    connection.IsConnected = true;
                    connection.EstablishedAt = DateTime.UtcNow; // Update last successful check time
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Pre-established connection {Key} health check failed: {Error}", key, ex.Message);
                    connection.IsConnected = false;
                    
                    // If it's been disconnected for too long, remove it
                    if ((DateTime.UtcNow - connection.EstablishedAt).TotalMinutes > 1)
                    {
                        _logger.LogInformation("Removing stale pre-established connection {Key}", key);
                        await CleanupPreEstablishedConnection(key);
                    }
                }
            }
        }
    }
    
    private async Task<bool> EstablishConnection(string connectionKey, Shooter.Shared.Models.ActionServerInfo serverInfo)
    {
        try
        {
            var serverHost = serverInfo.IpAddress;
            var rpcPort = serverInfo.RpcPort;
            
            if (rpcPort == 0)
            {
                _logger.LogError("Pre-establish: ActionServer did not report RPC port");
                return false;
            }
            
            // Create and add connection in connecting state
            var connection = new PreEstablishedConnection
            {
                ServerInfo = serverInfo,
                EstablishedAt = DateTime.UtcNow,
                IsConnected = false,
                IsConnecting = true
            };
            
            _preEstablishedConnections[connectionKey] = connection;
            
            // Notify UI about connecting state
            NotifyConnectionsUpdated();
            
            // Resolve hostname to IP address if needed
            string resolvedHost = serverHost;
            try
            {
                if (!System.Net.IPAddress.TryParse(serverHost, out _))
                {
                    var hostEntry = await System.Net.Dns.GetHostEntryAsync(serverHost);
                    var ipAddress = hostEntry.AddressList.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                    if (ipAddress != null)
                    {
                        resolvedHost = ipAddress.ToString();
                    }
                    else
                    {
                        resolvedHost = "127.0.0.1";
                    }
                }
            }
            catch
            {
                resolvedHost = "127.0.0.1";
            }
            
            _logger.LogInformation("Pre-establishing RPC connection to {Host}:{Port} for zone {Key}", 
                resolvedHost, rpcPort, connectionKey);
            
            // Create RPC client
            var hostBuilder = Host.CreateDefaultBuilder()
                .UseOrleansRpcClient(rpcBuilder =>
                {
                    rpcBuilder.ConnectTo(resolvedHost, rpcPort);
                    // Configure transport based on configuration
                    var transportType = _configuration["RpcTransport"] ?? "litenetlib";
                    TransportType = transportType.ToLowerInvariant();
                    switch (TransportType)
                    {
                        case "ruffles":
                            _logger.LogInformation("Using Ruffles UDP transport");
                            rpcBuilder.UseRuffles();
                            break;
                        case "litenetlib":
                        default:
                            _logger.LogInformation("Using LiteNetLib UDP transport");
                            rpcBuilder.UseLiteNetLib();
                            TransportType = "litenetlib"; // Ensure we always have a value
                            break;
                    }
                })
                .ConfigureServices(services =>
                {
                    services.AddSerializer(serializer =>
                    {
                        serializer.AddAssembly(typeof(IGameRpcGrain).Assembly);
                    });
                })
                .Build();
                
            await hostBuilder.StartAsync();
            
            connection.RpcHost = hostBuilder;
            connection.RpcClient = hostBuilder.Services.GetRequiredService<Granville.Rpc.IRpcClient>();
            
            // Brief delay for handshake
            await Task.Delay(200);
            
            // Retry logic for getting the game grain
            int retryCount = 0;
            const int maxRetries = 3;
            
            while (retryCount < maxRetries)
            {
                try
                {
                    // Get the game grain
                    connection.GameGrain = connection.RpcClient.GetGrain<IGameRpcGrain>("game");
                    
                    // Test the connection
                    var testState = await connection.GameGrain.GetWorldState();
                    connection.IsConnected = true;
                    
                    _logger.LogInformation("Pre-established connection to zone {Key} successful", connectionKey);
                    break;
                }
                catch (ArgumentException ex) when (ex.Message.Contains("Could not find an implementation"))
                {
                    retryCount++;
                    if (retryCount < maxRetries)
                    {
                        _logger.LogWarning("Pre-establish: Manifest not ready for zone {Key}, retry {Retry}/{Max}", 
                            connectionKey, retryCount, maxRetries);
                        await Task.Delay(300 * retryCount); // Progressive delay
                    }
                    else
                    {
                        _logger.LogError(ex, "Failed to get game grain for pre-established connection to zone {Key} after {Max} retries", 
                            connectionKey, maxRetries);
                        connection.IsConnected = false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to test pre-established connection to zone {Key} - server may have disconnected", connectionKey);
                    connection.IsConnected = false;
                    break;
                }
            }
            
            connection.IsConnecting = false;
            _preEstablishedConnections[connectionKey] = connection;
            
            // Notify UI about connection state change
            NotifyConnectionsUpdated();
            
            return connection.IsConnected;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to establish connection to zone {Key}", connectionKey);
            
            // Clear connecting state on failure
            if (_preEstablishedConnections.TryGetValue(connectionKey, out var conn))
            {
                conn.IsConnecting = false;
                NotifyConnectionsUpdated();
            }
            
            return false;
        }
    }
    
    private void NotifyConnectionsUpdated()
    {
        var allConnectionStatus = new Dictionary<string, (bool isConnected, bool isNeighbor, bool isConnecting)>();
        
        foreach (var kvp in _preEstablishedConnections)
        {
            // All connections are now based on distance, so they're all "neighbors"
            allConnectionStatus[kvp.Key] = (kvp.Value.IsConnected, true, kvp.Value.IsConnecting);
        }
        
        PreEstablishedConnectionsUpdated?.Invoke(allConnectionStatus);
    }
    
    private async Task CleanupPreEstablishedConnection(string connectionKey)
    {
        if (_preEstablishedConnections.TryGetValue(connectionKey, out var connection))
        {
            _logger.LogInformation("Cleaning up pre-established connection to zone {Key}", connectionKey);
            
            try
            {
                if (connection.RpcHost != null)
                {
                    await connection.RpcHost.StopAsync(TimeSpan.FromSeconds(1));
                    connection.RpcHost.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing pre-established connection for zone {Key}", connectionKey);
            }
            
            _preEstablishedConnections.Remove(connectionKey);
        }
    }
    
    private async Task<List<EntityState>> FetchAdjacentZoneEntities()
    {
        var adjacentEntities = new List<EntityState>();
        
        // Get player position from last world state
        var playerEntity = _lastWorldState?.Entities?.FirstOrDefault(e => e.EntityId == PlayerId);
        if (playerEntity == null || _currentZone == null)
        {
            return adjacentEntities;
        }
        
        // Check if player is within 100 units of any zone border
        var (min, max) = _currentZone.GetBounds();
        var pos = playerEntity.Position;
        
        _logger.LogTrace("Current zone ({X},{Y}) bounds: ({MinX},{MinY}) to ({MaxX},{MaxY}), player at {Position}", 
            _currentZone.X, _currentZone.Y, min.X, min.Y, max.X, max.Y, pos);
        
        // Use zone peek distance for consistent visibility
        const float ZONE_PEEK_DISTANCE = 200f; // Match GameConstants.ZonePeekDistance
        
        bool nearLeftEdge = pos.X <= min.X + ZONE_PEEK_DISTANCE;
        bool nearRightEdge = pos.X >= max.X - ZONE_PEEK_DISTANCE;
        bool nearTopEdge = pos.Y <= min.Y + ZONE_PEEK_DISTANCE;
        bool nearBottomEdge = pos.Y >= max.Y - ZONE_PEEK_DISTANCE;
        
        if (!nearLeftEdge && !nearRightEdge && !nearTopEdge && !nearBottomEdge)
        {
            // Player is not near any borders
            return adjacentEntities;
        }
        
        _logger.LogTrace("Player at {Position} near borders - L:{Left} R:{Right} T:{Top} B:{Bottom}", 
            pos, nearLeftEdge, nearRightEdge, nearTopEdge, nearBottomEdge);
        
        // Determine which zones to fetch from based on player position
        var zonesToFetch = new HashSet<GridSquare>();
        
        if (nearLeftEdge)
        {
            zonesToFetch.Add(new GridSquare(_currentZone.X - 1, _currentZone.Y));
            
            // Check corners
            if (nearTopEdge)
                zonesToFetch.Add(new GridSquare(_currentZone.X - 1, _currentZone.Y - 1));
            if (nearBottomEdge)
                zonesToFetch.Add(new GridSquare(_currentZone.X - 1, _currentZone.Y + 1));
        }
        
        if (nearRightEdge)
        {
            zonesToFetch.Add(new GridSquare(_currentZone.X + 1, _currentZone.Y));
            
            // Check corners
            if (nearTopEdge)
                zonesToFetch.Add(new GridSquare(_currentZone.X + 1, _currentZone.Y - 1));
            if (nearBottomEdge)
                zonesToFetch.Add(new GridSquare(_currentZone.X + 1, _currentZone.Y + 1));
        }
        
        if (nearTopEdge)
        {
            zonesToFetch.Add(new GridSquare(_currentZone.X, _currentZone.Y - 1));
        }
        
        if (nearBottomEdge)
        {
            zonesToFetch.Add(new GridSquare(_currentZone.X, _currentZone.Y + 1));
        }
        
        // Log zones we want to fetch from
        _logger.LogTrace("Want to fetch entities from zones: {Zones}", 
            string.Join(", ", zonesToFetch.Select(z => $"({z.X},{z.Y})"))); 
        
        // Log available pre-established connections
        _logger.LogTrace("Available pre-established connections: {Connections}", 
            string.Join(", ", _preEstablishedConnections.Select(kvp => $"{kvp.Key}:{(kvp.Value.IsConnected ? "Connected" : "Disconnected")}")));
        
        // Fetch entities from each adjacent zone using pre-established connections
        foreach (var zone in zonesToFetch)
        {
            var connectionKey = $"{zone.X},{zone.Y}";
            
            if (_preEstablishedConnections.TryGetValue(connectionKey, out var connection) && 
                connection.IsConnected && 
                connection.GameGrain != null)
            {
                try
                {
                    _logger.LogTrace("Fetching entities from zone ({X},{Y}) via pre-established connection", zone.X, zone.Y);
                    
                    // Use GetLocalWorldState to avoid recursive fetching on the server
                    var worldState = await connection.GameGrain.GetLocalWorldState();
                    if (worldState?.Entities != null && _currentZone != null)
                    {
                        // Get bounds of current zone for filtering
                        var (currentMin, currentMax) = _currentZone.GetBounds();
                        
                        _logger.LogTrace("Filtering entities from zone ({X},{Y}). Current zone bounds: ({MinX},{MinY}) to ({MaxX},{MaxY})", 
                            zone.X, zone.Y, currentMin.X, currentMin.Y, currentMax.X, currentMax.Y);
                        
                        // Log total entities before filtering
                        var totalEntities = worldState.Entities.Count;
                        _logger.LogTrace("Zone ({X},{Y}) has {Count} total entities before filtering", zone.X, zone.Y, totalEntities);
                        
                        // Filter to only include entities within 100 units of our zone
                        var filteredEntities = worldState.Entities.Where(e => 
                        {
                            // Check if entity is within 100 units of the shared border
                            var entityZone = GridSquare.FromPosition(e.Position);
                            
                            // Entity must be in the adjacent zone
                            if (entityZone.X != zone.X || entityZone.Y != zone.Y)
                                return false;
                            
                            // Check distance to our zone
                            // Zone (0,0) is to the left of zone (1,0), so entities near X=500 (right edge of 0,0) should be included
                            if (zone.X < _currentZone.X && e.Position.X >= currentMin.X - ZONE_PEEK_DISTANCE) return true; // Left zone, entities near our left edge
                            if (zone.X > _currentZone.X && e.Position.X <= currentMax.X + ZONE_PEEK_DISTANCE) return true; // Right zone, entities near our right edge
                            if (zone.Y < _currentZone.Y && e.Position.Y >= currentMin.Y - ZONE_PEEK_DISTANCE) return true; // Top zone, entities near our top edge
                            if (zone.Y > _currentZone.Y && e.Position.Y <= currentMax.Y + ZONE_PEEK_DISTANCE) return true; // Bottom zone, entities near our bottom edge
                            
                            // For corner zones, check both axes
                            if (zone.X != _currentZone.X && zone.Y != _currentZone.Y)
                            {
                                bool xInRange = (zone.X < _currentZone.X && e.Position.X >= currentMin.X - ZONE_PEEK_DISTANCE) ||
                                               (zone.X > _currentZone.X && e.Position.X <= currentMax.X + ZONE_PEEK_DISTANCE);
                                bool yInRange = (zone.Y < _currentZone.Y && e.Position.Y >= currentMin.Y - ZONE_PEEK_DISTANCE) ||
                                               (zone.Y > _currentZone.Y && e.Position.Y <= currentMax.Y + ZONE_PEEK_DISTANCE);
                                return xInRange && yInRange;
                            }
                            
                            return false;
                        }).ToList();
                        
                        adjacentEntities.AddRange(filteredEntities);
                        
                        if (filteredEntities.Any())
                        {
                            _logger.LogTrace("Fetched {Count} entities from zone ({X},{Y}) via pre-established connection", 
                                filteredEntities.Count, zone.X, zone.Y);
                            
                            // Log details of fetched entities
                            var entityTypes = filteredEntities.GroupBy(e => e.Type)
                                .ToDictionary(g => g.Key, g => g.Count());
                            _logger.LogDebug("Fetched entities from zone ({X},{Y}): Players={Players}, Enemies={Enemies}, Bullets={Bullets}, Factories={Factories}",
                                zone.X, zone.Y,
                                entityTypes.GetValueOrDefault(EntityType.Player, 0),
                                entityTypes.GetValueOrDefault(EntityType.Enemy, 0),
                                entityTypes.GetValueOrDefault(EntityType.Bullet, 0),
                                entityTypes.GetValueOrDefault(EntityType.Factory, 0));
                        }
                        else
                        {
                            _logger.LogTrace("No entities from zone ({X},{Y}) were within 100 units of shared border", zone.X, zone.Y);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogTrace("Failed to fetch entities from zone ({X},{Y}): {Error}", 
                        zone.X, zone.Y, ex.Message);
                }
            }
            else
            {
                _logger.LogTrace("No pre-established connection for zone ({X},{Y}) - connection exists: {Exists}, connected: {Connected}", 
                    zone.X, zone.Y, 
                    _preEstablishedConnections.ContainsKey(connectionKey),
                    _preEstablishedConnections.TryGetValue(connectionKey, out var c) && c.IsConnected);
            }
        }
        
        return adjacentEntities;
    }
    
    private void Cleanup()
    {
        IsConnected = false;
        TransportType = null;
        _lastSequenceNumber = -1;
        
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
        
        _worldStateTimer?.Dispose();
        _heartbeatTimer?.Dispose();
        _availableZonesTimer?.Dispose();
        _chatPollingTimer?.Dispose();
        _worldStateTimer = null;
        _heartbeatTimer = null;
        _availableZonesTimer = null;
        _chatPollingTimer = null;
        
        _gameGrain = null;
        _rpcClient = null;
        
        // Dispose of the RPC host
        if (_rpcHost != null)
        {
            try
            {
                _rpcHost.StopAsync(TimeSpan.FromSeconds(1)).Wait();
                _rpcHost.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing RPC host");
            }
            _rpcHost = null;
        }
        
        // Don't clean up pre-established connections during transitions
        // They will be cleaned up during periodic maintenance
        // Only clean them up if this is a final dispose (see Dispose method)
        
        // Don't clear PlayerId as we need it for reconnection
        // PlayerId = null;
        CurrentServerId = null;
        _currentZone = null;
    }
    
    // Observer update methods
    public void UpdateZoneStats(ZoneStatistics stats)
    {
        ZoneStatsUpdated?.Invoke(stats);
    }
    
    public void UpdateAvailableZones(List<GridSquare> availableZones)
    {
        _cachedAvailableZones = availableZones;
        AvailableZonesUpdated?.Invoke(availableZones);
    }
    
    public void UpdateAdjacentEntities(Dictionary<string, List<EntityState>> entitiesByZone)
    {
        _cachedAdjacentEntities = entitiesByZone;
        // Merge with current world state if needed
    }
    
    public void HandleScoutAlert(ScoutAlert alert)
    {
        ScoutAlertReceived?.Invoke(alert);
    }
    
    public async Task<double> GetServerFpsAsync()
    {
        if (_gameGrain == null || !IsConnected)
        {
            return 0;
        }
        
        try
        {
            return await _gameGrain.GetServerFps();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error getting server FPS");
            return 0;
        }
    }
    
    public void Dispose()
    {
        Cleanup();
        
        // Clean up all pre-established connections on final dispose
        var allKeys = _preEstablishedConnections.Keys.ToList();
        foreach (var key in allKeys)
        {
            _ = Task.Run(async () => await CleanupPreEstablishedConnection(key));
        }
        _preEstablishedConnections.Clear();
        
        PlayerId = null;  // Clear PlayerId only on final dispose
    }
    
    public void HandleGameOver(GameOverMessage gameOverMessage)
    {
        _logger.LogInformation("Game Over! Final scores:");
        foreach (var score in gameOverMessage.PlayerScores.OrderBy(s => s.RespawnCount))
        {
            _logger.LogInformation("  {PlayerName}: {RespawnCount} deaths", score.PlayerName, score.RespawnCount);
        }
        
        // Notify UI about game over
        GameOverReceived?.Invoke(gameOverMessage);
    }
    
    public void HandleGameRestarted()
    {
        _logger.LogInformation("Game restarted! New round beginning.");
        
        // Notify UI about game restart
        GameRestartedReceived?.Invoke();
    }
    
    public void HandleChatMessage(ChatMessage message)
    {
        _logger.LogInformation("Received chat message from {Sender}: {Message}", 
            message.SenderName, message.Message);
        
        // Notify UI about chat message
        ChatMessageReceived?.Invoke(message);
    }
    
    public async Task SendChatMessage(string message)
    {
        if (!IsConnected || _gameGrain == null)
        {
            _logger.LogWarning("[CHAT_DEBUG] Cannot send chat message - not connected to game. IsConnected={IsConnected}, GameGrain={GameGrain}", IsConnected, _gameGrain != null);
            return;
        }

        var chatMessage = new ChatMessage(
            SenderId: PlayerId ?? "Unknown",
            SenderName: PlayerName ?? "Unknown",
            Message: message,
            Timestamp: DateTime.UtcNow,
            IsSystemMessage: false
        );

        try
        {
            _logger.LogInformation("[CHAT_DEBUG] Attempting to send chat message from {PlayerName} ({PlayerId}): {Message}", PlayerName, PlayerId, message);
            await _gameGrain.SendChatMessage(chatMessage);
            _logger.LogInformation("[CHAT_DEBUG] Successfully sent chat message to RPC grain: {Message}", message);
            
            // As a workaround, always handle the message locally to ensure the sender sees their own message
            // This is necessary because observer pattern might not be supported by the RPC transport
            _logger.LogInformation("[CHAT_DEBUG] Handling chat message locally to ensure sender sees it");
            HandleChatMessage(chatMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CHAT_DEBUG] Failed to send chat message: {Message}", message);
            throw; // Re-throw so the UI can show an error
        }
    }
    
    private void StartChatPolling()
    {
        if (_chatPollingTimer != null)
        {
            _logger.LogDebug("[CHAT_DEBUG] Chat polling already started");
            return;
        }
        
        _logger.LogInformation("[CHAT_DEBUG] Starting chat polling fallback");
        _lastChatPollTime = DateTime.UtcNow;
        _chatPollingTimer = new Timer(async _ => await PollChatMessages(), null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
    }
    
    private async Task PollChatMessages()
    {
        if (_gameGrain == null || !IsConnected || _cancellationTokenSource?.Token.IsCancellationRequested == true)
        {
            return;
        }
        
        try
        {
            var messages = await _gameGrain.GetRecentChatMessages(_lastChatPollTime);
            if (messages != null && messages.Count > 0)
            {
                _logger.LogInformation("[CHAT_DEBUG] Received {Count} chat messages from polling", messages.Count);
                
                foreach (var message in messages)
                {
                    // Don't show our own messages again (we already added them locally)
                    if (message.SenderId != PlayerId)
                    {
                        HandleChatMessage(message);
                    }
                }
                
                // Update poll time to the latest message timestamp
                if (messages.Count > 0)
                {
                    _lastChatPollTime = messages.Max(m => m.Timestamp);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[CHAT_DEBUG] Failed to poll chat messages");
        }
    }
}

// Response types from Silo HTTP endpoints - using models from Shooter.Shared
public record PlayerRegistrationResponse(Shooter.Shared.Models.PlayerInfo PlayerInfo, Shooter.Shared.Models.ActionServerInfo ActionServer);

// Pre-established connection tracking
internal class PreEstablishedConnection
{
    public IHost? RpcHost { get; set; }
    public Granville.Rpc.IRpcClient? RpcClient { get; set; }
    public IGameRpcGrain? GameGrain { get; set; }
    public Shooter.Shared.Models.ActionServerInfo ServerInfo { get; set; } = null!;
    public DateTime EstablishedAt { get; set; }
    public bool IsConnected { get; set; }
    public bool IsConnecting { get; set; }
}

