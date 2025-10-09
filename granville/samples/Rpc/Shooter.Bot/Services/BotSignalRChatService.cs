using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Shooter.Bot.Services;

/// <summary>
/// SignalR chat service for bots to send messages through UFX.Orleans.SignalRBackplane.
/// </summary>
public class BotSignalRChatService : IDisposable
{
    private readonly ILogger<BotSignalRChatService> _logger;
    private readonly IConfiguration _configuration;
    private HubConnection? _hubConnection;
    private readonly string _botName;
    private bool _isConnected;

    public BotSignalRChatService(
        ILogger<BotSignalRChatService> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        
        // Get bot name from configuration
        _botName = _configuration.GetValue<string>("BotName") ?? "Bot";
    }

    public async Task<bool> ConnectAsync()
    {
        try
        {
            // Get the Client URL from configuration (not Silo URL for SignalR)
            // SignalR hub is hosted by the Client, not the Silo
            var clientUrl = _configuration.GetValue<string>("ClientUrl", "");

            // If ClientUrl is not specified, try to derive from environment or use default
            if (string.IsNullOrEmpty(clientUrl))
            {
                // In Aspire environment, the client runs on port 7080 (HTTPS)
                clientUrl = "https://localhost:7080";
                _logger.LogInformation("ClientUrl not configured, using default: {ClientUrl}", clientUrl);
            }

            // Ensure HTTPS for SignalR
            if (!clientUrl.StartsWith("https://"))
            {
                clientUrl = clientUrl.Replace("http://", "https://");
            }

            var hubUrl = $"{clientUrl}/gamehub";
            _logger.LogInformation("Bot {BotName} connecting to SignalR hub at {HubUrl}", _botName, hubUrl);
            
            // Build the hub connection
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    // Accept self-signed certificates in development
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
            
            // Handle reconnection events
            _hubConnection.Reconnecting += (error) =>
            {
                _logger.LogWarning(error, "Bot {BotName} SignalR connection lost, attempting to reconnect...", _botName);
                _isConnected = false;
                return Task.CompletedTask;
            };
            
            _hubConnection.Reconnected += (connectionId) =>
            {
                _logger.LogInformation("Bot {BotName} SignalR reconnected with ID: {ConnectionId}", _botName, connectionId);
                _isConnected = true;
                return Task.CompletedTask;
            };
            
            _hubConnection.Closed += (error) =>
            {
                _logger.LogError(error, "Bot {BotName} SignalR connection closed", _botName);
                _isConnected = false;
                return Task.CompletedTask;
            };
            
            // Start the connection
            await _hubConnection.StartAsync();
            _isConnected = true;
            
            _logger.LogInformation("Bot {BotName} connected to SignalR hub", _botName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bot {BotName} failed to connect to SignalR hub", _botName);
            _isConnected = false;
            return false;
        }
    }

    public async Task SendMessageAsync(string message)
    {
        if (_hubConnection == null || !_isConnected)
        {
            _logger.LogWarning("Bot {BotName} cannot send message - not connected to SignalR hub", _botName);
            return;
        }
        
        try
        {
            _logger.LogDebug("Bot {BotName} sending SignalR message: {Message}", _botName, message);
            await _hubConnection.InvokeAsync("SendMessage", _botName, message);
            _logger.LogDebug("Bot {BotName} SignalR message sent successfully", _botName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bot {BotName} failed to send SignalR message", _botName);
        }
    }

    public async Task DisconnectAsync()
    {
        if (_hubConnection != null)
        {
            try
            {
                await _hubConnection.DisposeAsync();
                _logger.LogInformation("Bot {BotName} disconnected from SignalR hub", _botName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bot {BotName} error disconnecting from SignalR hub", _botName);
            }
            finally
            {
                _hubConnection = null;
                _isConnected = false;
            }
        }
    }

    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
    }
}