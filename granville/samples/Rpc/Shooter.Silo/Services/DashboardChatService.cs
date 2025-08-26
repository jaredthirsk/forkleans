using Microsoft.AspNetCore.SignalR.Client;
using Shooter.Shared.Models;

namespace Shooter.Silo.Services;

/// <summary>
/// Service that manages SignalR connection for the Silo dashboard chat functionality.
/// </summary>
public class DashboardChatService : IDisposable
{
    private readonly ILogger<DashboardChatService> _logger;
    private readonly IConfiguration _configuration;
    private HubConnection? _hubConnection;
    private string _siloName = "silo-0";
    
    // Events
    public event Action<ChatMessage>? ChatMessageReceived;
    public event Action<string>? ConnectionStatusChanged;
    
    // Connection state
    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;
    public string SiloName => _siloName;

    public DashboardChatService(
        ILogger<DashboardChatService> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        
        // Generate a unique silo name for this instance
        var instanceId = Environment.GetEnvironmentVariable("ASPIRE_INSTANCE_ID") ?? "0";
        _siloName = $"silo-{instanceId}";
        
        // Don't auto-initialize - wait for the web server to be ready
    }

    /// <summary>
    /// Initialize and connect to the local SignalR hub.
    /// </summary>
    public async Task InitializeAsync()
    {
        // Prevent multiple connections
        if (_hubConnection != null)
        {
            _logger.LogInformation("DashboardChatService already initialized, connection state: {State}", _hubConnection.State);
            return;
        }
        
        try
        {
            // Connect to the local SignalR hub (same silo)
            // Get the actual server URLs from configuration or use localhost with current port
            var baseUrl = GetLocalServerUrl();
            var hubUrl = $"{baseUrl}gameHub";
            
            _logger.LogInformation("Connecting dashboard chat to local SignalR hub at {HubUrl}", hubUrl);
            
            // Build the hub connection
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    // For local connections, we might need to handle self-signed certificates
                    options.HttpMessageHandlerFactory = (handler) =>
                    {
                        if (handler is HttpClientHandler clientHandler)
                        {
                            clientHandler.ServerCertificateCustomValidationCallback = 
                                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                        }
                        return handler;
                    };
                })
                .WithAutomaticReconnect()
                .Build();
            
            // Register event handlers - only listen to ReceiveChatMessage to avoid duplicates
            _hubConnection.On<ChatMessage>("ReceiveChatMessage", (chatMessage) =>
            {
                _logger.LogInformation("[DASHBOARD_CHAT] ReceiveChatMessage handler called - From {User}: {Message}", 
                    chatMessage.SenderName, chatMessage.Message);
                
                // Check if there are any subscribers
                if (ChatMessageReceived == null)
                {
                    _logger.LogWarning("[DASHBOARD_CHAT] No subscribers to ChatMessageReceived event!");
                }
                else
                {
                    var subscriberCount = ChatMessageReceived.GetInvocationList().Length;
                    _logger.LogInformation("[DASHBOARD_CHAT] ChatMessageReceived has {SubscriberCount} subscribers", subscriberCount);
                }
                
                try
                {
                    ChatMessageReceived?.Invoke(chatMessage);
                    _logger.LogInformation("[DASHBOARD_CHAT] ChatMessageReceived event fired successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[DASHBOARD_CHAT] Error firing ChatMessageReceived event");
                }
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
                
                _logger.LogInformation("[DASHBOARD_CHAT] Received system message: {Message}", message);
                ChatMessageReceived?.Invoke(chatMessage);
            });
            
            // Handle reconnection events
            _hubConnection.Reconnecting += (error) =>
            {
                _logger.LogWarning(error, "Dashboard chat connection lost, attempting to reconnect...");
                ConnectionStatusChanged?.Invoke("Reconnecting...");
                return Task.CompletedTask;
            };
            
