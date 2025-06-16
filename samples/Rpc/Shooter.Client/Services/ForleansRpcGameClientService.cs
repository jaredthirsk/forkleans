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
public class ForleansRpcGameClientService : IDisposable
{
    private readonly ILogger<ForleansRpcGameClientService> _logger;
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
    
    public event Action<WorldState>? WorldStateUpdated;
    public event Action<string>? ServerChanged;
    public event Action<List<GridSquare>>? AvailableZonesUpdated;
    
    public bool IsConnected { get; private set; }
    public string? PlayerId { get; private set; }
    public string? CurrentServerId { get; private set; }
    
    public ForleansRpcGameClientService(
        ILogger<ForleansRpcGameClientService> logger,
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
            
            // Wait a bit for the handshake and manifest exchange to complete
            // This is a workaround - ideally the RPC client would expose a way to wait for readiness
            await Task.Delay(1000);
            
            // Get the game grain - use a fixed key since this represents the server itself
            // In RPC, grains are essentially singleton services per server
            _gameGrain = _rpcClient.GetGrain<IGameRpcGrain>("game");
            
            // Connect via RPC
            _logger.LogInformation("Connecting player {PlayerId} via RPC", PlayerId);
            var connected = await _gameGrain.ConnectPlayer(PlayerId);
            _logger.LogInformation("RPC ConnectPlayer returned: {Connected}", connected);
            
            if (!connected)
            {
                _logger.LogError("Failed to connect player via RPC");
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
        if (_gameGrain == null || !IsConnected || _cancellationTokenSource?.Token.IsCancellationRequested == true)
        {
            return;
        }
        
        try
        {
            var worldState = await _gameGrain.GetWorldState();
            if (worldState != null)
            {
                _logger.LogDebug("Received world state with {Count} entities", worldState.Entities?.Count ?? 0);
                
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
                        
                        if (distToEdge < 100) // Within 100 units of edge
                        {
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
            _logger.LogInformation("Checking for server transition for player {PlayerId}", PlayerId);
            
            // Query the Orleans silo for the correct server
            var response = await _httpClient.GetFromJsonAsync<ActionServerInfo>(
                $"api/world/players/{PlayerId}/server");
                
            if (response != null && response.ServerId != CurrentServerId)
            {
                _logger.LogInformation("Player {PlayerId} needs to transition from server {OldServer} to {NewServer}", 
                    PlayerId, CurrentServerId, response.ServerId);
                
                // Disconnect from current server
                Cleanup();
                
                // Reconnect to new server
                await ConnectToActionServer(response);
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
    
    private async Task ConnectToActionServer(ActionServerInfo serverInfo)
    {
        CurrentServerId = serverInfo.ServerId;
        
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
        
        // Wait for handshake
        await Task.Delay(1000);
        
        // Get the game grain
        _gameGrain = _rpcClient.GetGrain<IGameRpcGrain>("game");
        
        // Reconnect player
        var connected = await _gameGrain.ConnectPlayer(PlayerId!);
        if (connected)
        {
            IsConnected = true;
            
            // Restart timers
            _worldStateTimer = new Timer(async _ => await PollWorldState(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(16));
            _heartbeatTimer = new Timer(async _ => await SendHeartbeat(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
            _availableZonesTimer = new Timer(async _ => await PollAvailableZones(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
            
            // Notify about server change
            ServerChanged?.Invoke(CurrentServerId);
            
            _logger.LogInformation("Successfully reconnected to new server {ServerId}", CurrentServerId);
        }
        else
        {
            _logger.LogError("Failed to reconnect player to new server");
        }
    }
    
    private void Cleanup()
    {
        IsConnected = false;
        
        _cancellationTokenSource?.Cancel();
        _worldStateTimer?.Dispose();
        _heartbeatTimer?.Dispose();
        _availableZonesTimer?.Dispose();
        
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
        
        // Don't clear PlayerId as we need it for reconnection
        // PlayerId = null;
        CurrentServerId = null;
    }
    
    public void Dispose()
    {
        Cleanup();
        PlayerId = null;  // Clear PlayerId only on final dispose
    }
}

// Response types from Silo HTTP endpoints
public record PlayerRegistrationResponse(PlayerInfo PlayerInfo, ActionServerInfo ActionServer);
public record PlayerInfo(string PlayerId, string Name, Vector2 Position);
public record ActionServerInfo(string ServerId, string IpAddress, int UdpPort, string HttpEndpoint, int RpcPort, GridSquare AssignedSquare);

