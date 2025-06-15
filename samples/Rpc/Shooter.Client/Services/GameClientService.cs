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
    
    public event Action<WorldState>? WorldStateUpdated;
    public bool IsConnected => _currentServer != null;
    public string? PlayerId => _playerId;
    
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
                await _actionServerClient.PostAsJsonAsync(
                    $"game/input/{_playerId}", 
                    new PlayerInput(moveDirection, isShooting));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send player input");
            }
        }
    }
    
    private async Task PollWorldStateAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _actionServerClient != null)
        {
            try
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get world state from {BaseAddress}", _actionServerClient.BaseAddress);
            }
            
            await Task.Delay(50, cancellationToken); // 20 FPS update rate
        }
    }
    
    public void Dispose()
    {
        _pollingCancellation?.Cancel();
        _pollingCancellation?.Dispose();
        DisconnectAsync().Wait();
    }
}

public record PlayerRegistrationResponse
{
    public PlayerInfo? PlayerInfo { get; init; }
    public ActionServerInfo? ActionServer { get; init; }
}

public record PlayerInput(Vector2 MoveDirection, bool IsShooting);