            _hubConnection.Reconnected += (connectionId) =>
            {
                _logger.LogInformation("Dashboard chat reconnected with ID: {ConnectionId}", connectionId);
                ConnectionStatusChanged?.Invoke("Connected");
                return Task.CompletedTask;
            };
            
            _hubConnection.Closed += (error) =>
            {
                _logger.LogError(error, "Dashboard chat connection closed");
                ConnectionStatusChanged?.Invoke("Disconnected");
                return Task.CompletedTask;
            };
            
            // Start the connection
            await _hubConnection.StartAsync();
            
            _logger.LogInformation("Dashboard chat connected to local SignalR hub as {SiloName}", _siloName);
            ConnectionStatusChanged?.Invoke("Connected");
            
            // Send a join message
            await SendSystemMessageAsync($"{_siloName} dashboard connected");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize dashboard chat service");
            ConnectionStatusChanged?.Invoke("Connection failed");
        }
    }

    /// <summary>
    /// Send a chat message from the silo dashboard.
    /// </summary>
    public async Task SendMessageAsync(string message)
    {
        if (_hubConnection == null || !IsConnected)
        {
            _logger.LogWarning("Cannot send message - not connected to SignalR hub");
            return;
        }
        
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }
        
        try
        {
            _logger.LogInformation("[DASHBOARD_CHAT] Sending message from {SiloName}: {Message}", _siloName, message);
            await _hubConnection.InvokeAsync("SendMessage", _siloName, message);
            _logger.LogInformation("[DASHBOARD_CHAT] Message sent successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DASHBOARD_CHAT] Failed to send chat message");
        }
    }

    /// <summary>
    /// Send a system message from the dashboard.
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
    /// Disconnect from the SignalR hub.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_hubConnection != null)
        {
            try
            {
                // Send a leave message
                await SendSystemMessageAsync($"{_siloName} dashboard disconnected");
                
                await _hubConnection.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting dashboard chat service");
            }
            finally
            {
                _hubConnection = null;
                ConnectionStatusChanged?.Invoke("Disconnected");
            }
        }
    }
    
    private string GetLocalServerUrl()
    {
        // Since we're running in the same process, use the base URL from request context
        // or try to detect the current server configuration
        
        // Try from ASPNETCORE_URLS first
        var urls = _configuration["ASPNETCORE_URLS"];
        if (!string.IsNullOrEmpty(urls))
        {
            // ASPNETCORE_URLS may contain both HTTP and HTTPS URLs
            // We need to use the HTTPS one for SignalR
            var urlArray = urls.Split(';');
            string selectedUrl = urlArray[0];
            
            // Prefer HTTPS URL if available
            foreach (var url in urlArray)
            {
                if (url.StartsWith("https://"))
                {
                    selectedUrl = url;
                    break;
                }
            }
            
            _logger.LogInformation("Using server URL from ASPNETCORE_URLS: {Url}", selectedUrl);
            return selectedUrl.EndsWith("/") ? selectedUrl : selectedUrl + "/";
        }
        
        // Try to get HTTP port and construct HTTPS URL
        var httpPort = _configuration.GetValue<int?>("ASPNETCORE_HTTP_PORT");
        var httpsPort = _configuration.GetValue<int?>("ASPNETCORE_HTTPS_PORT");
        
        if (httpsPort.HasValue)
        {
            var url = $"https://localhost:{httpsPort}/";
            _logger.LogInformation("Using server URL from ASPNETCORE_HTTPS_PORT: {Url}", url);
            return url;
        }
        
        if (httpPort.HasValue)
        {
            // HTTPS port is typically HTTP port + 1
            var url = $"https://localhost:{httpPort + 1}/";
            _logger.LogInformation("Using server URL derived from ASPNETCORE_HTTP_PORT: {Url}", url);
            return url;
        }
        
        // Fallback to common Silo URL (HTTP port 7071, HTTPS port 7072)
        var fallback = "https://localhost:7072/";
        _logger.LogWarning("Using fallback server URL: {Url} - this may cause connection issues", fallback);
        return fallback;
    }

    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
    }
}