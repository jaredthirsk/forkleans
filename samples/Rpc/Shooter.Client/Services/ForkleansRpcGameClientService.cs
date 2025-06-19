using Forkleans;
using Forkleans.Rpc;
using Forkleans.Rpc.Transport.LiteNetLib;
using Forkleans.Serialization;
using Shooter.Shared.Models;
using Shooter.Shared.RpcInterfaces;
using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Shooter.Client.Services;

/// <summary>
/// Game client service that uses Forkleans RPC for all communication with ActionServers.
/// This provides a single LiteNetLib UDP connection for all game operations.
/// </summary>
public class ForkleansRpcGameClientService : IDisposable
{
    private readonly ILogger<ForkleansRpcGameClientService> _logger;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private Forkleans.IClusterClient? _rpcClient;
    private IHost? _rpcHost;
    private IGameRpcGrain? _gameGrain;
    private Timer? _worldStateTimer;
    private Timer? _heartbeatTimer;
    private Timer? _availableZonesTimer;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isTransitioning = false;
    private DateTime _lastZoneBoundaryCheck = DateTime.MinValue;
    private long _lastSequenceNumber = -1;
    private readonly Dictionary<string, PreEstablishedConnection> _preEstablishedConnections = new();
    private GridSquare? _currentZone = null;
    
    public event Action<WorldState>? WorldStateUpdated;
    public event Action<string>? ServerChanged;
    public event Action<List<GridSquare>>? AvailableZonesUpdated;
    public event Action<Dictionary<string, bool>>? PreEstablishedConnectionsUpdated;
    
    public bool IsConnected { get; private set; }
    public string? PlayerId { get; private set; }
    public string? CurrentServerId { get; private set; }
    
