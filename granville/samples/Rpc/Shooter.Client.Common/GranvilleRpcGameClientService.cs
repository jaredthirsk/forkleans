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
using Shooter.Client.Common.Configuration;
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
    private readonly ILoggerFactory _loggerFactory;
    private readonly ZoneTransitionDebouncer _zoneDebouncer;
    private readonly RobustTimerManager _timerManager;
    private readonly ConnectionResilienceManager _connectionManager;
    private Granville.Rpc.IRpcClient? _rpcClient;
    private IHost? _rpcHost;
    private IGameGranule? _gameGrain;
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
    private DateTime _lastForcedTransitionTime = DateTime.MinValue; // Track last forced reconnection to prevent rapid-fire
    private string? _currentTransitionTarget = null; // Track zone transition target for deduplication
    private long _lastSequenceNumber = -1;
    private readonly ConcurrentDictionary<string, PreEstablishedConnection> _preEstablishedConnections = new();
    private GridSquare? _currentZone = null;
    private GridSquare? _previousZone = null; // For one-way hysteresis
    private GridSquare? _lastStableZone = null; // Last zone player was stable in (for hysteresis calculation)
    private DateTime _zoneEntryTime = DateTime.MinValue; // When entered current zone
    private DateTime _stableZoneEntryTime = DateTime.MinValue; // When entered last stable zone
    private bool _isBlockedFromPreviousZone = false; // Currently blocked from re-entering
    private Vector2 _blockedBoundaryNormal = Vector2.Zero; // Normal vector of blocked boundary
    private DateTime _blockingStartTime = DateTime.MinValue; // When one-way blocking started
    private WorldState? _lastWorldState = null;
    private Vector2 _lastInputDirection = Vector2.Zero;
    private bool _lastInputShooting = false;
    private readonly HashSet<string> _visitedZones = new();
    private readonly Queue<(string zoneKey, DateTime visitTime)> _recentlyVisitedZones = new();
    private bool _worldStateErrorLogged = false;
    private DateTime _lastWorldStateError = DateTime.MinValue;
    private bool _playerInputErrorLogged = false;
    private DateTime _lastPlayerInputError = DateTime.MinValue;
    private GameObserver? _observer;
    private List<GridSquare> _cachedAvailableZones = new();
    private Dictionary<string, List<EntityState>> _cachedAdjacentEntities = new();
    private NetworkStatistics? _latestNetworkStats = null;
    private NetworkStatisticsTracker? _clientNetworkTracker = null;
    private Vector2? _lastKnownPlayerPosition = null; // Track last known player position
    private DateTime _lastPositionUpdateTime = DateTime.MinValue; // When position was last updated
    private ZoneTransitionHealthMonitor? _healthMonitor = null; // Health monitoring for zone transitions
    private Timer? _healthReportTimer = null; // Timer for periodic health reports
    private DateTime _zoneTransitionStartTime = DateTime.MinValue; // When zone transition started
    private ZoneTransitionDebouncer? _zoneTransitionDebouncer = null; // Prevents rapid zone transitions
    
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
    public event Action<VictoryPauseMessage>? VictoryPauseReceived;
    public event Action<GameOverMessage>? GameOverReceived;
    public event Action<GridSquare?, Vector2, bool>? OneWayBoundaryStateChanged; // previousZone, boundaryNormal, isBlocked
    public event Action? GameRestartedReceived;
    public event Action<ChatMessage>? ChatMessageReceived;
    public event Action<NetworkStatistics>? NetworkStatsUpdated;
    
    public bool IsConnected { get; private set; }
    public bool IsTransitioning => _isTransitioning;
    public string? PlayerId { get; private set; }
    public string? PlayerName { get; private set; }
    public string? CurrentServerId { get; private set; }
    public string? TransportType { get; private set; }
    public GridSquare? CurrentGridSquare => _currentZone;
    public bool IsBlockedFromPreviousZone => _isBlockedFromPreviousZone;
    public GridSquare? BlockedZone => _isBlockedFromPreviousZone ? _previousZone : null;
    public Vector2 BlockedBoundaryNormal => _blockedBoundaryNormal;
    
    public GranvilleRpcGameClientService(
        ILogger<GranvilleRpcGameClientService> logger,
        HttpClient httpClient,
        IConfiguration configuration,
        ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _httpClient = httpClient;
        _configuration = configuration;
        _loggerFactory = loggerFactory;
        
        // Initialize the protection components
        _zoneDebouncer = new ZoneTransitionDebouncer(loggerFactory.CreateLogger<ZoneTransitionDebouncer>());
        _timerManager = new RobustTimerManager(loggerFactory.CreateLogger<RobustTimerManager>());
        _connectionManager = new ConnectionResilienceManager(loggerFactory.CreateLogger<ConnectionResilienceManager>());
        _healthMonitor = new ZoneTransitionHealthMonitor(loggerFactory.CreateLogger<ZoneTransitionHealthMonitor>());
        _zoneTransitionDebouncer = new ZoneTransitionDebouncer(loggerFactory.CreateLogger<ZoneTransitionDebouncer>());

        // Subscribe to prolonged mismatch events for forced reconnection
        _healthMonitor.OnProlongedMismatchDetected += (playerZone, serverZone, duration) =>
        {
            _logger.LogWarning("[FORCED_RECONNECT] Triggering forced reconnection due to prolonged zone mismatch. " +
                "Player in zone ({PlayerX},{PlayerY}), server expects ({ServerX},{ServerY}), duration: {Duration}ms",
                playerZone.X, playerZone.Y, serverZone.X, serverZone.Y, duration);

            // Initiate forced zone transition to correct zone
            RunSafeFireAndForget(async () =>
            {
                // Small delay to avoid rapid reconnections
                await Task.Delay(500);

                // Force transition to the player's actual zone
                await ForceZoneTransition(playerZone);

                _logger.LogInformation("[FORCED_RECONNECT] Successfully forced transition to zone ({X},{Y})",
                    playerZone.X, playerZone.Y);
            }, "ForceZoneTransition");
        };

        // Set up periodic health reporting (every 30 seconds)
        _healthReportTimer = new Timer(_ => 
        {
            try
            {
                _healthMonitor?.LogHealthReport();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging health report");
            }
        }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }
    
    public async Task<bool> ConnectAsync(string playerName)
    {
        try
        {
            // Prevent multiple concurrent connection attempts
            lock (_transitionLock)
            {
                if (_isTransitioning || IsConnected)
                {
                    _logger.LogWarning("[CONNECT] Skipping connection - already connecting/connected. IsTransitioning={IsTransitioning}, IsConnected={IsConnected}", 
                        _isTransitioning, IsConnected);
                    return IsConnected;
                }
                _isTransitioning = true;
            }
            
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
            
            // Report server zone to health monitor
            _healthMonitor?.UpdateServerZone(_currentZone);
            
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
            _logger.LogInformation("Creating RPC host for {Host}:{Port}", resolvedHost, rpcPort);
            var host = BuildRpcHost(resolvedHost, rpcPort, PlayerId);
            _rpcHost = host;
            
            try
            {
                // Start the host in the background to avoid blocking on console lifetime
                _ = host.RunAsync();
                
                // Give the host time to start services
                await Task.Delay(500);
                _logger.LogInformation("RPC host services started");
                
                // Defer getting the RPC client service to avoid potential blocking
                // during host startup. Give the host time to fully initialize.
                await Task.Delay(100);
                
                // Now get the RPC client service after a small delay
                _logger.LogInformation("Getting RPC client service from DI container");
                try
                {
                    _rpcClient = host.Services.GetRequiredService<Granville.Rpc.IRpcClient>();
                    _logger.LogInformation("RPC client obtained successfully");
                }
                catch (Exception serviceEx)
                {
                    _logger.LogError(serviceEx, "Failed to get RPC client service from DI container");
                    // Try one more time after another delay
                    await Task.Delay(500);
                    _rpcClient = host.Services.GetRequiredService<Granville.Rpc.IRpcClient>();
                    _logger.LogInformation("RPC client obtained successfully on retry");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start RPC host or get RPC client");
                return false;
            }
            
            
            // Debug: check what manifest provider we have
            try
            {
                var manifestProvider = _rpcHost?.Services.GetKeyedService<IClusterManifestProvider>("rpc");
                _logger.LogInformation("RPC manifest provider type: {Type}", manifestProvider?.GetType().FullName ?? "NULL");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not get keyed manifest provider: {Error}", ex.Message);
            }
            
            // Enhanced debugging: Check RPC client state
            _logger.LogInformation("RPC client started, beginning grain acquisition process");
            _logger.LogInformation("RPC client type: {Type}", _rpcClient?.GetType().FullName ?? "NULL");
            
            // Wait for the manifest to be populated from the server
            _logger.LogInformation("Waiting for RPC manifest to be populated...");
            
            if (_rpcClient == null)
            {
                _logger.LogError("RPC client is null, cannot wait for manifest");
                return false;
            }
            
            try
            {
                await _rpcClient.WaitForManifestAsync(TimeSpan.FromSeconds(10));
                _logger.LogInformation("RPC manifest is ready, proceeding to get grain");
            }
            catch (TimeoutException ex)
            {
                _logger.LogError("Failed to receive manifest from server: {Error}", ex.Message);
                return false;
            }
            
            // Now try to get the grain (should work immediately)
            const int maxRetries = 3; // Reduced since manifest should be ready
            const int retryDelayMs = 200;
            const int grainAcquisitionTimeoutSeconds = 10; // Reduced timeout
            
            var startTime = DateTime.UtcNow;
            _logger.LogInformation("Starting grain acquisition at {StartTime}", startTime);
            
            using var grainAcquisitionCts = new CancellationTokenSource(TimeSpan.FromSeconds(grainAcquisitionTimeoutSeconds));
            
            try
            {
                for (int i = 0; i < maxRetries; i++)
                {
                    if (grainAcquisitionCts.Token.IsCancellationRequested)
                    {
                        _logger.LogError("Grain acquisition timed out after {Seconds} seconds", grainAcquisitionTimeoutSeconds);
                        throw new TimeoutException($"Grain acquisition timed out after {grainAcquisitionTimeoutSeconds} seconds");
                    }
                    
                    try
                    {
                        _logger.LogInformation("Attempting to get IGameGranule, attempt {Attempt}/{MaxRetries}", i + 1, maxRetries);
                        
                        // Wrap GetGrain in a task with timeout
                        var getGrainTask = Task.Run(() => 
                        {
                            try
                            {
                                _logger.LogInformation("Inside Task.Run, about to call GetGrain. RpcClient is {RpcClientState}", 
                                    _rpcClient == null ? "NULL" : "not null");
                                
                                if (_rpcClient == null)
                                {
                                    _logger.LogError("RpcClient is null, cannot call GetGrain");
                                    return null;
                                }
                                
                                _logger.LogInformation("Calling _rpcClient.GetGrain<IGameGranule>(\"game\")");
                                _logger.LogInformation("RpcClient type check: {Type}, HashCode: {HashCode}", 
                                    _rpcClient.GetType().FullName, _rpcClient.GetHashCode());
                                
                                // Add thread info
                                _logger.LogInformation("Thread before GetGrain: {ThreadId}, IsBackground: {IsBackground}, IsThreadPool: {IsThreadPool}", 
                                    Thread.CurrentThread.ManagedThreadId, 
                                    Thread.CurrentThread.IsBackground, 
                                    Thread.CurrentThread.IsThreadPoolThread);
                                
                                IGameGranule? grain = null;
                                try
                                {
                                    _logger.LogInformation("About to invoke GetGrain method");
                                    grain = _rpcClient.GetGrain<IGameGranule>("game");
                                    _logger.LogInformation("GetGrain method returned");
                                }
                                catch (Exception innerEx)
                                {
                                    _logger.LogError(innerEx, "Exception during GetGrain call");
                                    throw;
                                }
                                
                                _logger.LogInformation("GetGrain returned: {GrainType}", grain?.GetType().FullName ?? "null");
                                return grain;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Exception in GetGrain task: {ExceptionType} - {Message}", 
                                    ex.GetType().FullName, ex.Message);
                                throw;
                            }
                        }, grainAcquisitionCts.Token);
                        
                        _logger.LogDebug("Waiting for GetGrain task with 2 second timeout...");
                        _gameGrain = await getGrainTask.WaitAsync(TimeSpan.FromSeconds(2), grainAcquisitionCts.Token);
                        
                        var elapsed = DateTime.UtcNow - startTime;
                        _logger.LogInformation("✅ Successfully obtained game grain on attempt {Attempt} after {ElapsedMs}ms", i + 1, elapsed.TotalMilliseconds);
                        break;
                    }
                    catch (ArgumentException ex) when (ex.Message.Contains("Could not find an implementation"))
                    {
                        _logger.LogWarning("❌ Grain acquisition attempt {Attempt}/{MaxRetries} failed: {Error}", i + 1, maxRetries, ex.Message);
                        
                        if (i < maxRetries - 1)
                        {
                            _logger.LogDebug("Waiting {DelayMs}ms before next attempt...", retryDelayMs);
                            await Task.Delay(retryDelayMs, grainAcquisitionCts.Token);
                        }
                        else
                        {
                            var totalElapsed = DateTime.UtcNow - startTime;
                            _logger.LogError("🚨 All grain acquisition attempts failed after {TotalMs}ms. ActionServer may not be running or may not have registered IGameGranule implementation.", totalElapsed.TotalMilliseconds);
                            throw new InvalidOperationException(
                                $"Failed to get game grain after {maxRetries} attempts. " +
                                "The RPC server may not have registered the grain implementation.", ex);
                        }
                    }
                    catch (TimeoutException)
                    {
                        _logger.LogError("GetGrain call timed out on attempt {Attempt}/{MaxRetries}", i + 1, maxRetries);
                        if (i < maxRetries - 1)
                        {
                            await Task.Delay(retryDelayMs, grainAcquisitionCts.Token);
                        }
                        else
                        {
                            throw new TimeoutException("GetGrain call timed out after all retries");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("Grain acquisition was cancelled (timeout after {Seconds} seconds)", grainAcquisitionTimeoutSeconds);
                throw new TimeoutException($"Grain acquisition timed out after {grainAcquisitionTimeoutSeconds} seconds");
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
            
            _logger.LogInformation("About to call ConnectPlayer for PlayerId: {PlayerId}", PlayerId);
            var connectStartTime = DateTime.UtcNow;
            
            string result;
            try
            {
                var connectTask = _gameGrain.ConnectPlayer(PlayerId);
                _logger.LogInformation("ConnectPlayer task created, waiting for completion...");
                
                // Add timeout to detect hanging
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                result = await connectTask.WaitAsync(TimeSpan.FromSeconds(10), cts.Token);
                
                var elapsed = DateTime.UtcNow - connectStartTime;
                _logger.LogInformation("RPC ConnectPlayer returned: {Result} after {ElapsedMs}ms", result, elapsed.TotalMilliseconds);
            }
            catch (TimeoutException)
            {
                _logger.LogError("ConnectPlayer timed out after 10 seconds for PlayerId: {PlayerId}", PlayerId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ConnectPlayer failed with exception for PlayerId: {PlayerId}", PlayerId);
                return false;
            }
            
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
                var loggerFactory = _rpcHost?.Services.GetRequiredService<ILoggerFactory>();
                if (loggerFactory == null)
                {
                    _logger.LogWarning("Could not get logger factory for observer");
                    return true;
                }
                _observer = new GameObserver(loggerFactory.CreateLogger<GameObserver>(), this);
                
                // Create an observer reference
                var observerRef = _rpcClient?.CreateObjectReference<IGameObserver>(_observer);
                if (observerRef != null && _gameGrain != null)
                {
                    await _gameGrain.Subscribe(observerRef);
                }
                
                _logger.LogInformation("[CHAT_DEBUG] Successfully subscribed to game updates via observer. Observer created: {ObserverCreated}", _observer != null);
            }
            catch (NotSupportedException)
            {
                _logger.LogDebug("[CHAT_DEBUG] Observer pattern not supported by RPC transport, falling back to polling.");
                // Start chat polling as fallback
                StartChatPolling();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[CHAT_DEBUG] Failed to subscribe observer, falling back to polling. Exception type: {ExceptionType}", ex.GetType().Name);
                // Start chat polling as fallback
                StartChatPolling();
            }
            
            // Start timers using RobustTimerManager for protected execution
            _timerManager.CreateTimer("worldState", _ =>
            {
                RunSafeFireAndForget(async () => await PollWorldState(), "PollWorldState");
            }, 33); // 30 FPS
            
            _timerManager.CreateTimer("heartbeat", _ =>
            {
                RunSafeFireAndForget(async () => await SendHeartbeat(), "SendHeartbeat");
            }, 5000);
            
            _timerManager.CreateTimer("availableZones", _ =>
            {
                RunSafeFireAndForget(async () => await PollAvailableZones(), "PollAvailableZones");
            }, 10000);
            
            _timerManager.CreateTimer("networkStats", _ =>
            {
                RunSafeFireAndForget(async () => await PollNetworkStats(), "PollNetworkStats");
            }, 1000);
            
            _timerManager.CreateTimer("watchdog", _ => { CheckPollingHealth(); }, 5000);
            
            // Initialize position tracking
            _lastKnownPlayerPosition = null;
            _lastPositionUpdateTime = DateTime.MinValue;
            
            // Initialize recovery timestamps
            _connectionStartTime = DateTime.UtcNow;
            _lastSuccessfulHeartbeat = DateTime.UtcNow;
            _lastWorldStateReceived = DateTime.UtcNow;
            
            _logger.LogInformation("Connected to game via Orleans RPC");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to game server");
            return false;
        }
        finally
        {
            // Always clear the transitioning flag
            lock (_transitionLock)
            {
                _isTransitioning = false;
            }
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
    
    private GridSquare ApplyOneWayHysteresis(GridSquare detectedZone, Vector2 position)
    {
        // Add comprehensive debug logging
        _logger.LogDebug("[HYSTERESIS_DEBUG] Called with detected=({DetX},{DetY}), pos=({PosX:F2},{PosY:F2}), " +
            "lastStable=({StableX},{StableY}), previous=({PrevX},{PrevY}), blocked={Blocked}",
            detectedZone.X, detectedZone.Y, position.X, position.Y,
            _lastStableZone?.X ?? -1, _lastStableZone?.Y ?? -1,
            _previousZone?.X ?? -1, _previousZone?.Y ?? -1,
            _isBlockedFromPreviousZone);
        
        // If we don't have a stable zone history, allow the transition
        if (_lastStableZone == null || _previousZone == null)
        {
            _logger.LogDebug("[HYSTERESIS_DEBUG] Initializing zones - no previous history");
            _isBlockedFromPreviousZone = false;
            _lastStableZone = detectedZone;
            _previousZone = detectedZone; // Initialize previousZone too
            _stableZoneEntryTime = DateTime.UtcNow;
            return detectedZone;
        }
        
        // If we're in a different zone than last stable, we've moved on
        if (detectedZone.X != _lastStableZone.X || detectedZone.Y != _lastStableZone.Y)
        {
            // First check if the player is very close to the zone boundary (likely a fluctuation)
            var distanceFromBoundary = CalculateDistanceFromBoundary(position, detectedZone, _lastStableZone);
            if (distanceFromBoundary >= 0 && distanceFromBoundary < ClientConfiguration.ZoneBoundaryFluctuationThresholdUnits)
            {
                // This is likely a position fluctuation near the boundary, ignore it
                _logger.LogTrace("[HYSTERESIS_DEBUG] Ignoring zone change - too close to boundary ({Distance:F2} units)",
                    distanceFromBoundary);
                return _lastStableZone; // Stay in current stable zone
            }
            
            // Check if trying to return to previous zone
            if (_previousZone != null && detectedZone.X == _previousZone.X && detectedZone.Y == _previousZone.Y)
            {
                // Calculate distance into the previous zone from the boundary
                var distanceIntoPreviousZone = CalculateDistanceFromBoundary(position, _previousZone, _lastStableZone);
                
                _logger.LogDebug("[HYSTERESIS_DEBUG] Trying to return to previous zone ({PrevX},{PrevY}) from ({StableX},{StableY}), " +
                    "distance={Distance:F2}, threshold={Threshold}",
                    _previousZone.X, _previousZone.Y, _lastStableZone.X, _lastStableZone.Y,
                    distanceIntoPreviousZone, ClientConfiguration.ZoneReentryThresholdUnits);
                
                // If zones aren't adjacent (distance < 0), something's wrong but allow it
                if (distanceIntoPreviousZone < 0)
                {
                    _logger.LogWarning("[ONE_WAY_HYSTERESIS] Non-adjacent zones: last stable ({FromX},{FromY}), trying to enter ({ToX},{ToY})",
                        _lastStableZone.X, _lastStableZone.Y, _previousZone.X, _previousZone.Y);
                        
                    bool wasBlocked = _isBlockedFromPreviousZone;
                    _isBlockedFromPreviousZone = false;
                    _blockingStartTime = DateTime.MinValue;
                    _lastStableZone = detectedZone;
                    _stableZoneEntryTime = DateTime.UtcNow;
                    
                    if (wasBlocked)
                    {
                        OneWayBoundaryStateChanged?.Invoke(null, Vector2.Zero, false);
                    }
                    
                    return detectedZone;
                }
                
                if (distanceIntoPreviousZone < ClientConfiguration.ZoneReentryThresholdUnits)
                {
                    // Block re-entry, stay in last stable zone
                    bool wasBlocked = _isBlockedFromPreviousZone;
                    _isBlockedFromPreviousZone = true;
                    
                    // Record when blocking started
                    if (!wasBlocked)
                    {
                        _blockingStartTime = DateTime.UtcNow;
                    }
                    
                    // Calculate boundary normal for bouncy wall physics
                    _blockedBoundaryNormal = CalculateBoundaryNormal(_lastStableZone, _previousZone);
                    
                    // Fire event if blocking state changed
                    if (!wasBlocked)
                    {
                        OneWayBoundaryStateChanged?.Invoke(_previousZone, _blockedBoundaryNormal, true);
                    }
                    
                    _logger.LogDebug("[ONE_WAY_HYSTERESIS] Blocking return to zone ({X},{Y}), distance: {Distance}, threshold: {Threshold}",
                        _previousZone.X, _previousZone.Y, distanceIntoPreviousZone, ClientConfiguration.ZoneReentryThresholdUnits);
                    
                    return _lastStableZone; // Stay in last stable zone
                }
                else
                {
                    // Sufficient distance, allow re-entry
                    _logger.LogInformation("[ONE_WAY_HYSTERESIS] Allowing return to zone ({X},{Y}), distance: {Distance} >= threshold: {Threshold}",
                        _previousZone.X, _previousZone.Y, distanceIntoPreviousZone, ClientConfiguration.ZoneReentryThresholdUnits);
                        
                    bool wasBlocked = _isBlockedFromPreviousZone;
                    _isBlockedFromPreviousZone = false;
                    _blockingStartTime = DateTime.MinValue;
                    _lastStableZone = detectedZone;
                    _stableZoneEntryTime = DateTime.UtcNow; // Update stable zone
                    
                    // Fire event if blocking state changed
                    if (wasBlocked)
                    {
                        OneWayBoundaryStateChanged?.Invoke(null, Vector2.Zero, false);
                    }
                    
                    return detectedZone;
                }
            }
            else
            {
                // Moving to a completely different zone (not previous)
                // Update zones and clear any existing blocking
                if (_isBlockedFromPreviousZone)
                {
                    _isBlockedFromPreviousZone = false;
                    _blockingStartTime = DateTime.MinValue;
                    OneWayBoundaryStateChanged?.Invoke(null, Vector2.Zero, false);
                }
                
                // Set up new one-way boundary immediately
                _previousZone = _lastStableZone;
                _lastStableZone = detectedZone;
                _stableZoneEntryTime = DateTime.UtcNow;
                
                // Show boundary immediately but not as "blocked" until someone tries to cross
                _isBlockedFromPreviousZone = false; // Not blocked yet, just active
                _blockingStartTime = DateTime.UtcNow;
                _blockedBoundaryNormal = CalculateBoundaryNormal(detectedZone, _previousZone);
                
                // Fire event to show chevrons immediately (cyan/blue, not blocked yet)
                OneWayBoundaryStateChanged?.Invoke(_previousZone, _blockedBoundaryNormal, false);
                
                _logger.LogDebug("[ONE_WAY_HYSTERESIS] Activated one-way boundary after entering zone ({X},{Y}) from ({PrevX},{PrevY})",
                    detectedZone.X, detectedZone.Y, _previousZone.X, _previousZone.Y);
                
                return detectedZone;
            }
        }
        
        // Staying in the same zone as last stable zone
        // Check for one-way boundary timeout (whether blocked or just active)
        if (_previousZone != null && _blockingStartTime != DateTime.MinValue)
        {
            var boundaryDuration = (DateTime.UtcNow - _blockingStartTime).TotalSeconds;
            if (boundaryDuration >= ClientConfiguration.OneWayTimeoutSeconds)
            {
                _logger.LogDebug("[ONE_WAY_HYSTERESIS] Clearing one-way boundary - timeout after {Duration:F1} seconds for zone ({X},{Y})",
                    boundaryDuration, _previousZone.X, _previousZone.Y);
                
                // Clear the boundary completely
                _previousZone = null;
                _isBlockedFromPreviousZone = false;
                _blockedBoundaryNormal = Vector2.Zero;
                _blockingStartTime = DateTime.MinValue;
                
                // Fire event to clear visual indicators
                OneWayBoundaryStateChanged?.Invoke(null, Vector2.Zero, false);
                return _lastStableZone;
            }
        }
        
        // If we've been stable in this zone for more than the configured time, clear the previous zone memory
        var stableZoneDuration = (DateTime.UtcNow - _stableZoneEntryTime).TotalSeconds;
        if (_previousZone != null && stableZoneDuration > ClientConfiguration.StableZoneClearTimeSeconds)
        {
            _logger.LogDebug("[ONE_WAY_HYSTERESIS] Clearing previous zone memory after {Duration:F1}s stable in zone ({X},{Y})",
                stableZoneDuration, _lastStableZone.X, _lastStableZone.Y);
            _previousZone = null; // Clear previous zone - we're fully established in current zone
        }
        
        // Check if we're blocked and have moved far enough to clear the blocking
        if (_isBlockedFromPreviousZone && _previousZone != null)
        {
            // Check for timeout
            var blockingDuration = (DateTime.UtcNow - _blockingStartTime).TotalSeconds;
            if (blockingDuration >= ClientConfiguration.OneWayTimeoutSeconds)
            {
                _logger.LogInformation("[ONE_WAY_HYSTERESIS] Clearing blocking - timeout after {Duration:F1} seconds",
                    blockingDuration);
                
                _isBlockedFromPreviousZone = false;
                _blockedBoundaryNormal = Vector2.Zero;
                _blockingStartTime = DateTime.MinValue;
                
                // Fire event to clear visual indicators
                OneWayBoundaryStateChanged?.Invoke(null, Vector2.Zero, false);
            }
            else
            {
                // Calculate how far we are from the previous zone boundary
                var distanceFromPreviousZone = CalculateDistanceFromBoundary(position, _lastStableZone, _previousZone);
                
                // If we've moved more than the threshold units into our current zone, clear the blocking
                if (distanceFromPreviousZone >= ClientConfiguration.ZoneReentryThresholdUnits)
                {
                    _logger.LogInformation("[ONE_WAY_HYSTERESIS] Clearing blocking - moved {Distance:F2} units into zone ({X},{Y}), threshold: {Threshold}",
                        distanceFromPreviousZone, _lastStableZone.X, _lastStableZone.Y, ClientConfiguration.ZoneReentryThresholdUnits);
                    
                    _isBlockedFromPreviousZone = false;
                    _blockedBoundaryNormal = Vector2.Zero;
                    _blockingStartTime = DateTime.MinValue;
                    
                    // Fire event to clear visual indicators
                    OneWayBoundaryStateChanged?.Invoke(null, Vector2.Zero, false);
                }
                else
                {
                    _logger.LogTrace("[HYSTERESIS_DEBUG] Still blocked - distance from previous zone: {Distance:F2}, threshold: {Threshold}, time remaining: {TimeLeft:F1}s",
                        distanceFromPreviousZone, ClientConfiguration.ZoneReentryThresholdUnits, ClientConfiguration.OneWayTimeoutSeconds - blockingDuration);
                }
            }
        }
        
        _logger.LogTrace("[HYSTERESIS_DEBUG] Staying in same zone ({X},{Y})", detectedZone.X, detectedZone.Y);
        return detectedZone;
    }
    
    private float CalculateDistanceFromBoundary(Vector2 position, GridSquare targetZone, GridSquare fromZone)
    {
        // Calculate perpendicular distance from the shared boundary into the target zone
        const float gridSize = 500f; // GridSquare.Size
        
        // Determine which boundary is shared
        if (targetZone.X != fromZone.X)
        {
            // Vertical boundary (X differs)
            if (targetZone.X > fromZone.X)
            {
                // Moving right, measure from left edge of target zone
                return position.X - (targetZone.X * gridSize);
            }
            else
            {
                // Moving left, measure from right edge of target zone
                return ((targetZone.X + 1) * gridSize) - position.X;
            }
        }
        else if (targetZone.Y != fromZone.Y)
        {
            // Horizontal boundary (Y differs)
            if (targetZone.Y > fromZone.Y)
            {
                // Moving up, measure from bottom edge of target zone
                return position.Y - (targetZone.Y * gridSize);
            }
            else
            {
                // Moving down, measure from top edge of target zone
                return ((targetZone.Y + 1) * gridSize) - position.Y;
            }
        }
        
        // Not adjacent zones - return a negative value to indicate invalid
        return -1f;
    }
    
    private Vector2 CalculateBoundaryNormal(GridSquare fromZone, GridSquare toZone)
    {
        // Calculate normal vector pointing from blocked zone into current zone
        if (toZone.X > fromZone.X)
            return new Vector2(-1, 0); // Blocking right entry, push left
        else if (toZone.X < fromZone.X)
            return new Vector2(1, 0);  // Blocking left entry, push right
        else if (toZone.Y > fromZone.Y)
            return new Vector2(0, -1); // Blocking up entry, push down
        else if (toZone.Y < fromZone.Y)
            return new Vector2(0, 1);  // Blocking down entry, push up
        
        return Vector2.Zero;
    }
    
    private Vector2 ApplyBouncyWallPhysics(Vector2 velocity, Vector2 boundaryNormal)
    {
        // Apply elastic collision with boundary
        // Reflect velocity component perpendicular to boundary, keep parallel component
        
        if (velocity == Vector2.Zero || boundaryNormal == Vector2.Zero)
            return velocity;
        
        // Calculate dot product to find perpendicular component
        float dotProduct = Vector2.Dot(velocity, boundaryNormal);
        
        // If moving away from boundary, don't modify
        if (dotProduct >= 0)
            return velocity;
        
        // Reflect perpendicular component with elasticity coefficient
        const float elasticity = 0.8f; // Slightly dampen to prevent infinite bouncing
        Vector2 reflected = velocity - boundaryNormal * (2f * elasticity * dotProduct);
        
        _logger.LogDebug("[BOUNCY_WALL] Applied physics - Original: {Original}, Normal: {Normal}, Reflected: {Reflected}",
            velocity, boundaryNormal, reflected);
        
        return reflected;
    }

    public async Task SendPlayerInput(Vector2 moveDirection, bool isShooting)
    {
        if (_gameGrain == null || !IsConnected || string.IsNullOrEmpty(PlayerId))
        {
            return;
        }
        
        // Apply bouncy wall physics if blocked from previous zone
        if (_isBlockedFromPreviousZone && _blockedBoundaryNormal != Vector2.Zero)
        {
            moveDirection = ApplyBouncyWallPhysics(moveDirection, _blockedBoundaryNormal);
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
    
    public void SendPlayerInputEx(Vector2? moveDirection, Vector2? shootDirection)
    {
        if (_gameGrain == null || !IsConnected || string.IsNullOrEmpty(PlayerId))
        {
            return;
        }
        
        // Apply bouncy wall physics if blocked from previous zone
        if (moveDirection.HasValue && _isBlockedFromPreviousZone && _blockedBoundaryNormal != Vector2.Zero)
        {
            moveDirection = ApplyBouncyWallPhysics(moveDirection.Value, _blockedBoundaryNormal);
        }
        
        // Track last input for zone transitions
        if (moveDirection.HasValue)
        {
            _lastInputDirection = moveDirection.Value;
        }
        _lastInputShooting = shootDirection.HasValue;
        
        try
        {
            // Double-check connection status before sending
            if (_gameGrain == null || !IsConnected || string.IsNullOrEmpty(PlayerId))
            {
                _logger.LogDebug("[INPUT_SEND] Skipping input - not connected or no game grain");
                return;
            }
            
            // Debug log to track which player is sending input
            if (moveDirection.HasValue || shootDirection.HasValue)
            {
                _logger.LogDebug("[INPUT_SEND] Instance {InstanceId} - Player {PlayerId} sending input - Move: {Move}, Shoot: {Shoot}", 
                    this.GetHashCode(), PlayerId, moveDirection.HasValue, shootDirection.HasValue);
            }
            
            // Use a reasonable timeout and fire-and-forget approach for input
            // Don't wait for the response to avoid blocking the bot loop
            var inputTask = _gameGrain.UpdatePlayerInputEx(PlayerId, moveDirection, shootDirection);
            
            // Set up timeout handling without blocking
            _ = Task.Run(async () =>
            {
                try
                {
                    await inputTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("Player input RPC timed out after 5 seconds");
                    IsConnected = false;
                    ServerChanged?.Invoke("Connection lost");
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug("Player input RPC was cancelled");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Player input RPC failed");
                    // Check if this is a connection error
                    if (ex.Message.Contains("Not connected") || ex.Message.Contains("RPC client is not connected"))
                    {
                        IsConnected = false;
                        ServerChanged?.Invoke("Connection lost");
                    }
                }
            });
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
    
    private DateTime? _lastWorldStateReceived;
    private const int WORLD_STATE_TIMEOUT_SECONDS = 10;
    
    private async Task PollWorldState()
    {
        if (_gameGrain == null || !IsConnected || _cancellationTokenSource?.Token.IsCancellationRequested == true || _isTransitioning)
        {
            _logger.LogTrace("PollWorldState skipped: GameGrain={GameGrain}, IsConnected={IsConnected}, IsCancelled={IsCancelled}, IsTransitioning={IsTransitioning}",
                _gameGrain != null, IsConnected, _cancellationTokenSource?.Token.IsCancellationRequested ?? true, _isTransitioning);
            return;
        }
        
        // Double-check RPC client connection status
        if (_rpcClient != null && _rpcClient.GetType().GetProperty("IsConnected")?.GetValue(_rpcClient) is bool rpcConnected && !rpcConnected)
        {
            _logger.LogWarning("[RPC_CHECK] RPC client reports disconnected, skipping poll and marking connection as lost");
            IsConnected = false;
            RunSafeFireAndForget(async () => await AttemptReconnection(), "AttemptReconnection");
            return;
        }
        
        // Check for stale world state timeout
        if (_lastWorldStateReceived.HasValue)
        {
            var timeSinceLastUpdate = DateTime.UtcNow - _lastWorldStateReceived.Value;
            if (timeSinceLastUpdate.TotalSeconds > WORLD_STATE_TIMEOUT_SECONDS)
            {
                _logger.LogError("[WORLD_STATE] No updates for {Seconds}s - connection likely broken",
                    timeSinceLastUpdate.TotalSeconds);
                
                // Don't wait for zone mismatch - directly verify connection
                RunSafeFireAndForget(async () => await ValidateAndCorrectZoneConnection(), "ValidateAndCorrectZoneConnection");
                
                // Reset the timer to avoid repeated triggers
                _lastWorldStateReceived = DateTime.UtcNow;
            }
        }
        else if (IsConnected)
        {
            // If we've never received world state but should be connected, that's also a problem
            var timeSinceConnection = DateTime.UtcNow - _connectionStartTime;
            if (timeSinceConnection.TotalSeconds > WORLD_STATE_TIMEOUT_SECONDS)
            {
                _logger.LogError("[WORLD_STATE] Never received world state after {Seconds}s of connection - triggering recovery",
                    timeSinceConnection.TotalSeconds);
                
                // Initialize the timestamp and trigger recovery
                _lastWorldStateReceived = DateTime.UtcNow;
                RunSafeFireAndForget(async () => await ValidateAndCorrectZoneConnection(), "ValidateAndCorrectZoneConnection");
            }
        }
        
        _logger.LogTrace("PollWorldState executing for player {PlayerId}", PlayerId);

        try
        {
            _lastWorldStatePollTime = DateTime.UtcNow;

            // Add timeout to prevent hung RPC calls from blocking forever
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var worldState = await _gameGrain.GetWorldState().WaitAsync(cts.Token);
            if (worldState != null)
            {
                // Update timestamp for successful world state receipt
                _lastWorldStateReceived = DateTime.UtcNow;
                
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
                        // Detect position jumps (e.g., from reconnection or server-side teleportation)
                        const float MAX_POSITION_JUMP = 150f; // Maximum allowed position change per update
                        const float RECONNECTION_GRACE_PERIOD = 2.0f; // Seconds after reconnection to allow position sync
                        
                        if (_lastKnownPlayerPosition.HasValue)
                        {
                            var timeSinceLastUpdate = (DateTime.UtcNow - _lastPositionUpdateTime).TotalSeconds;
                            var positionDelta = _lastKnownPlayerPosition.Value.DistanceTo(playerEntity.Position);
                            
                            // Check for abnormal position jump
                            if (positionDelta > MAX_POSITION_JUMP)
                            {
                                _logger.LogWarning("[POSITION_JUMP] Detected position jump of {Delta:F2} units from {OldPos} to {NewPos} after {Time:F2}s.",
                                    positionDelta, _lastKnownPlayerPosition.Value, playerEntity.Position, timeSinceLastUpdate);
                                
                                // Determine the cause and handle appropriately
                                if (timeSinceLastUpdate > 10.0f)
                                {
                                    // Extended disconnection - likely respawn or server reset
                                    _logger.LogInformation("[POSITION_SYNC] Accepting server position after extended disconnection ({Time:F2}s) - likely respawn",
                                        timeSinceLastUpdate);
                                    
                                    // Reset zone tracking to prevent confusion
                                    _lastStableZone = null;
                                    _previousZone = null;
                                    _currentZone = GridSquare.FromPosition(playerEntity.Position);
                                    _isBlockedFromPreviousZone = false;
                                }
                                else if (playerEntity.Position.X < 1f && playerEntity.Position.Y > 499f && playerEntity.Position.Y < 501f)
                                {
                                    // This looks like spawn point (0.15, 500) - player likely died/respawned
                                    _logger.LogInformation("[POSITION_SYNC] Player appears to have respawned at spawn point {Position}", 
                                        playerEntity.Position);
                                    
                                    // Reset zone tracking for fresh start
                                    _lastStableZone = null;
                                    _previousZone = null;
                                    _currentZone = GridSquare.FromPosition(playerEntity.Position);
                                    _isBlockedFromPreviousZone = false;
                                }
                                else if (timeSinceLastUpdate < RECONNECTION_GRACE_PERIOD)
                                {
                                    // Recent update with large jump - possible zone transition or teleportation
                                    _logger.LogDebug("[POSITION_SYNC] Position correction within grace period - accepting server position");
                                }
                            }
                        }
                        
                        // Update tracked position
                        _lastKnownPlayerPosition = playerEntity.Position;
                        _lastPositionUpdateTime = DateTime.UtcNow;
                        
                        // Report position update to health monitor
                        _healthMonitor?.UpdatePlayerPosition(playerEntity.Position);
                        
                        var detectedZone = GridSquare.FromPosition(playerEntity.Position);
                        var playerZone = ApplyOneWayHysteresis(detectedZone, playerEntity.Position);
                        
                        // Check if player's actual zone differs from the server's zone
                        if (_currentZone != null && (playerZone.X != _currentZone.X || playerZone.Y != _currentZone.Y))
                        {
                            // Process zone changes immediately without debouncing
                            var now = DateTime.UtcNow;
                            var timeSinceLastChange = (now - _lastZoneChangeTime).TotalSeconds;
                            
                            // Only update tracking if this is a truly new zone change (avoid spam)
                            var isNewZoneChange = _lastDetectedZone == null || 
                                _lastDetectedZone.X != playerZone.X || 
                                _lastDetectedZone.Y != playerZone.Y;
                                
                            if (isNewZoneChange)
                            {
                                _lastZoneChangeTime = now;
                                _lastDetectedZone = playerZone;
                                
                                // Zone change is happening (playerZone != _currentZone)
                                // This means hysteresis did NOT block the transition
                                // The hysteresis logic has already updated _previousZone and _lastStableZone
                                _zoneEntryTime = now;
                                
                                // Check how long the transition is taking for appropriate log level
                                var transitionTime = _zoneTransitionStartTime != DateTime.MinValue 
                                    ? (DateTime.UtcNow - _zoneTransitionStartTime).TotalSeconds 
                                    : 0;
                                
                                // Only log if transition has been going on for more than a minimal grace period
                                // This prevents spam during the initial connection setup
                                const double gracePeriod = 0.5; // 500ms grace period before logging
                                
                                if (transitionTime > gracePeriod || !_isTransitioning)
                                {
                                    var isTransitioningSlow = transitionTime > ClientConfiguration.ZoneTransitionWarningTimeSeconds;
                                    var logLevel = isTransitioningSlow ? LogLevel.Warning : LogLevel.Information;
                                    
                                    var transitionStatus = _isTransitioning ? $"transitioning for {transitionTime:F1}s" : "starting transition";
                                    var messagePrefix = isTransitioningSlow ? "SLOW ZONE TRANSITION" : "ZONE TRANSITION";
                                    
                                    _logger.Log(logLevel, "[{MessagePrefix}] Ship moved to zone ({ShipX},{ShipY}) but client still connected to server for zone ({ServerX},{ServerY}) at position {Position} ({TransitionStatus})", 
                                        messagePrefix,
                                        playerZone.X, playerZone.Y,
                                        _currentZone?.X ?? -1, 
                                        _currentZone?.Y ?? -1, 
                                        playerEntity.Position,
                                        transitionStatus);
                                }
                                
                                // DON'T update _currentZone yet - wait for server transition to complete
                                // This prevents the health monitor from seeing zone mismatches
                                
                                // Schedule server transition check (don't use Task.Run in hot path)
                                _ = CheckForServerTransition();
                            }
                            else
                            {
                                _logger.LogDebug("[CLIENT_ZONE_CHANGE] Still in zone mismatch to ({X},{Y}) - last change was {Seconds}s ago", 
                                    playerZone.X, playerZone.Y, timeSinceLastChange);
                                
                                // Still call CheckForServerTransition in case previous transition failed
                                _ = CheckForServerTransition();
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
                    
                    // Record that we received a world state update
                    _healthMonitor?.RecordWorldStateReceived();
                    
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
            
            // Check if this is a disconnection error
            if (ex is InvalidOperationException && ex.Message.Contains("RPC client is not connected"))
            {
                _logger.LogWarning("[RPC_DISCONNECT] RPC client disconnected, marking connection as lost");
                IsConnected = false;
                
                // Immediately attempt reconnection for RPC disconnection
                if (_worldStatePollFailures == 1)
                {
                    _logger.LogInformation("[RPC_RECONNECT] Attempting immediate reconnection after RPC disconnect");
                    RunSafeFireAndForget(async () => await AttemptReconnection(), "AttemptReconnection");
                }
            }
            
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
                RunSafeFireAndForget(async () => await AttemptReconnection(), "AttemptReconnection");
            }
        }
    }
    
    private DateTime _lastSuccessfulHeartbeat = DateTime.UtcNow;
    private DateTime _connectionStartTime = DateTime.UtcNow;
    private const int HEARTBEAT_TIMEOUT_SECONDS = 10;
    
    private async Task SendHeartbeat()
    {
        if (_gameGrain == null || !IsConnected || _cancellationTokenSource?.Token.IsCancellationRequested == true)
        {
            _logger.LogDebug("[HEARTBEAT] Skipping - not connected or shutting down");
            return;
        }
        
        _logger.LogDebug("[HEARTBEAT] Sending heartbeat...");
        try
        {
            // Use the new lightweight GetServerTime method for heartbeat
            var serverTime = await _gameGrain.GetServerTime().WaitAsync(TimeSpan.FromSeconds(2));
            
            _lastSuccessfulHeartbeat = DateTime.UtcNow;
            _logger.LogInformation("[HEARTBEAT] Server responded at {Time}", serverTime);
        }
        catch (TimeoutException)
        {
            var timeSinceLastHeartbeat = DateTime.UtcNow - _lastSuccessfulHeartbeat;
            
            if (timeSinceLastHeartbeat.TotalSeconds > HEARTBEAT_TIMEOUT_SECONDS)
            {
                _logger.LogError("[HEARTBEAT] No successful heartbeat for {Seconds}s - triggering recovery",
                    timeSinceLastHeartbeat.TotalSeconds);
                
                // Mark connection as broken
                IsConnected = false;
                
                // Trigger recovery
                RunSafeFireAndForget(async () => await RecoverFromBrokenConnection(), "RecoverFromBrokenConnection");
            }
            else
            {
                _logger.LogWarning("[HEARTBEAT] Heartbeat timed out but within tolerance ({Seconds}s since last success)",
                    timeSinceLastHeartbeat.TotalSeconds);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HEARTBEAT] Failed to send heartbeat");
            
            var timeSinceLastHeartbeat = DateTime.UtcNow - _lastSuccessfulHeartbeat;
            if (timeSinceLastHeartbeat.TotalSeconds > HEARTBEAT_TIMEOUT_SECONDS)
            {
                _logger.LogError("[HEARTBEAT] Heartbeat failures exceeded threshold - triggering recovery");
                IsConnected = false;
                RunSafeFireAndForget(async () => await RecoverFromBrokenConnection(), "RecoverFromBrokenConnection");
            }
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
                        RunSafeFireAndForget(async () => await AttemptReconnection(), "AttemptReconnection");
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
            // Use ConnectionResilienceManager for robust reconnection
            var result = await _connectionManager.ExecuteWithReconnect(
                async () => await TestAndRestoreConnection(),
                "reconnection",
                _cancellationTokenSource?.Token ?? CancellationToken.None
            );
            
            if (result != null)
            {
                _logger.LogInformation("Reconnection successful");
            }
            else
            {
                _logger.LogWarning("Reconnection failed after all attempts");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during reconnection attempt");
        }
    }
    
    private async Task RecoverFromBrokenConnection()
    {
        _logger.LogInformation("[RECOVERY] Starting connection recovery process");
        
        if (_isTransitioning)
        {
            _logger.LogInformation("[RECOVERY] Already transitioning, skipping recovery");
            return;
        }
        
        try
        {
            // First try to validate and correct zone connection
            await ValidateAndCorrectZoneConnection();
            
            // If that didn't work, try standard reconnection
            if (!IsConnected)
            {
                await AttemptReconnection();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RECOVERY] Failed during connection recovery");
        }
    }
    
    private async Task ValidateAndCorrectZoneConnection()
    {
        _logger.LogInformation("[ZONE_VALIDATION] Checking if connected to correct zone");
        
        try
        {
            // Get authoritative player zone from Orleans
            var authoritativeZone = await GetAuthoritativePlayerZone();
            
            if (authoritativeZone == null)
            {
                _logger.LogWarning("[ZONE_VALIDATION] Could not determine authoritative player zone");
                return;
            }
            
            // Compare with current connection
            if (_currentZone != null && !authoritativeZone.Equals(_currentZone))
            {
                _logger.LogWarning("[ZONE_CORRECTION] Connected to zone {Current} but player is actually in zone {Actual}",
                    _currentZone, authoritativeZone);
                
                // Force transition to correct zone
                await ForceZoneTransition(authoritativeZone);
            }
            else
            {
                _logger.LogInformation("[ZONE_VALIDATION] Already connected to correct zone {Zone}", _currentZone);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ZONE_VALIDATION] Failed to validate zone connection");
        }
    }
    
    private async Task<GridSquare?> GetAuthoritativePlayerZone()
    {
        try
        {
            if (string.IsNullOrEmpty(PlayerId))
            {
                _logger.LogWarning("[ZONE_LOOKUP] No player ID available");
                return null;
            }
            
            // Query the Orleans Silo via HTTP for player info
            var siloUrl = _configuration["SiloUrl"] ?? "https://localhost:7071/";
            if (!siloUrl.EndsWith("/")) siloUrl += "/";
            
            var response = await _httpClient.GetAsync($"{siloUrl}api/world/player/{PlayerId}/info");
            
            if (response.IsSuccessStatusCode)
            {
                var playerInfo = await response.Content.ReadFromJsonAsync<PlayerInfo>();
                if (playerInfo != null)
                {
                    // Calculate zone from player position
                    var playerZone = GridSquare.FromPosition(playerInfo.Position);
                    _logger.LogInformation("[ZONE_LOOKUP] Player {PlayerId} at position {Position} is authoritatively in zone {Zone}",
                        PlayerId, playerInfo.Position, playerZone);
                    return playerZone;
                }
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("[ZONE_LOOKUP] Player {PlayerId} not found in WorldManager", PlayerId);
            }
            else
            {
                _logger.LogWarning("[ZONE_LOOKUP] Failed to get player info: {Status}", response.StatusCode);
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ZONE_LOOKUP] Failed to get authoritative player zone");
            return null;
        }
    }
    
    private async Task ForceZoneTransition(GridSquare targetZone)
    {
        _logger.LogInformation("[FORCE_TRANSITION] Forcing transition to zone {Zone}", targetZone);

        // CRITICAL FIX: Check if a transition is already in progress using the lock
        // This prevents race conditions between normal and forced transitions
        bool shouldProceed;
        lock (_transitionLock)
        {
            if (_isTransitioning)
            {
                _logger.LogWarning("[FORCE_TRANSITION] Skipping forced transition - a transition is already in progress");
                return;
            }

            // Debouncing: Prevent rapid-fire forced reconnections
            // Only allow one forced transition every 10 seconds
            var timeSinceLastForced = DateTime.UtcNow - _lastForcedTransitionTime;
            if (timeSinceLastForced.TotalSeconds < 10)
            {
                _logger.LogWarning("[FORCE_TRANSITION] Skipping forced transition - too soon after last forced transition " +
                    "({ElapsedSeconds:F1}s ago, minimum 10s required)", timeSinceLastForced.TotalSeconds);
                return;
            }

            _isTransitioning = true;
            _lastForcedTransitionTime = DateTime.UtcNow;
            shouldProceed = true;
        }

        if (!shouldProceed)
        {
            return;
        }

        // Add timeout to prevent hanging forever
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            // Look up the server for the target zone
            var serverInfo = await GetServerInfoForZone(targetZone);
            if (serverInfo == null)
            {
                _logger.LogError("[FORCE_TRANSITION] No server found for zone {Zone}", targetZone);
                return;
            }

            // Check if we're already connected to the correct server
            if (serverInfo.ServerId == CurrentServerId)
            {
                _logger.LogInformation("[FORCE_TRANSITION] Already connected to correct server {ServerId} for zone {Zone}. " +
                    "Player might be in wrong zone on this server. Attempting reconnect to same server.",
                    serverInfo.ServerId, targetZone);

                // Disconnect and reconnect to the SAME server to reset player state
                if (_gameGrain != null && !string.IsNullOrEmpty(PlayerId))
                {
                    try
                    {
                        // Explicitly disconnect player from server
                        await _gameGrain.DisconnectPlayer(PlayerId);
                        _logger.LogInformation("[FORCE_TRANSITION] Disconnected player {PlayerId} from server {ServerId}",
                            PlayerId, CurrentServerId);

                        // Wait for server to process the disconnect and remove player from simulation
                        // The server's AddPlayer checks if a player sent input in the last 10s and rejects duplicates
                        // We need to wait long enough for the server's simulation tick to process the disconnect
                        await Task.Delay(500);

                        // Reconnect to the same server
                        var reconnectResult = await _gameGrain.ConnectPlayer(PlayerId);
                        if (reconnectResult != "SUCCESS")
                        {
                            _logger.LogError("[FORCE_TRANSITION] Failed to reconnect to same server: {Result}", reconnectResult);
                            // Fall through to full reconnection below
                            Cleanup();
                            await ConnectToActionServer(serverInfo.IpAddress, serverInfo.RpcPort, serverInfo.ServerId);
                        }
                        else
                        {
                            _logger.LogInformation("[FORCE_TRANSITION] Successfully reconnected to same server {ServerId}", CurrentServerId);

                            // CRITICAL: Update zone tracking to prevent health monitor from detecting false mismatch
                            _currentZone = targetZone;
                            _healthMonitor?.UpdateServerZone(targetZone);

                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[FORCE_TRANSITION] Error during same-server reconnect, attempting full reconnect");
                        // Fall through to full reconnection below
                        Cleanup();
                    }
                }
            }

            // Different server - do full disconnect and reconnect
            if (_gameGrain != null)
            {
                try
                {
                    await DisconnectAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[FORCE_TRANSITION] Error disconnecting from current server");
                }
            }

            // Connect to correct server
            await ConnectToActionServer(serverInfo.IpAddress, serverInfo.RpcPort, serverInfo.ServerId);

            // CRITICAL: Update zone tracking to prevent health monitor from detecting false mismatch
            // This is needed for cross-server transitions (same-server path already does this at line 1751)
            _currentZone = targetZone;
            _healthMonitor?.UpdateServerZone(targetZone);

            _logger.LogInformation("[FORCE_TRANSITION] Successfully forced transition to zone {Zone}", targetZone);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _logger.LogError("[FORCE_TRANSITION] Forced transition to zone {Zone} timed out after 10 seconds. " +
                "This may indicate network issues or server overload.", targetZone);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FORCE_TRANSITION] Failed to force transition to zone {Zone}. " +
                "Error type: {ErrorType}, Message: {Message}",
                targetZone, ex.GetType().Name, ex.Message);
        }
        finally
        {
            lock (_transitionLock)
            {
                _isTransitioning = false;
            }
            _logger.LogDebug("[FORCE_TRANSITION] Released transition lock for zone {Zone}", targetZone);
        }
    }
    
    private async Task<ActionServerInfo?> GetServerInfoForZone(GridSquare zone)
    {
        try
        {
            // Query via HTTP API
            var siloUrl = _configuration["SiloUrl"] ?? "https://localhost:7071/";
            if (!siloUrl.EndsWith("/")) siloUrl += "/";
            
            var response = await _httpClient.GetAsync($"{siloUrl}api/world/action-servers");
            
            if (response.IsSuccessStatusCode)
            {
                var servers = await response.Content.ReadFromJsonAsync<List<ActionServerInfo>>();
                return servers?.FirstOrDefault(s => s.AssignedSquare.Equals(zone));
            }
            
            _logger.LogWarning("[ZONE_LOOKUP] Failed to get action servers: {Status}", response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get server info for zone {Zone}", zone);
            return null;
        }
    }
    
    private async Task<object> TestAndRestoreConnection()
    {
        _logger.LogInformation("Testing connection to current server");
        
        // First try to test the current connection
        if (_gameGrain != null)
        {
            try
            {
                // Add timeout to prevent hanging on connection test
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var testState = await _gameGrain.GetWorldState().WaitAsync(cts.Token);
                if (testState != null)
                {
                    _logger.LogInformation("Connection test successful, resetting failure count and restarting timers");
                    _worldStatePollFailures = 0;
                    IsConnected = true;  // Fix: Set connection status to true since test succeeded
                    _connectionManager.MarkConnectionSuccess();
                    
                    // Reset position tracking after reconnection to prevent false jump detection
                    _lastKnownPlayerPosition = null;
                    _lastPositionUpdateTime = DateTime.MinValue;
                    _logger.LogDebug("[RECONNECTION] Reset position tracking after successful reconnection");
                    
                    // Restart timers using the timer manager
                    _timerManager.RestartAllTimers();
                    _logger.LogInformation("Restarted all timers after successful connection test");
                    
                    return new object(); // Return non-null to indicate success
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Connection test failed, proceeding with reconnection");
                _connectionManager.MarkConnectionFailure("Connection test failed");
            }
        }
        
        // Force a zone check to trigger proper reconnection
        await CheckForServerTransition();
        
        // If we successfully reconnected during zone check, return success
        if (IsConnected)
        {
            return new object();
        }
        
        // Last resort: Force restart timers even if we can't restore full connection
        // This prevents the UI from being permanently stuck
        _logger.LogWarning("Connection restore failed, force restarting timers to prevent UI freeze");
        try
        {
            _timerManager.RestartAllTimers();
            _logger.LogInformation("Force restarted timers despite connection issues");
            
            // Mark as connected to allow timer operation, even if connection is degraded
            IsConnected = true;
            return new object();
        }
        catch (Exception timerEx)
        {
            _logger.LogError(timerEx, "Failed to force restart timers");
        }
        
        throw new InvalidOperationException("Failed to restore connection");
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
        
        // Check if we've been in the wrong zone for too long (force transition after 5 seconds)
        // Only check timing if we have valid zone change time (not after respawn)
        var forceTransition = false;
        var timeSinceZoneChange = 0.0;
        if (_lastZoneChangeTime != DateTime.MinValue)
        {
            timeSinceZoneChange = (DateTime.UtcNow - _lastZoneChangeTime).TotalSeconds;
            forceTransition = timeSinceZoneChange > 5.0;
        }
        
        if (forceTransition)
        {
            _logger.LogWarning("[ZONE_TRANSITION] FORCING transition after {Time:F1}s in wrong zone. Player in ({PlayerX},{PlayerY}) but connected to ({ServerX},{ServerY})",
                timeSinceZoneChange, playerZone.X, playerZone.Y, _currentZone?.X ?? -1, _currentZone?.Y ?? -1);
            
            // Bypass debouncer and force the transition
            await PerformZoneTransitionDebounced(playerZone, playerEntity.Position);
            return;
        }
        
        // Use debouncer to prevent rapid transitions
        var debounceStart = DateTime.UtcNow;
        var shouldTransition = await _zoneDebouncer.ShouldTransitionAsync(
            playerZone,
            playerEntity.Position,
            async () => await PerformZoneTransitionDebounced(playerZone, playerEntity.Position)
        );
        var debounceDuration = (DateTime.UtcNow - debounceStart).TotalMilliseconds;
        
        _logger.LogDebug("[ZONE_TRANSITION] Debouncer took {Duration}ms, allowed transition: {Allowed}", 
            debounceDuration, shouldTransition);
        
        if (!shouldTransition)
        {
            _logger.LogDebug("[ZONE_TRANSITION] Transition to zone ({X},{Y}) prevented by debouncer", 
                playerZone.X, playerZone.Y);
        }
    }
    
    private async Task PerformZoneTransitionDebounced(GridSquare playerZone, Vector2 playerPosition)
    {
        _isTransitioning = true;
        
        try
        {
            _logger.LogTrace("Checking for server transition for player {PlayerId} at position {Position} in zone ({ZoneX},{ZoneY})", 
                PlayerId, playerPosition, playerZone.X, playerZone.Y);
            
            // Query the Orleans silo for the correct server
            var serverLookupStart = DateTime.UtcNow;
            var response = await _httpClient.GetFromJsonAsync<Shooter.Shared.Models.ActionServerInfo>(
                $"api/world/players/{PlayerId}/server");
            var serverLookupDuration = (DateTime.UtcNow - serverLookupStart).TotalMilliseconds;
            
            _logger.LogDebug("[ZONE_TRANSITION] Server lookup took {Duration}ms for player {PlayerId}", 
                serverLookupDuration, PlayerId);
                
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
                
                // Track transition start time
                _zoneTransitionStartTime = DateTime.UtcNow;
                
                _logger.LogInformation("[ZONE_TRANSITION] Player {PlayerId} needs to transition from server {OldServer} to {NewServer} for zone ({ZoneX},{ZoneY})", 
                    PlayerId, CurrentServerId, response.ServerId, response.AssignedSquare.X, response.AssignedSquare.Y);
                
                var cleanupStart = DateTime.UtcNow;
                
                // Use timer manager to protect timers during transition
                using (var transitionScope = _timerManager.BeginTransition($"zone change to {response.AssignedSquare.X},{response.AssignedSquare.Y}"))
                {
                    // Timers are now paused, not disposed
                
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
                    // Don't reset _lastZoneChangeTime to prevent astronomical time calculations
                } // transitionScope disposed here, timers resume
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
        // DON'T update _currentZone yet - wait for connection verification to complete
        
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
                        
                        // Register the network statistics tracker BEFORE configuring transport
                        if (_clientNetworkTracker != null)
                        {
                            rpcBuilder.Services.AddSingleton<Granville.Rpc.Telemetry.INetworkStatisticsTracker>(_clientNetworkTracker);
                        }
                        
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
                                rpcBuilder.UseLiteNetLib()
                                    .ConfigureLiteNetLib(options =>
                                    {
                                        options.ConnectionTimeoutMs = 5000; // 5 seconds instead of 120 in DEBUG
                                        options.PollingIntervalMs = 15;
                                    });
                                TransportType = "litenetlib";
                                break;
                        }
                    })
                    .ConfigureServices(services =>
                    {
                        services.AddSerializer(serializer =>
                        {
                            serializer.AddAssembly(typeof(IGameGranule).Assembly);
                            // Add RPC protocol assembly for RPC message serialization
                            serializer.AddAssembly(typeof(Granville.Rpc.Protocol.RpcMessage).Assembly);
                            // Add Shooter.Shared assembly for game models (Player, WorldState, etc.)
                            serializer.AddAssembly(typeof(PlayerInfo).Assembly);
                        });
                    })
                    .Build();
                
                // Start the host without awaiting - it runs in the background
                _ = hostBuilder.RunAsync();
                
                // Wait a bit for the host to start up
                await Task.Delay(100);
                _rpcHost = hostBuilder;
                
                // Defer getting the RPC client service to avoid potential blocking
                try
                {
                    _rpcClient = hostBuilder.Services.GetRequiredService<Granville.Rpc.IRpcClient>();
                }
                catch (Exception serviceEx)
                {
                    _logger.LogWarning(serviceEx, "[ZONE_TRANSITION] Failed to get RPC client service immediately, retrying after delay");
                    await Task.Delay(500);
                    _rpcClient = hostBuilder.Services.GetRequiredService<Granville.Rpc.IRpcClient>();
                }
                
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
                        
                        var getGrainTask = Task.Run(() => _rpcClient.GetGrain<IGameGranule>("game"), combinedCts.Token);
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
                    
                    // Register the network statistics tracker BEFORE configuring transport
                    if (_clientNetworkTracker != null)
                    {
                        rpcBuilder.Services.AddSingleton<Granville.Rpc.Telemetry.INetworkStatisticsTracker>(_clientNetworkTracker);
                    }
                    
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
                            rpcBuilder.UseLiteNetLib()
                                .ConfigureLiteNetLib(options =>
                                {
                                    options.ConnectionTimeoutMs = 5000; // 5 seconds instead of 120 in DEBUG
                                    options.PollingIntervalMs = 15;
                                });
                            TransportType = "litenetlib"; // Ensure we always have a value
                            break;
                    }
                })
                .ConfigureServices(services =>
                {
                    services.AddSerializer(serializer =>
                    {
                        serializer.AddAssembly(typeof(IGameGranule).Assembly);
                        // Add RPC protocol assembly for RPC message serialization
                        serializer.AddAssembly(typeof(Granville.Rpc.Protocol.RpcMessage).Assembly);
                        // Add Shooter.Shared assembly for game models (Player, WorldState, etc.)
                        serializer.AddAssembly(typeof(PlayerInfo).Assembly);
                    });
                })
                .Build();
                
            // Start the host without awaiting - it runs in the background
            _ = hostBuilder.RunAsync();
            
            // Wait a bit for the host to start up
            await Task.Delay(100);
            
            _rpcHost = hostBuilder;
            
            // Defer getting the RPC client service to avoid potential blocking
            try
            {
                _rpcClient = hostBuilder.Services.GetRequiredService<Granville.Rpc.IRpcClient>();
            }
            catch (Exception serviceEx)
            {
                _logger.LogWarning(serviceEx, "[ZONE_TRANSITION] Failed to get RPC client service immediately, retrying after delay");
                await Task.Delay(500);
                _rpcClient = hostBuilder.Services.GetRequiredService<Granville.Rpc.IRpcClient>();
            }
            
            
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
                    var getGrainTask = Task.Run(() => _rpcClient.GetGrain<IGameGranule>("game"), combinedCts.Token);
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
                
                // Log transition completion with timing info
                var transitionDuration = _zoneTransitionStartTime != DateTime.MinValue 
                    ? (DateTime.UtcNow - _zoneTransitionStartTime).TotalSeconds 
                    : -1;
                
                if (transitionDuration >= 0)
                {
                    var logLevel = transitionDuration > ClientConfiguration.ZoneTransitionWarningTimeSeconds 
                        ? LogLevel.Warning 
                        : LogLevel.Information;
                    
                    _logger.Log(logLevel, 
                        "[ZONE_TRANSITION] Successfully connected to zone ({X},{Y}) in {Duration:F2}s - Test GetWorldState got {Count} entities", 
                        serverInfo.AssignedSquare.X, serverInfo.AssignedSquare.Y, transitionDuration, testState?.Entities?.Count ?? 0);
                    
                    // NOW we can safely update _currentZone since connection is verified
                    _currentZone = serverInfo.AssignedSquare;
                }
                else
                {
                    _logger.LogInformation("Test GetWorldState succeeded, got {Count} entities", testState?.Entities?.Count ?? 0);
                    
                    // NOW we can safely update _currentZone since connection is verified
                    _currentZone = serverInfo.AssignedSquare;
                }
                
                // Clear transition timing
                _zoneTransitionStartTime = DateTime.MinValue;
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
            _logger.LogWarning("[ZONE_TRANSITION] Test GetWorldState timed out after 3 seconds, but continuing with zone transition");
            // Update zone anyway to prevent being stuck - the connection might still be valid
            _currentZone = serverInfo.AssignedSquare;
            _zoneTransitionStartTime = DateTime.MinValue;
            // Don't set IsConnected = false; let the connection remain and see if it recovers
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[ZONE_TRANSITION] Test GetWorldState was cancelled, but continuing with zone transition");
            // Update zone anyway to prevent being stuck
            _currentZone = serverInfo.AssignedSquare;
            _zoneTransitionStartTime = DateTime.MinValue;
            // Don't set IsConnected = false; let the connection remain
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ZONE_TRANSITION] Test GetWorldState failed, but tentatively updating zone to prevent deadlock");
            // CRITICAL FIX: Update zone even on failure to prevent being permanently stuck
            _currentZone = serverInfo.AssignedSquare;
            _zoneTransitionStartTime = DateTime.MinValue;
            // Keep connection but mark as potentially unstable
            _logger.LogWarning("[ZONE_TRANSITION] Connection may be unstable, will retry if needed");
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
        
        // Use RobustTimerManager to create protected timers
        _timerManager.CreateTimer("worldState", _ => { RunSafeFireAndForget(async () => await PollWorldState(), "PollWorldState"); }, 33); // 30 FPS
        _timerManager.CreateTimer("heartbeat", _ => { RunSafeFireAndForget(async () => await SendHeartbeat(), "SendHeartbeat"); }, 5000);
        _timerManager.CreateTimer("availableZones", _ => { RunSafeFireAndForget(async () => await PollAvailableZones(), "PollAvailableZones"); }, 10000);
        _timerManager.CreateTimer("networkStats", _ => { RunSafeFireAndForget(async () => await PollNetworkStats(), "PollNetworkStats"); }, 1000);
        _timerManager.CreateTimer("watchdog", _ => { RunSafeFireAndForget(async () => await CheckPollingHealth(), "CheckPollingHealth"); }, 5000);
        
        // Restart chat polling if it was active
        if (_observer == null)
        {
            StartChatPolling();
        }
        
        // Reset sequence number for new server
        _lastSequenceNumber = -1;
        
        // Reset polling health tracking
        _worldStatePollFailures = 0;
        _lastWorldStatePollTime = DateTime.UtcNow;
        
        // Reset position tracking for new server
        _lastKnownPlayerPosition = null;
        _lastPositionUpdateTime = DateTime.MinValue;
        
        // Notify about server change
        ServerChanged?.Invoke(CurrentServerId);
        
        _logger.LogInformation("Successfully reconnected to new server {ServerId}", CurrentServerId);
        
        // Update health monitor with the new server zone
        _healthMonitor?.UpdateServerZone(_currentZone);
        
        // Mark this pre-established connection as recently used to prevent cleanup
        var currentZoneKey = $"{serverInfo.AssignedSquare.X},{serverInfo.AssignedSquare.Y}";
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
                    
                    // Register the network statistics tracker BEFORE configuring transport
                    if (_clientNetworkTracker != null)
                    {
                        rpcBuilder.Services.AddSingleton<Granville.Rpc.Telemetry.INetworkStatisticsTracker>(_clientNetworkTracker);
                    }
                    
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
                            rpcBuilder.UseLiteNetLib()
                                .ConfigureLiteNetLib(options =>
                                {
                                    options.ConnectionTimeoutMs = 5000; // 5 seconds instead of 120 in DEBUG
                                    options.PollingIntervalMs = 15;
                                });
                            TransportType = "litenetlib"; // Ensure we always have a value
                            break;
                    }
                })
                .ConfigureServices(services =>
                {
                    services.AddSerializer(serializer =>
                    {
                        serializer.AddAssembly(typeof(IGameGranule).Assembly);
                        // Add RPC protocol assembly for RPC message serialization
                        serializer.AddAssembly(typeof(Granville.Rpc.Protocol.RpcMessage).Assembly);
                        // Add Shooter.Shared assembly for game models (Player, WorldState, etc.)
                        serializer.AddAssembly(typeof(PlayerInfo).Assembly);
                    });
                })
                .Build();
                
            // Start the host without awaiting - it runs in the background
            _ = hostBuilder.RunAsync();
            
            // Wait a bit for the host to start up
            await Task.Delay(100);
            
            connection.RpcHost = hostBuilder;
            
            // Defer getting the RPC client service to avoid potential blocking
            try
            {
                connection.RpcClient = hostBuilder.Services.GetRequiredService<Granville.Rpc.IRpcClient>();
            }
            catch (Exception serviceEx)
            {
                _logger.LogWarning(serviceEx, "Failed to get RPC client service immediately, retrying after delay");
                await Task.Delay(500);
                connection.RpcClient = hostBuilder.Services.GetRequiredService<Granville.Rpc.IRpcClient>();
            }
            
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
                    connection.GameGrain = connection.RpcClient.GetGrain<IGameGranule>("game");
                    
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
            
            // Report connection status to health monitor
            _healthMonitor?.UpdatePreEstablishedConnection(serverInfo.ServerId, connection.IsConnected, false);
            
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
        
        // Reset zone transition state to prevent stale timing issues after respawn
        _lastZoneChangeTime = DateTime.MinValue;
        _lastDetectedZone = null;
        _zoneEntryTime = DateTime.MinValue;
        
        // Reset health monitor to clear any stale zone tracking
        _healthMonitor?.UpdateServerZone(null);
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
        // Use HostBuilder instead of Host.CreateDefaultBuilder to avoid console lifetime
        var hostBuilder = new HostBuilder()
            .ConfigureLogging(logging =>
            {
                // Clear default providers to avoid duplicate logs
                logging.ClearProviders();
                
                // Configure logging to match parent application settings
                logging.SetMinimumLevel(LogLevel.Debug);
                logging.AddFilter("Granville.Rpc", LogLevel.Debug);
                logging.AddFilter("Orleans", LogLevel.Information);
                logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
                
                // Add console logging for immediate visibility
                logging.AddConsole();
                
                // Add debug logging for development
                logging.AddDebug();
            })
            .UseOrleansRpcClient(rpcBuilder =>
            {
                rpcBuilder.ConnectTo(resolvedHost, rpcPort);
                
                // Register the network statistics tracker BEFORE configuring transport
                // Only for the main connection, not pre-established connections
                if (playerId != null && _clientNetworkTracker == null)
                {
                    rpcBuilder.Services.AddSingleton<Granville.Rpc.Telemetry.INetworkStatisticsTracker>(sp =>
                    {
                        _clientNetworkTracker = new NetworkStatisticsTracker(playerId);
                        return _clientNetworkTracker;
                    });
                }
                
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
                        rpcBuilder.UseLiteNetLib()
                            .ConfigureLiteNetLib(options =>
                            {
                                options.ConnectionTimeoutMs = 5000; // 5 seconds instead of 120 in DEBUG
                                options.PollingIntervalMs = 15;
                            });
                        TransportType = "litenetlib"; // Ensure we always have a value
                        break;
                }
            })
            .ConfigureServices(services =>
            {
                // Add serialization for the grain interfaces and shared models
                services.AddSerializer(serializer =>
                {
                    serializer.AddAssembly(typeof(IGameGranule).Assembly);
                    // Add RPC protocol assembly for RPC message serialization
                    serializer.AddAssembly(typeof(Granville.Rpc.Protocol.RpcMessage).Assembly);
                    // Add Shooter.Shared assembly for game models (PlayerInfo, WorldState, etc.)
                    serializer.AddAssembly(typeof(PlayerInfo).Assembly);
                });
                
                // NOTE: Network statistics tracker is now registered in the rpcBuilder.ConfigureServices above
                // to ensure it's available when the transport is created
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
            RunSafeFireAndForget(async () => await CleanupPreEstablishedConnection(key), $"CleanupPreEstablishedConnection-{key}");
        }
        _preEstablishedConnections.Clear();
        
        // Dispose of the protection components
        _timerManager?.Dispose();
        _zoneDebouncer?.Reset();
        _connectionManager?.Reset();
        _healthReportTimer?.Dispose();
        
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
    
    public void HandleVictoryPause(VictoryPauseMessage victoryPauseMessage)
    {
        _logger.LogInformation("Victory pause started! Countdown: {CountdownSeconds}s", victoryPauseMessage.CountdownSeconds);
        
        // Notify UI about victory pause
        VictoryPauseReceived?.Invoke(victoryPauseMessage);
    }
    
    public void HandleChatMessage(ChatMessage message)
    {
        _logger.LogInformation("Received chat message from {Sender}: {Message}", 
            message.SenderName, message.Message);
        
        // Notify UI about chat message
        ChatMessageReceived?.Invoke(message);
    }
    
    /// <summary>
    /// Safely runs a task in the background with proper exception handling to prevent unobserved task exceptions
    /// </summary>
    private void RunSafeFireAndForget(Func<Task> taskFactory, string operationName)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await taskFactory();
            }
            catch (TimeoutException tex)
            {
                _logger.LogWarning(tex, "[FIRE_AND_FORGET] Operation '{Operation}' timed out", operationName);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("[FIRE_AND_FORGET] Operation '{Operation}' was cancelled", operationName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FIRE_AND_FORGET] Operation '{Operation}' failed", operationName);
            }
        });
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
    
    /// <summary>
    /// Checks if the player has moved to a different zone and initiates a server transition if needed.
    /// This is called periodically from the Game.razor component when world state is updated.
    /// </summary>
    public async Task CheckAndHandleZoneTransition(Vector2 playerPosition)
    {
        if (!IsConnected || _isTransitioning || _gameGrain == null)
        {
            return;
        }
        
        try
        {
            // Determine the player's current zone based on position
            var playerZone = GridSquare.FromPosition(playerPosition);
            
            // If we don't have a current zone recorded, set it
            if (_currentZone == null)
            {
                _currentZone = playerZone;
                _logger.LogInformation("[ZONE_TRANSITION] Initial zone set to ({X},{Y})", playerZone.X, playerZone.Y);
                return;
            }
            
            // Check if player has moved to a different zone
            if (playerZone.X != _currentZone.X || playerZone.Y != _currentZone.Y)
            {
                _logger.LogInformation("[ZONE_TRANSITION] Player moved from zone ({OldX},{OldY}) to ({NewX},{NewY})",
                    _currentZone.X, _currentZone.Y, playerZone.X, playerZone.Y);
                
                // Use the zone transition debouncer to prevent rapid transitions
                if (_zoneTransitionDebouncer == null)
                {
                    _zoneTransitionDebouncer = new ZoneTransitionDebouncer(
                        _loggerFactory.CreateLogger<ZoneTransitionDebouncer>());
                }
                
                // Attempt zone transition with debouncing
                var transitioned = await _zoneTransitionDebouncer.ShouldTransitionAsync(
                    playerZone,
                    playerPosition,
                    async () => await PerformZoneTransition(playerZone));
                
                if (transitioned)
                {
                    _currentZone = playerZone;
                    _logger.LogInformation("[ZONE_TRANSITION] Successfully transitioned to zone ({X},{Y})", 
                        playerZone.X, playerZone.Y);
                    
                    // Report successful transition to health monitor
                    var transitionDuration = DateTime.UtcNow - _zoneTransitionStartTime;
                    _healthMonitor?.RecordTransitionComplete(true, transitionDuration);
                    _healthMonitor?.UpdateServerZone(playerZone);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ZONE_TRANSITION] Error checking zone transition");
        }
    }
    
    /// <summary>
    /// Performs the actual zone transition by connecting to the appropriate server.
    /// </summary>
    private async Task PerformZoneTransition(GridSquare newZone)
    {
        _isTransitioning = true;
        _zoneTransitionStartTime = DateTime.UtcNow;
        
        // Record transition start in health monitor
        _healthMonitor?.RecordTransitionStart(_currentZone ?? newZone, newZone);
        
        try
        {
            _logger.LogInformation("[ZONE_TRANSITION] Starting transition to zone ({X},{Y}) from current zone ({CX},{CY})", 
                newZone.X, newZone.Y, _currentZone?.X ?? -1, _currentZone?.Y ?? -1);
            
            // Query the silo for the correct action server for this zone
            var siloUrl = _configuration["SiloUrl"] ?? "https://localhost:7071";
            if (!siloUrl.EndsWith("/")) siloUrl += "/";
            
            // Get the action server for the NEW zone's center position
            var zoneCenter = newZone.GetCenter();
            var response = await _httpClient.GetAsync($"{siloUrl}api/world/action-servers/for-position?x={zoneCenter.X}&y={zoneCenter.Y}");
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[ZONE_TRANSITION] Failed to get server for zone ({X},{Y}): {Status}", 
                    newZone.X, newZone.Y, response.StatusCode);
                return;
            }
            
            var serverInfo = await response.Content.ReadFromJsonAsync<ActionServerInfo>();
            if (serverInfo == null)
            {
                _logger.LogError("[ZONE_TRANSITION] No server assigned for zone ({X},{Y})", newZone.X, newZone.Y);
                return;
            }
            
            _logger.LogInformation("[ZONE_TRANSITION] Found server {ServerId} for zone ({X},{Y})", 
                serverInfo.ServerId, newZone.X, newZone.Y);
            
            // Check if we're already connected to the correct server
            if (serverInfo.ServerId == CurrentServerId)
            {
                _logger.LogInformation("[ZONE_TRANSITION] Already connected to correct server {ServerId} for zone ({X},{Y})",
                    serverInfo.ServerId, newZone.X, newZone.Y);

                // Still need to update our zone tracking even if server hasn't changed
                _currentZone = newZone;
                _healthMonitor?.UpdateServerZone(newZone);

                return;
            }
            
            _logger.LogInformation("[ZONE_TRANSITION] Switching from server {OldServer} to {NewServer} for zone ({X},{Y})",
                CurrentServerId, serverInfo.ServerId, newZone.X, newZone.Y);
            
            // Save current state
            var oldGameGrain = _gameGrain;
            var oldHost = _rpcHost;
            
            try
            {
                // Connect to new server
                await ConnectToActionServer(serverInfo.IpAddress, serverInfo.RpcPort, serverInfo.ServerId);
                
                // If successful, clean up old connection
                if (oldHost != null)
                {
                    try
                    {
                        await oldHost.StopAsync();
                        oldHost.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[ZONE_TRANSITION] Error disposing old RPC host");
                    }
                }
                
                // Update current server ID and zone
                CurrentServerId = serverInfo.ServerId;
                _currentZone = newZone;
                
                // Update health monitor with the new server zone
                _healthMonitor?.UpdateServerZone(newZone);
                
                // Fire server changed event
                ServerChanged?.Invoke(CurrentServerId);
                
                _logger.LogInformation("[ZONE_TRANSITION] Successfully transitioned to server {ServerId} for zone ({X},{Y})",
                    serverInfo.ServerId, newZone.X, newZone.Y);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ZONE_TRANSITION] Failed to connect to new server {ServerId}", serverInfo.ServerId);
                
                // Restore old connection on failure
                _gameGrain = oldGameGrain;
                _rpcHost = oldHost;
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ZONE_TRANSITION] Zone transition failed");

            // CRITICAL: Even though transition failed, update _currentZone to match physical position
            // This prevents prolonged zone mismatches when RPC calls timeout
            _currentZone = newZone;
            _healthMonitor?.UpdateServerZone(newZone);
            _logger.LogWarning("[ZONE_TRANSITION] Updated zone tracking to ({X},{Y}) despite failed transition to prevent mismatch",
                newZone.X, newZone.Y);

            // Record failed transition in health monitor
            var transitionDuration = DateTime.UtcNow - _zoneTransitionStartTime;
            _healthMonitor?.RecordTransitionComplete(false, transitionDuration);

            // Reset transition state on failure
            _isTransitioning = false;
            _zoneTransitionStartTime = DateTime.MinValue;

            // Optionally trigger a full reconnect if the transition failed catastrophically
            if (!IsConnected && _gameGrain == null)
            {
                _logger.LogWarning("[ZONE_TRANSITION] Lost connection during failed transition, attempting full reconnect");
                await ConnectAsync(PlayerName ?? "Player");
            }
        }
        finally
        {
            _isTransitioning = false;
            _zoneTransitionStartTime = DateTime.MinValue;
        }
    }
    
    /// <summary>
    /// Connects to a specific action server for zone transitions.
    /// </summary>
    private async Task ConnectToActionServer(string serverHost, int rpcPort, string serverId)
    {
        _logger.LogInformation("[ZONE_TRANSITION] Connecting to action server at {Host}:{Port}", serverHost, rpcPort);
        
        // Resolve hostname to IP if needed
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
        
        // Build new RPC host for the target server
        var newHost = BuildRpcHost(resolvedHost, rpcPort, PlayerId!);
        
        // Start the host in the background
        _ = newHost.RunAsync();
        
        // Give the host time to start services
        await Task.Delay(500);
        
        // Get the RPC client from the new host
        var rpcClient = newHost.Services.GetRequiredService<Granville.Rpc.IRpcClient>();
        if (rpcClient == null)
        {
            throw new InvalidOperationException("Failed to get RPC client from host");
        }
        
        // Get the game grain
        var gameGrain = rpcClient.GetGrain<IGameGranule>(PlayerId!);
        if (gameGrain == null)
        {
            throw new InvalidOperationException("Failed to get game grain");
        }
        
        // Perform handshake with the new server
        var handshakeResult = await gameGrain.ConnectPlayer(PlayerId!);
        if (handshakeResult != "SUCCESS")
        {
            throw new InvalidOperationException($"Handshake failed: {handshakeResult}");
        }
        
        // Update internal state
        _rpcHost = newHost;
        _rpcClient = rpcClient;
        _gameGrain = gameGrain;
        CurrentServerId = serverId;
        
        _logger.LogInformation("[ZONE_TRANSITION] Successfully connected to action server {ServerId}", serverId);
    }
}

// Response types from Silo HTTP endpoints - using models from Shooter.Shared
public record PlayerRegistrationResponse(
    Shooter.Shared.Models.PlayerInfo PlayerInfo,
    Shooter.Shared.Models.ActionServerInfo ActionServer,
    string? SessionKey = null,
    DateTime? SessionExpiresAt = null);

// Pre-established connection tracking
internal class PreEstablishedConnection
{
    public IHost? RpcHost { get; set; }
    public Granville.Rpc.IRpcClient? RpcClient { get; set; }
    public IGameGranule? GameGrain { get; set; }
    public Shooter.Shared.Models.ActionServerInfo ServerInfo { get; set; } = null!;
    public DateTime EstablishedAt { get; set; }
    public DateTime LastUsedTime { get; set; }
    public bool IsConnected { get; set; }
    public bool IsConnecting { get; set; }
}

