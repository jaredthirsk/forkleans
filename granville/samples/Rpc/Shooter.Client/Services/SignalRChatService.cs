using Microsoft.AspNetCore.SignalR.Client;
using Shooter.Shared.GrainInterfaces;
using Shooter.Shared.Models;
using System.Net.Http.Json;

namespace Shooter.Client.Services;

/// <summary>
/// Service that manages SignalR connections for chat functionality across multiple silos.
/// </summary>
public class SignalRChatService : IDisposable
{
    private readonly ILogger<SignalRChatService> _logger;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private HubConnection? _hubConnection;
    private List<SiloInfo> _availableSilos = new();
    private SiloInfo? _currentSilo;
    private string? _playerName;
    private string? _playerId;
    
    // Events
    public event Action<ChatMessage>? ChatMessageReceived;
    public event Action<string>? ConnectionStatusChanged;
    public event Action<SiloInfo>? SiloChanged;
    public event Action<List<SiloInfo>>? SilosUpdated;
    
    // Connection state
    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;
    public SiloInfo? CurrentSilo => _currentSilo;
    public List<SiloInfo> AvailableSilos => _availableSilos;

    public SignalRChatService(
        ILogger<SignalRChatService> logger,
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _logger = logger;
        _httpClient = httpClient;
        _configuration = configuration;
    }