    public ForkleansRpcGameClientService(
        ILogger<ForkleansRpcGameClientService> logger,
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
            
            // First register with HTTP to get server info and player ID
            var registrationResponse = await RegisterWithHttpAsync(playerName);
            if (registrationResponse == null) return false;
            
            PlayerId = registrationResponse.PlayerInfo?.PlayerId ?? string.Empty;
            CurrentServerId = registrationResponse.ActionServer?.ServerId ?? "Unknown";
            _currentZone = registrationResponse.ActionServer?.AssignedSquare;
            
            _logger.LogInformation("Player registered with ID {PlayerId} on server {ServerId} in zone ({X}, {Y})", 
                PlayerId, CurrentServerId, 
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
            
            _logger.LogInformation("Connecting to Forkleans RPC server at {Host}:{Port}", resolvedHost, rpcPort);
            
            // Create RPC client
            var hostBuilder = Host.CreateDefaultBuilder()
                .UseOrleansRpcClient(rpcBuilder =>
                {
                    rpcBuilder.ConnectTo(resolvedHost, rpcPort);
                    rpcBuilder.UseLiteNetLib();
                })
                .ConfigureServices(services =>
                {
                    // Add serialization for the grain interfaces
                    services.AddSerializer(serializer =>
                    {
                        serializer.AddAssembly(typeof(IGameRpcGrain).Assembly);
                    });
                })
                .Build();
                
            await hostBuilder.StartAsync();
            
            _rpcHost = hostBuilder;
            _rpcClient = hostBuilder.Services.GetRequiredService<Forkleans.IClusterClient>();
            
            // Wait longer for the handshake and manifest exchange to complete
            // This is a workaround - ideally the RPC client would expose a way to wait for readiness
            await Task.Delay(2000);
            
            // Get the game grain - use a fixed key since this represents the server itself
            // In RPC, grains are essentially singleton services per server
            _gameGrain = _rpcClient.GetGrain<IGameRpcGrain>("game");
            
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
            
            // Start polling for world state
            _worldStateTimer = new Timer(async _ => await PollWorldState(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(16));
            
            // Start heartbeat
            _heartbeatTimer = new Timer(async _ => await SendHeartbeat(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
            
            // Start polling for available zones
            _availableZonesTimer = new Timer(async _ => await PollAvailableZones(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
            
            _logger.LogInformation("Connected to game via Forkleans RPC");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to game server");
            return false;
        }
    }
    
    private async Task<PlayerRegistrationResponse?> RegisterWithHttpAsync(string playerName)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/world/players/register", 
                new { PlayerId = Guid.NewGuid().ToString(), Name = playerName });
            
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<Shooter.Client.Services.PlayerRegistrationResponse>();
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
        
        try
        {
            await _gameGrain.UpdatePlayerInput(PlayerId, moveDirection, isShooting);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send player input");
        }
    }
    
    public async Task SendPlayerInputEx(Vector2? moveDirection, Vector2? shootDirection)
    {
        if (_gameGrain == null || !IsConnected || string.IsNullOrEmpty(PlayerId))
        {
            return;
        }
        
        try
        {
            await _gameGrain.UpdatePlayerInputEx(PlayerId, moveDirection, shootDirection);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send player input");
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
                    // Check if player is near zone boundary
                    var playerEntity = worldState.Entities?.FirstOrDefault(e => e.EntityId == PlayerId);
                    if (playerEntity != null)
                    {
                        var currentZone = GridSquare.FromPosition(playerEntity.Position);
                        var (min, max) = currentZone.GetBounds();
                        var distToEdge = Math.Min(
                            Math.Min(playerEntity.Position.X - min.X, max.X - playerEntity.Position.X),
                            Math.Min(playerEntity.Position.Y - min.Y, max.Y - playerEntity.Position.Y)
                        );
                        
                        if (distToEdge < 50)
                        {
                            // Throttle zone boundary checks to once per second
                            var now = DateTime.UtcNow;
                            if ((now - _lastZoneBoundaryCheck).TotalSeconds >= 1.0)
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
            _logger.LogError(ex, "Failed to get world state");
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
        
        _isTransitioning = true;
        
        try
        {
            _logger.LogTrace("Checking for server transition for player {PlayerId}", PlayerId);
            
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
                
                // Disconnect from current server
                Cleanup();
                
                var cleanupDuration = (DateTime.UtcNow - cleanupStart).TotalMilliseconds;
                _logger.LogInformation("[ZONE_TRANSITION] Cleanup took {Duration}ms", cleanupDuration);
                
                // Reconnect to new server
                var connectStart = DateTime.UtcNow;
                await ConnectToActionServer(response);
                var connectDuration = (DateTime.UtcNow - connectStart).TotalMilliseconds;
                _logger.LogInformation("[ZONE_TRANSITION] ConnectToActionServer took {Duration}ms", connectDuration);
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
        
        // Create new cancellation token source for the new connection
        _cancellationTokenSource = new CancellationTokenSource();
        
        // Check if we have a pre-established connection for this zone
        var connectionKey = $"{serverInfo.AssignedSquare.X},{serverInfo.AssignedSquare.Y}";
        _logger.LogInformation("[ZONE_TRANSITION] Checking for pre-established connection with key {Key}. Available keys: {Keys}", 
            connectionKey, string.Join(", ", _preEstablishedConnections.Keys));
        
        if (_preEstablishedConnections.TryGetValue(connectionKey, out var preEstablished) && preEstablished.IsConnected)
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
            _logger.LogInformation("[ZONE_TRANSITION] No pre-established connection for zone {Key}, creating new connection", connectionKey);
            
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
            
            _logger.LogInformation("Connecting to new Forkleans RPC server at {Host}:{Port}", resolvedHost, rpcPort);
            
            // Create new RPC client
            var hostBuilder = Host.CreateDefaultBuilder()
                .UseOrleansRpcClient(rpcBuilder =>
                {
                    rpcBuilder.ConnectTo(resolvedHost, rpcPort);
                    rpcBuilder.UseLiteNetLib();
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
            _rpcClient = hostBuilder.Services.GetRequiredService<Forkleans.IClusterClient>();
            
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
            var testState = await _gameGrain.GetWorldState();
            _logger.LogInformation("Test GetWorldState succeeded, got {Count} entities", testState?.Entities?.Count ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test GetWorldState failed after reconnection");
            IsConnected = false;
            return;
        }
        
        // Restart timers with minimal initial delay for smooth transition
        _worldStateTimer = new Timer(async _ => await PollWorldState(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(16));
        _heartbeatTimer = new Timer(async _ => await SendHeartbeat(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
        _availableZonesTimer = new Timer(async _ => await PollAvailableZones(), null, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(2));
        
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
        
        // Find neighboring zones (adjacent to current zone)
        var neighboringZones = GetNeighboringZones(_currentZone);
        
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
            
            // Check each neighboring zone
            var connectionStatus = new Dictionary<GridSquare, bool>();
            
            foreach (var zone in neighboringZones)
            {
                if (zones.Contains(zone) && zoneToServer.TryGetValue(zone, out var serverInfo))
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
            
            // Clean up old connections not in neighboring zones
            // Only clean up connections older than 10 seconds to avoid race conditions
            // Also keep the current zone's connection
            var validKeys = neighboringZones.Select(z => $"{z.X},{z.Y}").ToHashSet();
            validKeys.Add($"{_currentZone.X},{_currentZone.Y}"); // Keep current zone connection
            
            var keysToRemove = _preEstablishedConnections
                .Where(kvp => !validKeys.Contains(kvp.Key) && 
                              (DateTime.UtcNow - kvp.Value.EstablishedAt).TotalSeconds > 10)
                .Select(kvp => kvp.Key)
                .ToList();
            
            if (keysToRemove.Count > 0)
            {
                _logger.LogInformation("Cleaning up {Count} old pre-established connections: {Keys}", 
                    keysToRemove.Count, string.Join(", ", keysToRemove));
            }
            
            foreach (var key in keysToRemove)
            {
                await CleanupPreEstablishedConnection(key);
            }
            
            // Notify about ALL pre-established connections (not just the ones we just checked)
            // Convert to a format that can be serialized to JSON (string keys instead of GridSquare keys)
            var allConnectionStatus = new Dictionary<string, bool>();
            foreach (var kvp in _preEstablishedConnections)
            {
                allConnectionStatus[kvp.Key] = kvp.Value.IsConnected;
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
        
        // Add all 8 neighboring zones (including diagonals)
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
                    rpcBuilder.UseLiteNetLib();
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
            
            var connection = new PreEstablishedConnection
            {
                RpcHost = hostBuilder,
                RpcClient = hostBuilder.Services.GetRequiredService<Forkleans.IClusterClient>(),
                ServerInfo = serverInfo,
                EstablishedAt = DateTime.UtcNow,
                IsConnected = false
            };
            
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
                    _logger.LogError(ex, "Failed to test pre-established connection to zone {Key}", connectionKey);
                    connection.IsConnected = false;
                    break;
                }
            }
            
            _preEstablishedConnections[connectionKey] = connection;
            return connection.IsConnected;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to establish connection to zone {Key}", connectionKey);
            return false;
        }
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
    
    private void Cleanup()
    {
        IsConnected = false;
        _lastSequenceNumber = -1;
        
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
        
        _worldStateTimer?.Dispose();
        _heartbeatTimer?.Dispose();
        _availableZonesTimer?.Dispose();
        _worldStateTimer = null;
        _heartbeatTimer = null;
        _availableZonesTimer = null;
        
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
}

// Response types from Silo HTTP endpoints - using models from Shooter.Shared
public record PlayerRegistrationResponse(Shooter.Shared.Models.PlayerInfo PlayerInfo, Shooter.Shared.Models.ActionServerInfo ActionServer);

// Pre-established connection tracking
internal class PreEstablishedConnection
{
    public IHost? RpcHost { get; set; }
    public Forkleans.IClusterClient? RpcClient { get; set; }
    public IGameRpcGrain? GameGrain { get; set; }
    public Shooter.Shared.Models.ActionServerInfo ServerInfo { get; set; } = null!;
    public DateTime EstablishedAt { get; set; }
    public bool IsConnected { get; set; }
}

