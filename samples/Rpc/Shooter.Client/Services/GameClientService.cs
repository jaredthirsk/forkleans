using System.Net.Http.Json;
using Shooter.Shared.Models;

namespace Shooter.Client.Services;

public class GameClientService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GameClientService> _logger;
    private string? _playerId;
    private ActionServerInfo? _currentServer;
    private HttpClient? _actionServerClient;
    private CancellationTokenSource? _pollingCancellation;
    private DateTime _lastServerCheck = DateTime.UtcNow;
    private bool _isTransitioning = false;
    
    public event Action<WorldState>? WorldStateUpdated;
    public event Action<string>? ServerChanged;
    public event Action<List<GridSquare>>? AvailableZonesUpdated;
    public bool IsConnected => _currentServer != null && !_isTransitioning;
    public string? PlayerId => _playerId;
    public string? CurrentServerId => _currentServer?.ServerId;
    
    public GameClientService(HttpClient httpClient, ILogger<GameClientService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }
    
    public async Task<bool> ConnectAsync(string playerName)
    {
        try
        {
            _playerId = Guid.NewGuid().ToString();
            
            // Register with Orleans silo
            var response = await _httpClient.PostAsJsonAsync(
                "api/world/players/register",
                new { PlayerId = _playerId, Name = playerName });
                
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to register player");
                return false;
            }
            
            var registration = await response.Content.ReadFromJsonAsync<PlayerRegistrationResponse>();
            if (registration?.ActionServer == null)
            {
                _logger.LogError("No action server available");
                return false;
            }
            
            _currentServer = registration.ActionServer;
            
            // Create HTTP client for action server
            // Note: Using UdpPort field which actually contains the HTTP port for the action server
            // If IpAddress looks like a service name (contains letters), use it directly
            var baseUrl = _currentServer.IpAddress.Any(char.IsLetter) 
                ? $"http://{_currentServer.IpAddress}/"  // Aspire service discovery will handle the port
                : $"http://{_currentServer.IpAddress}:{_currentServer.UdpPort}/"; // Direct IP:port
                
            _logger.LogInformation("Connecting to ActionServer at: {BaseUrl}", baseUrl);
            
            _actionServerClient = new HttpClient
            {
                BaseAddress = new Uri(baseUrl)
            };
            
            // Connect player to game
            var connectResponse = await _actionServerClient.PostAsync($"game/connect/{_playerId}", null);
            if (!connectResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to connect to game service");
                return false;
            }
            
            // Start polling for world state
            _pollingCancellation = new CancellationTokenSource();
            _ = Task.Run(() => PollWorldStateAsync(_pollingCancellation.Token));
            
            // Fetch available zones
            await FetchAvailableZones();
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect");
            return false;
        }
    }
    
    public async Task DisconnectAsync()
    {
        _pollingCancellation?.Cancel();
        
        if (_actionServerClient != null && _playerId != null)
        {
            try
            {
                await _actionServerClient.DeleteAsync($"game/disconnect/{_playerId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during disconnect");
            }
        }
        
        _actionServerClient?.Dispose();
        _actionServerClient = null;
        _currentServer = null;
    }
    
    public async Task SendPlayerInput(Vector2 moveDirection, bool isShooting)
    {
        if (_actionServerClient != null && _playerId != null)
        {
            try
            {
                if (moveDirection.Length() > 0 || isShooting)
                {
                    _logger.LogDebug("Sending input for {PlayerId}: Move={Move}, Shoot={Shoot}", 
                        _playerId, moveDirection, isShooting);
                }
                
                await _actionServerClient.PostAsJsonAsync(
                    $"game/input/{_playerId}", 
                    new PlayerInput(moveDirection, isShooting));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send player input");
            }
        }
        else
        {
            _logger.LogWarning("Cannot send input - client or playerId null");
        }
    }
    
    private async Task PollWorldStateAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _actionServerClient != null)
        {
            try
            {
                // Check for server transitions every 2 seconds
                if (DateTime.UtcNow - _lastServerCheck > TimeSpan.FromSeconds(2) && !_isTransitioning)
                {
                    _lastServerCheck = DateTime.UtcNow;
                    await CheckForServerTransition();
                    await FetchAvailableZones(); // Also refresh available zones
                }
                
                if (!_isTransitioning)
                {
                    var worldState = await _actionServerClient.GetFromJsonAsync<WorldState>("game/state", cancellationToken);
                    if (worldState != null)
                    {
                        _logger.LogDebug("Received world state with {EntityCount} entities", worldState.Entities?.Count ?? 0);
                        WorldStateUpdated?.Invoke(worldState);
                    }
                    else
                    {
                        _logger.LogWarning("Received null world state");
                    }
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Player might have been removed from this server
                _logger.LogWarning("Player not found on server, checking for transition");
                await CheckForServerTransition();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get world state from {BaseAddress}", _actionServerClient.BaseAddress);
            }
            
            await Task.Delay(50, cancellationToken); // 20 FPS update rate
        }
    }
    
    private async Task FetchAvailableZones()
    {
        try
        {
            var servers = await _httpClient.GetFromJsonAsync<List<ActionServerInfo>>("api/world/action-servers");
            if (servers != null)
            {
                var availableZones = servers.Select(s => s.AssignedSquare).ToList();
                AvailableZonesUpdated?.Invoke(availableZones);
                _logger.LogInformation("Fetched {Count} available zones", availableZones.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch available zones");
        }
    }
    
    public void Dispose()
    {
        _pollingCancellation?.Cancel();
        _pollingCancellation?.Dispose();
        DisconnectAsync().Wait();
    }
    
    private async Task CheckForServerTransition()
    {
        if (_playerId == null || _isTransitioning) return;
        
        try
        {
            // Query Orleans for the correct server for this player
            var response = await _httpClient.GetFromJsonAsync<ActionServerInfo>(
                $"api/world/players/{_playerId}/server");
                
            if (response != null && response.ServerId != _currentServer?.ServerId)
            {
                _logger.LogInformation("Server transition detected: {OldServer} -> {NewServer}", 
                    _currentServer?.ServerId, response.ServerId);
                    
                _isTransitioning = true;
                
                // Disconnect from current server
                if (_actionServerClient != null)
                {
                    try
                    {
                        await _actionServerClient.DeleteAsync($"game/disconnect/{_playerId}");
                    }
                    catch { /* Ignore errors during disconnect */ }
                    
                    _actionServerClient.Dispose();
                }
                
                // Connect to new server
                _currentServer = response;
                var baseUrl = _currentServer.IpAddress.Any(char.IsLetter) 
                    ? $"http://{_currentServer.IpAddress}/"
                    : $"http://{_currentServer.IpAddress}:{_currentServer.UdpPort}/";
                    
                _logger.LogInformation("Connecting to new ActionServer at: {BaseUrl}", baseUrl);
                
                _actionServerClient = new HttpClient
                {
                    BaseAddress = new Uri(baseUrl)
                };
                
                // Connect to the new server
                var connectResponse = await _actionServerClient.PostAsync($"game/connect/{_playerId}", null);
                if (connectResponse.IsSuccessStatusCode)
                {
                    // Give the server more time to initialize the player with correct position
                    _logger.LogInformation("Connected to new server, waiting for player initialization...");
                    await Task.Delay(300); // Increased delay
                    
                    _isTransitioning = false;
                    ServerChanged?.Invoke(response.ServerId);
                    _logger.LogInformation("Successfully connected to new server {ServerId}", response.ServerId);
                }
                else
                {
                    _logger.LogError("Failed to connect to new server, status: {Status}", connectResponse.StatusCode);
                    _isTransitioning = false;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for server transition");
            _isTransitioning = false;
        }
    }
}

public record PlayerRegistrationResponse
{
    public PlayerInfo? PlayerInfo { get; init; }
    public ActionServerInfo? ActionServer { get; init; }
}

public record PlayerInput(Vector2 MoveDirection, bool IsShooting);