    /// <summary>
    /// Initialize the service and discover available silos.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            await DiscoverSilosAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize SignalR chat service");
        }
    }

    /// <summary>
    /// Discover available silos from the primary silo.
    /// </summary>
    public async Task DiscoverSilosAsync()
    {
        try
        {
            var siloUrl = _configuration["SiloUrl"] ?? "https://localhost:7071/";
            if (!siloUrl.EndsWith("/")) siloUrl += "/";
            
            _logger.LogInformation("Discovering silos from {SiloUrl}", siloUrl);
            
            var response = await _httpClient.GetFromJsonAsync<List<SiloInfo>>($"{siloUrl}api/world/silos");
            if (response != null && response.Count > 0)
            {
                _availableSilos = response;
                _logger.LogInformation("Discovered {Count} silos", _availableSilos.Count);
                
                foreach (var silo in _availableSilos)
                {
                    _logger.LogInformation("Silo {SiloId}: {HttpsEndpoint} (Primary: {IsPrimary})", 
                        silo.SiloId, silo.HttpsEndpoint, silo.IsPrimary);
                }
                
                SilosUpdated?.Invoke(_availableSilos);
            }
            else
            {
                _logger.LogWarning("No silos discovered");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover silos");
        }
    }

    /// <summary>
    /// Connect to a random silo for load balancing.
    /// </summary>
    public async Task ConnectAsync(string playerId, string playerName)
    {
        _playerId = playerId;
        _playerName = playerName;
        
        if (_availableSilos.Count == 0)
        {
            await DiscoverSilosAsync();
        }
        
        if (_availableSilos.Count == 0)
        {
            _logger.LogError("No silos available to connect to");
            ConnectionStatusChanged?.Invoke("No silos available");
            return;
        }
        
        // Select a random silo
        var random = new Random();
        var silo = _availableSilos[random.Next(_availableSilos.Count)];
        
        await ConnectToSiloAsync(silo);
    }

    /// <summary>
    /// Connect to a specific silo.
    /// </summary>
    public async Task ConnectToSiloAsync(SiloInfo silo)
    {
        try
        {
            // Disconnect from current silo if connected
            if (_hubConnection != null)
            {
                await DisconnectAsync();
            }
            
            _currentSilo = silo;
            
            _logger.LogInformation("Connecting to silo {SiloId} at {SignalRUrl}", silo.SiloId, silo.SignalRUrl);
            
            // Build the hub connection
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(silo.SignalRUrl, options =>
                {
                    // Configure options if needed (e.g., authentication)
                    options.HttpMessageHandlerFactory = (handler) =>
                    {
                        if (handler is HttpClientHandler clientHandler)
                        {
                            // Accept self-signed certificates in development
                            clientHandler.ServerCertificateCustomValidationCallback = 
                                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                        }
                        return handler;
                    };
                })
                .WithAutomaticReconnect()
                .Build();
            
            // Register event handlers
            _hubConnection.On<string, string>("ReceiveMessage", (user, message) =>
            {
                var chatMessage = new ChatMessage(
                    SenderId: "unknown", // We don't have sender ID in current implementation
                    SenderName: user,
                    Message: message,
                    Timestamp: DateTime.UtcNow,
                    IsSystemMessage: false
                );
                
                _logger.LogInformation("[SIGNALR_CHAT] Received message from {User}: {Message}", user, message);
                ChatMessageReceived?.Invoke(chatMessage);
            });
            
            _hubConnection.On<string>("ReceiveSystemMessage", (message) =>
            {
                var chatMessage = new ChatMessage(
                    SenderId: "system",
                    SenderName: "System",
                    Message: message,
                    Timestamp: DateTime.UtcNow,
                    IsSystemMessage: true
                );
                
                _logger.LogInformation("[SIGNALR_CHAT] Received system message: {Message}", message);
                ChatMessageReceived?.Invoke(chatMessage);
            });
            
            // Handle reconnection events
            _hubConnection.Reconnecting += (error) =>
            {
                _logger.LogWarning(error, "SignalR connection lost, attempting to reconnect...");
                ConnectionStatusChanged?.Invoke("Reconnecting...");
                return Task.CompletedTask;
            };
            
            _hubConnection.Reconnected += (connectionId) =>
            {
                _logger.LogInformation("SignalR reconnected with ID: {ConnectionId}", connectionId);
                ConnectionStatusChanged?.Invoke("Connected");
                return Task.CompletedTask;
            };
            
            _hubConnection.Closed += (error) =>
            {
                _logger.LogError(error, "SignalR connection closed");
                ConnectionStatusChanged?.Invoke("Disconnected");
                return Task.CompletedTask;
            };
            
            // Start the connection
            await _hubConnection.StartAsync();
            
            _logger.LogInformation("Connected to SignalR hub on silo {SiloId}", silo.SiloId);
            ConnectionStatusChanged?.Invoke($"Connected to {silo.SiloId}");
            SiloChanged?.Invoke(silo);
            
            // Send a join message
            if (!string.IsNullOrEmpty(_playerName))
            {
                await SendSystemMessageAsync($"{_playerName} joined the chat");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to SignalR hub");
            ConnectionStatusChanged?.Invoke("Connection failed");
            _currentSilo = null;
        }
    }

    /// <summary>
    /// Switch to the next silo in the list.
    /// </summary>
    public async Task SwitchToNextSiloAsync()
    {
        if (_availableSilos.Count <= 1)
        {
            _logger.LogWarning("No other silos available to switch to");
            return;
        }
        
        // Find current silo index
        var currentIndex = _currentSilo != null 
            ? _availableSilos.FindIndex(s => s.SiloId == _currentSilo.SiloId) 
            : -1;
        
        // Get next silo (wrap around)
        var nextIndex = (currentIndex + 1) % _availableSilos.Count;
        var nextSilo = _availableSilos[nextIndex];
        
        _logger.LogInformation("Switching from silo {Current} to {Next}", 
            _currentSilo?.SiloId ?? "none", nextSilo.SiloId);
        
        await ConnectToSiloAsync(nextSilo);
    }

    /// <summary>
    /// Send a chat message.
    /// </summary>
    public async Task SendMessageAsync(string message)
    {
        if (_hubConnection == null || !IsConnected)
        {
            _logger.LogWarning("Cannot send message - not connected to SignalR hub");
            return;
        }
        
        if (string.IsNullOrEmpty(_playerName))
        {
            _logger.LogWarning("Cannot send message - player name not set");
            return;
        }
        
        try
        {
            _logger.LogInformation("[SIGNALR_CHAT] Attempting to send message from {PlayerName}: {Message}", _playerName, message);
            _logger.LogInformation("[SIGNALR_CHAT] HubConnection state: {State}, ConnectionId: {ConnectionId}", 
                _hubConnection.State, _hubConnection.ConnectionId);
            
            await _hubConnection.InvokeAsync("SendMessage", _playerName, message);
            
            _logger.LogInformation("[SIGNALR_CHAT] Message sent successfully to hub");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SIGNALR_CHAT] Failed to send chat message");
        }
    }

    /// <summary>
    /// Send a system message.
    /// </summary>
    private async Task SendSystemMessageAsync(string message)
    {
        if (_hubConnection == null || !IsConnected)
        {
            return;
        }
        
        try
        {
            await _hubConnection.InvokeAsync("SendMessage", "System", message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send system message");
        }
    }

    /// <summary>
    /// Disconnect from the current SignalR hub.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_hubConnection != null)
        {
            try
            {
                // Send a leave message
                if (!string.IsNullOrEmpty(_playerName))
                {
                    await SendSystemMessageAsync($"{_playerName} left the chat");
                }
                
                await _hubConnection.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting from SignalR hub");
            }
            finally
            {
                _hubConnection = null;
                _currentSilo = null;
                ConnectionStatusChanged?.Invoke("Disconnected");
            }
        }
    }

    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
    }
}