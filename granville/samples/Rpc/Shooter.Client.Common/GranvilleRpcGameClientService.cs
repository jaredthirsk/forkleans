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
using System.Threading;
using System.Collections.Concurrent;

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
    private Timer? _networkStatsTimer;
    private Timer? _watchdogTimer;
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
    private string? _currentTransitionTarget = null; // Track zone transition target for deduplication
    private long _lastSequenceNumber = -1;
    private readonly ConcurrentDictionary<string, PreEstablishedConnection> _preEstablishedConnections = new();
    private GridSquare? _currentZone = null;
    private WorldState? _lastWorldState = null;
    private Vector2 _lastInputDirection = Vector2.Zero;
    private bool _lastInputShooting = false;
    private readonly HashSet<string> _visitedZones = new();
    private readonly Queue<(string zoneKey, DateTime visitTime)> _recentlyVisitedZones = new();
    private bool _worldStateErrorLogged = false;
    private DateTime _lastWorldStateError = DateTime.MinValue;
    private bool _playerInputErrorLogged = false;
    private DateTime _lastPlayerInputError = DateTime.MinValue;
    private GameRpcObserver? _observer;
    private List<GridSquare> _cachedAvailableZones = new();
    private Dictionary<string, List<EntityState>> _cachedAdjacentEntities = new();
    private NetworkStatistics? _latestNetworkStats = null;
    private NetworkStatisticsTracker? _clientNetworkTracker = null;
    
    // Connection distance thresholds with hysteresis
    private const float CONNECTION_CREATE_DISTANCE = 200f;    // Create connections when within this distance
    private const float CONNECTION_DISPOSE_DISTANCE = 400f;   // Dispose connections when beyond this distance
    private const float ADJACENT_ENTITY_FETCH_DISTANCE = 200f; // Fetch entities when near zone edge
    
    public event Action<WorldState>? WorldStateUpdated;
    public event Action<string>? ServerChanged;
    public event Action<List<GridSquare>>? AvailableZonesUpdated;
    public event Action<Dictionary<string, (bool isConnected, bool isNeighbor, bool isConnecting)>>? PreEstablishedConnectionsUpdated;
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
            
            PlayerId = registrationResponse.PlayerInfo?.PlayerId;
            PlayerName = registrationResponse.PlayerInfo?.Name ?? playerName;
            CurrentServerId = registrationResponse.ActionServer?.ServerId ?? "Unknown";
            _currentZone = registrationResponse.ActionServer?.AssignedSquare;
            
            // Validate PlayerId
            if (string.IsNullOrEmpty(PlayerId))
            {
                _logger.LogError("Registration failed: PlayerId is null or empty");
                return false;
            }
            
            // Mark initial zone as visited
            if (_currentZone != null)
            {
                TrackVisitedZone($"{_currentZone.X},{_currentZone.Y}");
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
            
            // Create RPC client using helper method
            var hostBuilder = BuildRpcHost(resolvedHost, rpcPort, PlayerId);
                
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
            const int maxRetries = 15; // Increased for better manifest reliability
            const int retryDelayMs = 50; // Reduced delay - manifest sync is usually very fast
            
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
            
            // Extra safety check - should never happen after validation above
            if (string.IsNullOrEmpty(PlayerId))
            {
                _logger.LogError("Cannot connect: PlayerId is null or empty");
                return false;
            }
            
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
            _worldStateTimer = new Timer(async _ => 
            {
                try
                {
                    await PollWorldState();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[TIMER_CALLBACK] Unhandled exception in world state timer callback. ExceptionType={ExceptionType}, State: IsConnected={IsConnected}, IsTransitioning={IsTransitioning}, ThreadId={ThreadId}, GameGrainNull={GameGrainNull}, CancellationRequested={CancellationRequested}",
                        ex.GetType().FullName, IsConnected, _isTransitioning, Thread.CurrentThread.ManagedThreadId, _gameGrain == null, _cancellationTokenSource?.Token.IsCancellationRequested ?? true);
                }
            }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(33)); // Reduced from 16ms to 33ms (30 FPS)
            
            // Start heartbeat
            _heartbeatTimer = new Timer(async _ => 
            {
                try
                {
                    await SendHeartbeat();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception in heartbeat timer callback");
                }
            }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
            
            // Start polling for available zones - reduced frequency for better performance
            _availableZonesTimer = new Timer(async _ => 
            {
                try
                {
                    await PollAvailableZones();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception in available zones timer callback");
                }
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(10));
            
            // Start polling for network stats (since observer might not be supported)
            _networkStatsTimer = new Timer(async _ => 
            {
                try
                {
                    await PollNetworkStats();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception in network stats timer callback");
                }
            }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            
            // Start watchdog timer to monitor polling health
            _watchdogTimer = new Timer(_ => CheckPollingHealth(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            
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
            
            // Now that Vector2 is supported in secure binary serialization,
            // we can use the original method
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
            _logger.LogTrace("PollWorldState skipped: GameGrain={GameGrain}, IsConnected={IsConnected}, IsCancelled={IsCancelled}, IsTransitioning={IsTransitioning}",
                _gameGrain != null, IsConnected, _cancellationTokenSource?.Token.IsCancellationRequested ?? true, _isTransitioning);
            return;
        }
        
        _logger.LogTrace("PollWorldState executing for player {PlayerId}", PlayerId);
        
        try
        {
            _lastWorldStatePollTime = DateTime.UtcNow;
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
                        
                        bool nearLeftEdge = pos.X <= min.X + ADJACENT_ENTITY_FETCH_DISTANCE;
                        bool nearRightEdge = pos.X >= max.X - ADJACENT_ENTITY_FETCH_DISTANCE;
                        bool nearTopEdge = pos.Y <= min.Y + ADJACENT_ENTITY_FETCH_DISTANCE;
                        bool nearBottomEdge = pos.Y >= max.Y - ADJACENT_ENTITY_FETCH_DISTANCE;
                        
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
                            // Process zone changes immediately without debouncing
                            var now = DateTime.UtcNow;
                            var timeSinceLastChange = (now - _lastZoneChangeTime).TotalSeconds;
                            
                            if (_lastDetectedZone == null || 
                                _lastDetectedZone.X != playerZone.X || 
                                _lastDetectedZone.Y != playerZone.Y)
                            {
                                _lastZoneChangeTime = now;
                                _lastDetectedZone = playerZone;
                                
                                _logger.LogInformation("[CLIENT_ZONE_CHANGE] Player moved from zone ({OldX},{OldY}) to ({NewX},{NewY}) at position {Position}", 
                                    _currentZone.X, _currentZone.Y, playerZone.X, playerZone.Y, playerEntity.Position);
                                
                                // Schedule server transition check (don't use Task.Run in hot path)
                                _ = CheckForServerTransition();
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
                
                // Reset failure count on success
                _worldStatePollFailures = 0;
            }
            else
            {
                _logger.LogWarning("Received null world state from server");
                _worldStatePollFailures++;
            }
        }
        catch (Exception ex)
        {
            _worldStatePollFailures++;
            
            // Throttle error logging to avoid spamming logs during connection issues
            if (!_worldStateErrorLogged || (DateTime.UtcNow - _lastWorldStateError).TotalSeconds > 5)
            {
                _logger.LogError(ex, "[WORLD_STATE] Failed to get world state (failure count: {FailureCount}). ExceptionType={ExceptionType}, State: IsConnected={IsConnected}, IsTransitioning={IsTransitioning}, ThreadId={ThreadId}, GameGrainNull={GameGrainNull}, RpcClientNull={RpcClientNull}, CancellationRequested={CancellationRequested}, TimeSinceLastPoll={TimeSinceLastPoll}ms",
                    _worldStatePollFailures, ex.GetType().FullName, IsConnected, _isTransitioning, Thread.CurrentThread.ManagedThreadId, 
                    _gameGrain == null, _rpcClient == null, _cancellationTokenSource?.Token.IsCancellationRequested ?? true, 
                    (DateTime.UtcNow - _lastWorldStatePollTime).TotalMilliseconds);
                _worldStateErrorLogged = true;
                _lastWorldStateError = DateTime.UtcNow;
            }
            
            // If we have too many consecutive failures, attempt to reconnect
            if (_worldStatePollFailures >= 10)
            {
                _logger.LogWarning("Too many consecutive world state poll failures ({Count}), attempting reconnection", _worldStatePollFailures);
                _ = Task.Run(async () => await AttemptReconnection());
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
    
    private Task CheckPollingHealth()
    {
        try
        {
            var timeSinceLastPoll = DateTime.UtcNow - _lastWorldStatePollTime;
            
            if (timeSinceLastPoll.TotalSeconds > 10)
            {
                _logger.LogWarning("World state polling appears to have stopped. Last poll was {Seconds:F1} seconds ago", timeSinceLastPoll.TotalSeconds);
                
                // Check if we're stuck in transitioning state
                lock (_transitionLock)
                {
                    if (_isTransitioning && timeSinceLastPoll.TotalSeconds > 30)
                    {
                        _logger.LogWarning("[WATCHDOG] Stuck in transitioning state for {Seconds:F1} seconds, forcing reset. ThreadId={ThreadId}", 
                            timeSinceLastPoll.TotalSeconds, Thread.CurrentThread.ManagedThreadId);
                        _isTransitioning = false;
                    }
                }
                
                // Attempt to restart polling if it's been too long
                bool isCurrentlyTransitioning = false;
                lock (_transitionLock)
                {
                    isCurrentlyTransitioning = _isTransitioning;
                }
                
                if (timeSinceLastPoll.TotalSeconds > 15 && !isCurrentlyTransitioning)
                {
                    if (!IsConnected)
                    {
                        _logger.LogWarning("Polling stopped and client is disconnected. Attempting reconnection.");
                        _ = Task.Run(async () => await AttemptReconnection());
                    }
                    else
                    {
                        _logger.LogWarning("[WATCHDOG] Attempting to restart world state polling. ThreadId={ThreadId}, CurrentTimer={TimerState}",
                            Thread.CurrentThread.ManagedThreadId, _worldStateTimer != null ? "Active" : "Null");
                        
                        // Safe timer replacement using lock to prevent race conditions
                        lock (_transitionLock)
                        {
                            var oldTimer = _worldStateTimer;
                            _worldStateTimer = new Timer(async _ => 
                            {
                                try
                                {
                                    await PollWorldState();
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "[TIMER_CALLBACK] Unhandled exception in restarted world state timer callback. ThreadId={ThreadId}", Thread.CurrentThread.ManagedThreadId);
                                }
                            }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(33));
                            
                            // Dispose old timer after replacement to avoid race with active callbacks
                            try
                            {
                                oldTimer?.Dispose();
                            }
                            catch (Exception disposeEx)
                            {
                                _logger.LogWarning(disposeEx, "[WATCHDOG] Error disposing old timer. ThreadId={ThreadId}", Thread.CurrentThread.ManagedThreadId);
                            }
                        }
                        
                        _logger.LogInformation("[WATCHDOG] World state timer restarted successfully");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in polling health check");
        }
        
        return Task.CompletedTask;
    }
    
    private async Task AttemptReconnection()
    {
        if (_isTransitioning)
        {
            _logger.LogInformation("Already transitioning, skipping reconnection attempt");
            return;
        }
        
        try
        {
            _logger.LogInformation("Attempting to reconnect to current server");
            
            // First try to test the current connection
            if (_gameGrain != null)
            {
                try
                {
                    var testState = await _gameGrain.GetWorldState();
                    if (testState != null)
                    {
                        _logger.LogInformation("Connection test successful, resetting failure count and restarting timers");
                        _worldStatePollFailures = 0;
                        IsConnected = true;  // Fix: Set connection status to true since test succeeded
                        
                        // Restart the world state timer if it appears to have stopped
                        if (_worldStateTimer != null)
                        {
                            _logger.LogInformation("Restarting world state timer after successful connection test");
                            _worldStateTimer.Dispose();
                            _worldStateTimer = new Timer(async _ => 
                            {
                                try
                                {
                                    await PollWorldState();
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Unhandled exception in restarted world state timer callback");
                                }
                            }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(33));
                        }
                        
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Connection test failed, proceeding with reconnection");
                }
            }
            
            // Force a zone check to trigger proper reconnection
            await CheckForServerTransition();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to attempt reconnection");
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
        // Prevent multiple simultaneous transition attempts
        lock (_transitionLock)
        {
            if (string.IsNullOrEmpty(PlayerId) || _isTransitioning)
            {
                _logger.LogTrace("[ZONE_TRANSITION] Skipping transition - already in progress. IsTransitioning={IsTransitioning}, PlayerId={PlayerId}", 
                    _isTransitioning, string.IsNullOrEmpty(PlayerId) ? "NULL" : "SET");
                return;
            }
            
            var now = DateTime.UtcNow;
            _lastTransitionAttempt = now;
            _transitionAttempts++;
            _isTransitioning = true;
        }
        
        try
        {
            _logger.LogDebug("[ZONE_TRANSITION] Attempt #{Attempts} for player {PlayerId}", _transitionAttempts, PlayerId);
            
            await CheckForServerTransitionInternal();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ZONE_TRANSITION] Error in transition attempt #{Attempts}", _transitionAttempts);
        }
        finally
        {
            lock (_transitionLock)
            {
                _isTransitioning = false;
                _currentTransitionTarget = null; // Clear transition target to allow new transitions
            }
        }
    }
    
    private async Task CheckForServerTransitionInternal()
    {
        if (string.IsNullOrEmpty(PlayerId))
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
                // Check for transition target deduplication
                var targetZoneKey = $"{response.AssignedSquare.X},{response.AssignedSquare.Y}";
                
                lock (_transitionLock)
                {
                    if (_currentTransitionTarget == targetZoneKey)
                    {
                        _logger.LogDebug("[ZONE_TRANSITION] Already transitioning to target zone {TargetZone}, skipping duplicate transition", targetZoneKey);
                        return;
                    }
                    _currentTransitionTarget = targetZoneKey;
                }
                
                _logger.LogInformation("[ZONE_TRANSITION] Player {PlayerId} needs to transition from server {OldServer} to {NewServer} for zone ({ZoneX},{ZoneY})", 
                    PlayerId, CurrentServerId, response.ServerId, response.AssignedSquare.X, response.AssignedSquare.Y);
                
                var cleanupStart = DateTime.UtcNow;
                
                // Stop timers before disconnecting
                _worldStateTimer?.Dispose();
                _heartbeatTimer?.Dispose();
                _availableZonesTimer?.Dispose();
                _networkStatsTimer?.Dispose();
                _watchdogTimer?.Dispose();
                _worldStateTimer = null;
                _heartbeatTimer = null;
                _availableZonesTimer = null;
                _networkStatsTimer = null;
                _watchdogTimer = null;
                
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
        TrackVisitedZone($"{serverInfo.AssignedSquare.X},{serverInfo.AssignedSquare.Y}");
        
        // Create new cancellation token source for the new connection
        _cancellationTokenSource = new CancellationTokenSource();
        
        try
        {
        
        // Check if we have a pre-established connection for this zone
        var connectionKey = $"{serverInfo.AssignedSquare.X},{serverInfo.AssignedSquare.Y}";
        _logger.LogInformation("[ZONE_TRANSITION] Checking for pre-established connection with key {Key}. Available keys: {Keys}", 
            connectionKey, string.Join(", ", _preEstablishedConnections.Keys));
        
        if (_preEstablishedConnections.TryGetValue(connectionKey, out var preEstablished) && preEstablished.IsConnected)
        {
            // Verify the connection is still alive by attempting a simple operation with retry
            bool connectionStillValid = false;
            int maxRetries = 2;
            
            for (int attempt = 0; attempt < maxRetries && !connectionStillValid; attempt++)
            {
                try
                {
                    _logger.LogInformation("[ZONE_TRANSITION] Testing pre-established connection for zone {Key} (attempt {Attempt}/{Max})", 
                        connectionKey, attempt + 1, maxRetries);
                    var testState = await preEstablished.GameGrain!.GetWorldState();
                    connectionStillValid = true;
                    _logger.LogInformation("[ZONE_TRANSITION] Pre-established connection test successful");
                }
                catch (Exception ex)
                {
                    if (attempt < maxRetries - 1)
                    {
                        _logger.LogWarning("[ZONE_TRANSITION] Connection test failed for zone {Key}, retrying: {Error}", 
                            connectionKey, ex.Message);
                        await Task.Delay(100); // Short delay before retry
                    }
                    else
                    {
                        _logger.LogWarning("[ZONE_TRANSITION] Pre-established connection for zone {Key} is no longer valid after {Attempts} attempts: {Error}", 
                            connectionKey, maxRetries, ex.Message);
                        preEstablished.IsConnected = false;
                    }
                }
            }
            
            if (connectionStillValid)
            {
                _logger.LogInformation("[ZONE_TRANSITION] Using pre-established connection for zone {Key}", connectionKey);
                preEstablished.LastUsedTime = DateTime.UtcNow;
                
                // DON'T share RPC infrastructure - create independent connection
                // This prevents Cleanup() from destroying pre-established connections
                _logger.LogInformation("[ZONE_TRANSITION] Creating independent connection based on pre-established zone {Key}", connectionKey);
                
                // Create new independent RPC host for main connection
                var serverHost = preEstablished.ServerInfo.IpAddress;
                var rpcPort = preEstablished.ServerInfo.RpcPort;
                
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
                            _logger.LogInformation("Resolved hostname {Host} to IP {IP}", serverHost, resolvedHost);
                        }
                        else
                        {
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
                
                // Create independent RPC host for main connection (don't share with pre-established)
                var hostBuilder = Host.CreateDefaultBuilder()
                    .UseOrleansRpcClient(rpcBuilder =>
                    {
                        rpcBuilder.ConnectTo(resolvedHost, rpcPort);
                        var transportType = _configuration["RpcTransport"] ?? "litenetlib";
                        switch (transportType.ToLowerInvariant())
                        {
                            case "ruffles":
                                _logger.LogInformation("Using Ruffles UDP transport");
                                rpcBuilder.UseRuffles();
                                TransportType = "ruffles";
                                break;
                            case "litenetlib":
                            default:
                                _logger.LogInformation("Using LiteNetLib UDP transport");
                                rpcBuilder.UseLiteNetLib();
                                TransportType = "litenetlib";
                                break;
                        }
                    })
                    .ConfigureServices(services =>
                    {
                        services.AddSerializer(serializer =>
                        {
                            serializer.AddAssembly(typeof(IGameRpcGrain).Assembly);
                            // Add RPC protocol assembly for RPC message serialization
                            serializer.AddAssembly(typeof(Granville.Rpc.Protocol.RpcMessage).Assembly);
                            // Add Shooter.Shared assembly for game models (Player, WorldState, etc.)
                            serializer.AddAssembly(typeof(PlayerInfo).Assembly);
                        });
                    })
                    .Build();
                
                await hostBuilder.StartAsync();
                _rpcHost = hostBuilder;
                _rpcClient = hostBuilder.Services.GetRequiredService<Granville.Rpc.IRpcClient>();
                
                // Get the game grain for the independent connection
                _logger.LogInformation("[ZONE_TRANSITION] Waiting for RPC handshake on independent connection...");
                
                using var handshakeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    handshakeTimeout.Token, 
                    _cancellationTokenSource?.Token ?? CancellationToken.None);
                
                int retryCount = 0;
                const int maxHandshakeRetries = 15; // Increased from 5 to 15 for better manifest reliability
                const int retryDelayMs = 50; // Low retry delay - manifest sync is usually very fast
                
                while (retryCount < maxHandshakeRetries)
                {
                    if (combinedCts.Token.IsCancellationRequested)
                    {
                        _logger.LogWarning("[ZONE_TRANSITION] Independent RPC handshake cancelled or timed out");
                        throw new OperationCanceledException("Independent RPC handshake was cancelled or timed out");
                    }
                    
                    try
                    {
                        if (retryCount > 0)
                        {
                            await Task.Delay(retryDelayMs, combinedCts.Token); // Fixed 50ms delay instead of progressive
                        }
                        
                        var getGrainTask = Task.Run(() => _rpcClient.GetGrain<IGameRpcGrain>("game"), combinedCts.Token);
                        _gameGrain = await getGrainTask.WaitAsync(TimeSpan.FromSeconds(2), combinedCts.Token);
                        _logger.LogInformation("[ZONE_TRANSITION] Successfully obtained game grain on independent connection (attempt {Attempt})", retryCount + 1);
                        break;
                    }
                    catch (ArgumentException ex) when (ex.Message.Contains("Could not find an implementation"))
                    {
                        retryCount++;
                        if (retryCount < maxHandshakeRetries)
                        {
                            _logger.LogWarning("[ZONE_TRANSITION] Independent connection manifest not ready, retry {Retry}/{Max}: {Error}", retryCount, maxHandshakeRetries, ex.Message);
                        }
                        else
                        {
                            _logger.LogError(ex, "[ZONE_TRANSITION] Failed to get game grain on independent connection after {Max} retries", maxHandshakeRetries);
                            throw;
                        }
                    }
                    catch (TimeoutException)
                    {
                        retryCount++;
                        if (retryCount < maxHandshakeRetries)
                        {
                            _logger.LogWarning("[ZONE_TRANSITION] Independent GetGrain timed out, retry {Retry}/{Max}", retryCount, maxHandshakeRetries);
                        }
                        else
                        {
                            _logger.LogError("[ZONE_TRANSITION] Independent GetGrain timed out after {Max} retries", maxHandshakeRetries);
                            throw new TimeoutException("Independent GetGrain timed out after retries");
                        }
                    }
                }
                
                // Don't remove from pre-established connections yet - let the cleanup handle it
                // This prevents race conditions where the connection might be needed again soon
                
                // Reconnect player with timeout
                _logger.LogInformation("Calling ConnectPlayer for {PlayerId} on pre-established connection", PlayerId);
                
                using var preConnectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var preConnectCts = CancellationTokenSource.CreateLinkedTokenSource(
                    preConnectTimeout.Token,
                    _cancellationTokenSource?.Token ?? CancellationToken.None);
                
                try
                {
                    if (string.IsNullOrEmpty(PlayerId))
                    {
                        _logger.LogError("[ZONE_TRANSITION] Cannot reconnect: PlayerId is null or empty");
                        return;
                    }
                    
                    var connectTask = _gameGrain!.ConnectPlayer(PlayerId);
                    var result = await connectTask.WaitAsync(TimeSpan.FromSeconds(5), preConnectCts.Token);
                    _logger.LogInformation("ConnectPlayer returned: {Result}", result);
                    
                    if (result != "SUCCESS")
                    {
                        _logger.LogError("Failed to reconnect player {PlayerId} to pre-established server", PlayerId);
                        return;
                    }
                }
                catch (TimeoutException)
                {
                    _logger.LogError("[ZONE_TRANSITION] ConnectPlayer on pre-established connection timed out after 5 seconds");
                    // Mark this connection as invalid and fall through to create new one
                    preEstablished.IsConnected = false;
                    await CleanupPreEstablishedConnection(connectionKey);
                    _preEstablishedConnections.TryRemove(connectionKey, out _);
                    _rpcHost = null; // Force creation of new connection
                    connectionStillValid = false;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogError("[ZONE_TRANSITION] ConnectPlayer on pre-established connection was cancelled");
                    throw;
                }
            }
            else
            {
                // Connection is dead, remove it and fall through to create a new one
                _logger.LogInformation("[ZONE_TRANSITION] Removing dead pre-established connection for zone {Key}", connectionKey);
                await CleanupPreEstablishedConnection(connectionKey);
                _preEstablishedConnections.TryRemove(connectionKey, out _);
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
                        // Add RPC protocol assembly for RPC message serialization
                        serializer.AddAssembly(typeof(Granville.Rpc.Protocol.RpcMessage).Assembly);
                        // Add Shooter.Shared assembly for game models (Player, WorldState, etc.)
                        serializer.AddAssembly(typeof(PlayerInfo).Assembly);
                    });
                })
                .Build();
                
            await hostBuilder.StartAsync();
            
            _rpcHost = hostBuilder;
            _rpcClient = hostBuilder.Services.GetRequiredService<Granville.Rpc.IRpcClient>();
            
            
            // Wait for handshake and manifest exchange to complete with timeout and retry logic
            _logger.LogInformation("[ZONE_TRANSITION] Waiting for RPC handshake...");
            
            using var handshakeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); // 10 second overall timeout
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                handshakeTimeout.Token, 
                _cancellationTokenSource?.Token ?? CancellationToken.None);
            
            int retryCount = 0;
            const int maxRetries = 15; // Increased for better manifest reliability
            const int retryDelayMs = 50; // Low delay - manifest sync is usually very fast
            Exception? lastException = null;
            
            while (retryCount < maxRetries)
            {
                if (combinedCts.Token.IsCancellationRequested)
                {
                    _logger.LogWarning("[ZONE_TRANSITION] RPC handshake cancelled or timed out");
                    throw new OperationCanceledException("RPC handshake was cancelled or timed out");
                }
                
                try
                {
                    // Short delay before each attempt, but not on first attempt
                    if (retryCount > 0)
                    {
                        await Task.Delay(retryDelayMs, combinedCts.Token); // Fixed 50ms delay instead of progressive
                    }
                    
                    // Get the game grain with timeout
                    var getGrainTask = Task.Run(() => _rpcClient.GetGrain<IGameRpcGrain>("game"), combinedCts.Token);
                    _gameGrain = await getGrainTask.WaitAsync(TimeSpan.FromSeconds(2), combinedCts.Token);
                    _logger.LogInformation("[ZONE_TRANSITION] Successfully obtained game grain on attempt {Attempt}", retryCount + 1);
                    break;
                }
                catch (ArgumentException ex) when (ex.Message.Contains("Could not find an implementation"))
                {
                    lastException = ex;
                    retryCount++;
                    if (retryCount < maxRetries)
                    {
                        _logger.LogWarning("[ZONE_TRANSITION] Manifest not ready, retry {Retry}/{Max}: {Error}", retryCount, maxRetries, ex.Message);
                    }
                    else
                    {
                        _logger.LogError(ex, "[ZONE_TRANSITION] Failed to get game grain after {Max} retries", maxRetries);
                        throw;
                    }
                }
                catch (TimeoutException)
                {
                    lastException = new TimeoutException($"GetGrain timed out on attempt {retryCount + 1}");
                    retryCount++;
                    if (retryCount < maxRetries)
                    {
                        _logger.LogWarning("[ZONE_TRANSITION] GetGrain timed out, retry {Retry}/{Max}", retryCount, maxRetries);
                    }
                    else
                    {
                        _logger.LogError(lastException, "[ZONE_TRANSITION] GetGrain timed out after {Max} retries", maxRetries);
                        throw lastException;
                    }
                }
                catch (OperationCanceledException) when (handshakeTimeout.Token.IsCancellationRequested)
                {
                    _logger.LogError("[ZONE_TRANSITION] RPC handshake timed out after {Timeout} seconds", 10);
                    throw new TimeoutException("RPC handshake timed out after 10 seconds");
                }
            }
            
            // Reconnect player
            if (_gameGrain == null)
            {
                _logger.LogError("[ZONE_TRANSITION] Game grain is null after all retries");
                return;
            }
            
            _logger.LogInformation("Calling ConnectPlayer for {PlayerId} on new server", PlayerId);
            
            // Add timeout to ConnectPlayer call as well
            using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(
                connectTimeout.Token,
                _cancellationTokenSource?.Token ?? CancellationToken.None);
            
            try
            {
                if (string.IsNullOrEmpty(PlayerId))
                {
                    _logger.LogError("[ZONE_TRANSITION] Cannot connect to new server: PlayerId is null or empty");
                    return;
                }
                
                var connectTask = _gameGrain.ConnectPlayer(PlayerId);
                var result = await connectTask.WaitAsync(TimeSpan.FromSeconds(5), connectCts.Token);
                _logger.LogInformation("ConnectPlayer returned: {Result}", result);
                
                if (result != "SUCCESS")
                {
                    _logger.LogError("Failed to reconnect player {PlayerId} to new server", PlayerId);
                    return;
                }
            }
            catch (TimeoutException)
            {
                _logger.LogError("[ZONE_TRANSITION] ConnectPlayer timed out after 5 seconds");
                throw;
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("[ZONE_TRANSITION] ConnectPlayer was cancelled");
                throw;
            }
            
        }
        
        IsConnected = true;
        
        // Test the connection with a simple call before starting timers
        try
        {
            _logger.LogInformation("Testing connection with GetWorldState call");
            using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var testCts = CancellationTokenSource.CreateLinkedTokenSource(
                testTimeout.Token,
                _cancellationTokenSource?.Token ?? CancellationToken.None);
            
            if (_gameGrain != null)
            {
                var testTask = _gameGrain.GetWorldState();
                var testState = await testTask.WaitAsync(TimeSpan.FromSeconds(3), testCts.Token);
                _logger.LogInformation("Test GetWorldState succeeded, got {Count} entities", testState?.Entities?.Count ?? 0);
            }
            else
            {
                _logger.LogError("Game grain is null, cannot test connection");
                IsConnected = false;
                return;
            }
        }
        catch (TimeoutException)
        {
            _logger.LogError("Test GetWorldState timed out after 3 seconds");
            IsConnected = false;
            return;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("Test GetWorldState was cancelled");
            IsConnected = false;
            return;
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
        _worldStateTimer = new Timer(async _ => await PollWorldState(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(33)); // Reduced from 16ms to 33ms (30 FPS)
        _heartbeatTimer = new Timer(async _ => await SendHeartbeat(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
        _availableZonesTimer = new Timer(async _ => await PollAvailableZones(), null, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(10));
        _networkStatsTimer = new Timer(async _ => await PollNetworkStats(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        
        // Restart chat polling if it was active
        if (_observer == null)
        {
            StartChatPolling();
        }
        
        // Restart watchdog timer
        _watchdogTimer?.Dispose();
        _watchdogTimer = new Timer(_ => CheckPollingHealth(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        
        // Reset sequence number for new server
        _lastSequenceNumber = -1;
        
        // Reset polling health tracking
        _worldStatePollFailures = 0;
        _lastWorldStatePollTime = DateTime.UtcNow;
        
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
        catch (TimeoutException ex)
        {
            bool wasTransitioning;
            lock (_transitionLock)
            {
                wasTransitioning = _isTransitioning;
                _isTransitioning = false;
            }
            _logger.LogError(ex, "[ZONE_TRANSITION] Zone transition timed out. State: IsConnected={IsConnected}, WasTransitioning={WasTransitioning}, ServerId={ServerId}, ThreadId={ThreadId}, TimersActive={TimersActive}",
                IsConnected, wasTransitioning, CurrentServerId, Thread.CurrentThread.ManagedThreadId, 
                $"World:{_worldStateTimer != null}, Heart:{_heartbeatTimer != null}, Zones:{_availableZonesTimer != null}, Watchdog:{_watchdogTimer != null}");
            IsConnected = false;
            throw;
        }
        catch (OperationCanceledException ex)
        {
            bool wasTransitioning;
            lock (_transitionLock)
            {
                wasTransitioning = _isTransitioning;
                _isTransitioning = false;
            }
            _logger.LogError(ex, "[ZONE_TRANSITION] Zone transition was cancelled. State: IsConnected={IsConnected}, WasTransitioning={WasTransitioning}, ServerId={ServerId}, ThreadId={ThreadId}, CancellationRequested={CancellationRequested}",
                IsConnected, wasTransitioning, CurrentServerId, Thread.CurrentThread.ManagedThreadId, _cancellationTokenSource?.Token.IsCancellationRequested ?? false);
            IsConnected = false;
            throw;
        }
        catch (Exception ex)
        {
            bool wasTransitioning;
            lock (_transitionLock)
            {
                wasTransitioning = _isTransitioning;
                _isTransitioning = false;
            }
            _logger.LogError(ex, "[ZONE_TRANSITION] Zone transition failed with unexpected error. ExceptionType={ExceptionType}, State: IsConnected={IsConnected}, WasTransitioning={WasTransitioning}, ServerId={ServerId}, ThreadId={ThreadId}, RpcHostState={RpcHostState}, GameGrainState={GameGrainState}",
                ex.GetType().FullName, IsConnected, wasTransitioning, CurrentServerId, Thread.CurrentThread.ManagedThreadId, 
                _rpcHost?.Services != null ? "Active" : "Null", _gameGrain != null ? "Active" : "Null");
            IsConnected = false;
            
            // Clean up any partial state
            try
            {
                Cleanup();
            }
            catch (Exception cleanupEx)
            {
                _logger.LogError(cleanupEx, "[ZONE_TRANSITION] Error during cleanup after failed zone transition. CleanupException={CleanupExceptionType}, ThreadId={ThreadId}",
                    cleanupEx.GetType().FullName, Thread.CurrentThread.ManagedThreadId);
            }
            
            throw;
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
                
                // Include zones within connection creation distance
                if (distToZone <= CONNECTION_CREATE_DISTANCE)
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
            
            // Check if player is actively moving
            var playerSpeed = 0f;
            if (playerEntity != null)
            {
                playerSpeed = (float)Math.Sqrt(playerEntity.Velocity.X * playerEntity.Velocity.X + 
                                              playerEntity.Velocity.Y * playerEntity.Velocity.Y);
            }
            
            // Skip cleanup if player is moving very fast (threshold: 150 units/sec)
            // Normal speeds: human players 80 units/sec, bots 100 units/sec
            if (playerSpeed > 150f)
            {
                _logger.LogDebug("Skipping connection cleanup - player is moving extremely fast at {Speed} units/sec", playerSpeed);
                return;
            }
            
            // Clean up connections based on hysteresis distance and last used time
            var now = DateTime.UtcNow;
            var connectionTimeout = TimeSpan.FromSeconds(30); // Keep connections alive for 30 seconds after last use
            
            // Calculate which connections are beyond disposal distance
            var keysToRemove = new List<string>();
            foreach (var kvp in _preEstablishedConnections)
            {
                // Never remove current zone
                if (kvp.Key == $"{_currentZone.X},{_currentZone.Y}")
                    continue;
                
                // Never remove recently visited zones
                if (_recentlyVisitedZones.Any(z => z.zoneKey == kvp.Key))
                {
                    _logger.LogTrace("Skipping cleanup of recently visited zone {Key}", kvp.Key);
                    continue;
                }
                
                // Parse zone coordinates
                var parts = kvp.Key.Split(',');
                if (parts.Length != 2 || !int.TryParse(parts[0], out var zoneX) || !int.TryParse(parts[1], out var zoneY))
                    continue;
                
                var zone = new GridSquare(zoneX, zoneY);
                var (min, max) = zone.GetBounds();
                
                // Calculate distance from player to zone
                float distToZone = 0;
                
                if (playerEntity!.Position.X < min.X)
                    distToZone = Math.Max(distToZone, min.X - playerEntity.Position.X);
                else if (playerEntity.Position.X > max.X)
                    distToZone = Math.Max(distToZone, playerEntity.Position.X - max.X);
                
                if (playerEntity.Position.Y < min.Y)
                    distToZone = Math.Max(distToZone, min.Y - playerEntity.Position.Y);
                else if (playerEntity.Position.Y > max.Y)
                    distToZone = Math.Max(distToZone, playerEntity.Position.Y - max.Y);
                
                // Remove if beyond disposal distance AND hasn't been used recently
                if (distToZone > CONNECTION_DISPOSE_DISTANCE && 
                    (now - kvp.Value.LastUsedTime) > connectionTimeout)
                {
                    keysToRemove.Add(kvp.Key);
                    _logger.LogDebug("Marking connection {Key} for removal - distance: {Distance}, last used: {LastUsed}s ago", 
                        kvp.Key, distToZone, (now - kvp.Value.LastUsedTime).TotalSeconds);
                }
            }
            
            if (keysToRemove.Count > 0)
            {
                _logger.LogInformation("Cleaning up {Count} old pre-established connections: {Keys}", 
                    keysToRemove.Count, string.Join(", ", keysToRemove));
            }
            
            // Log current pre-connection status
            _logger.LogInformation("Pre-connection status after update: Total={Count}, Zones={Zones}", 
                _preEstablishedConnections.Count, 
                string.Join(", ", _preEstablishedConnections.Select(kvp => $"{kvp.Key}:{(kvp.Value.IsConnected ? "OK" : "DEAD")}")));
            
            // Run cleanup operations in parallel for better performance
            var cleanupTasks = keysToRemove.Select(key => CleanupPreEstablishedConnection(key));
            await Task.WhenAll(cleanupTasks);
            
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
                // Try health check with retry
                bool healthCheckPassed = false;
                int maxRetries = 2;
                
                for (int attempt = 0; attempt < maxRetries && !healthCheckPassed; attempt++)
                {
                    try
                    {
                        // Send a lightweight request to check if connection is alive
                        var zones = await connection.GameGrain!.GetAvailableZones();
                        connection.IsConnected = true;
                        connection.EstablishedAt = DateTime.UtcNow; // Update last successful check time
                        connection.LastUsedTime = DateTime.UtcNow;
                        healthCheckPassed = true;
                    }
                    catch (Exception ex)
                    {
                        if (attempt < maxRetries - 1)
                        {
                            _logger.LogDebug("Pre-established connection {Key} health check failed, retrying: {Error}", key, ex.Message);
                            await Task.Delay(50); // Short delay before retry
                        }
                        else
                        {
                            _logger.LogWarning("Pre-established connection {Key} health check failed after {Attempts} attempts: {Error}", 
                                key, maxRetries, ex.Message);
                            connection.IsConnected = false;
                        }
                    }
                }
                
                // If it's been disconnected for too long, remove it
                if (!connection.IsConnected && (DateTime.UtcNow - connection.EstablishedAt).TotalMinutes > 1)
                {
                    _logger.LogInformation("Removing stale pre-established connection {Key}", key);
                    await CleanupPreEstablishedConnection(key);
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
                LastUsedTime = DateTime.UtcNow,
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
                        // Add RPC protocol assembly for RPC message serialization
                        serializer.AddAssembly(typeof(Granville.Rpc.Protocol.RpcMessage).Assembly);
                        // Add Shooter.Shared assembly for game models (Player, WorldState, etc.)
                        serializer.AddAssembly(typeof(PlayerInfo).Assembly);
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
            const int maxRetries = 15; // Increased for better manifest reliability
            
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
                        await Task.Delay(50); // Fixed 50ms delay - manifest sync is usually very fast
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
            
            _preEstablishedConnections.TryRemove(connectionKey, out _);
        }
    }
    
    private void TrackVisitedZone(string zoneKey)
    {
        _visitedZones.Add(zoneKey);
        
        // Add to recently visited zones
        _recentlyVisitedZones.Enqueue((zoneKey, DateTime.UtcNow));
        
        // Keep only the last 4 visited zones
        while (_recentlyVisitedZones.Count > 4)
        {
            _recentlyVisitedZones.Dequeue();
        }
        
        _logger.LogDebug("Tracked visit to zone {Zone}. Recent zones: {RecentZones}", 
            zoneKey, string.Join(", ", _recentlyVisitedZones.Select(z => z.zoneKey)));
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
                    connection.LastUsedTime = DateTime.UtcNow;
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
        _networkStatsTimer?.Dispose();
        _watchdogTimer?.Dispose();
        _worldStateTimer = null;
        _heartbeatTimer = null;
        _availableZonesTimer = null;
        _chatPollingTimer = null;
        _networkStatsTimer = null;
        _watchdogTimer = null;
        
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
    
    private IHost BuildRpcHost(string resolvedHost, int rpcPort, string? playerId = null)
    {
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
                    // Add RPC protocol assembly for RPC message serialization
                    serializer.AddAssembly(typeof(Granville.Rpc.Protocol.RpcMessage).Assembly);
                    // Add Shooter.Shared assembly for game models (PlayerInfo, WorldState, etc.)
                    serializer.AddAssembly(typeof(PlayerInfo).Assembly);
                });
                
                // Register the network statistics tracker as a singleton
                // Only for the main connection, not pre-established connections
                if (playerId != null && _clientNetworkTracker == null)
                {
                    services.AddSingleton<Granville.Rpc.Telemetry.INetworkStatisticsTracker>(sp =>
                    {
                        _clientNetworkTracker = new NetworkStatisticsTracker(playerId);
                        return _clientNetworkTracker;
                    });
                }
            })
            .Build();
            
        return hostBuilder;
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
        _chatPollingTimer = new Timer(async _ => 
        {
            try
            {
                await PollChatMessages();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in chat polling timer callback");
            }
        }, null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
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
    
    private async Task PollNetworkStats()
    {
        if (_gameGrain == null || !IsConnected || _cancellationTokenSource?.Token.IsCancellationRequested == true)
        {
            return;
        }
        
        try
        {
            var stats = await _gameGrain.GetNetworkStatistics();
            if (stats != null)
            {
                _logger.LogDebug("[NETWORK_STATS] Polled server stats: Sent={Sent}, Recv={Recv}", 
                    stats.PacketsSent, stats.PacketsReceived);
                HandleNetworkStats(stats);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[NETWORK_STATS] Failed to poll network statistics");
        }
    }
    
    public void HandleNetworkStats(NetworkStatistics stats)
    {
        _latestNetworkStats = stats;
        NetworkStatsUpdated?.Invoke(stats);
    }
    
    public NetworkStatistics? GetClientNetworkStats()
    {
        return _clientNetworkTracker?.GetStats();
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
    public DateTime LastUsedTime { get; set; }
    public bool IsConnected { get; set; }
    public bool IsConnecting { get; set; }
}

