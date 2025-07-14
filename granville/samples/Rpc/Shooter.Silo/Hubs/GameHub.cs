using Microsoft.AspNetCore.SignalR;
using Shooter.Shared.Models;
using Shooter.Shared.GrainInterfaces;
using Orleans;

namespace Shooter.Silo.Hubs;

/// <summary>
/// SignalR hub client interface for strongly-typed communication.
/// </summary>
public interface IGameHubClient
{
    Task ReceiveChatMessage(ChatMessage message);
    Task ReceiveMessage(string user, string message); // For compatibility with simple clients
    Task ReceiveSystemMessage(string message);
    Task ReceiveZoneStats(GlobalZoneStats stats);
    Task GameOver(GameOverMessage message);
    Task GameRestarted();
}

/// <summary>
/// SignalR hub for real-time game communication over the internet.
/// </summary>
public class GameHub : Hub<IGameHubClient>
{
    private readonly Orleans.IGrainFactory _grainFactory;
    private readonly ILogger<GameHub> _logger;
    private static readonly Dictionary<string, CancellationTokenSource> _statsSubscriptions = new();

    public GameHub(Orleans.IGrainFactory grainFactory, ILogger<GameHub> logger)
    {
        _grainFactory = grainFactory;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId} from {UserIdentifier}", 
            Context.ConnectionId, Context.UserIdentifier ?? "Anonymous");
        
        // Send welcome message
        var welcomeMessage = new ChatMessage(
            "System",
            "Game Server",
            "Welcome to the game! You are now connected to the global chat.",
            DateTime.UtcNow,
            true
        );
        await Clients.Caller.ReceiveChatMessage(welcomeMessage);
        
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}, Exception: {Exception}", 
            Context.ConnectionId, exception?.Message ?? "None");
        
        // Cancel any active subscriptions
        if (_statsSubscriptions.TryGetValue(Context.ConnectionId, out var cts))
        {
            cts.Cancel();
            _statsSubscriptions.Remove(Context.ConnectionId);
        }
        
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Client sends a chat message to all connected clients.
    /// </summary>
    public async Task SendMessage(string user, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }
        
        // Sanitize user name
        if (string.IsNullOrWhiteSpace(user))
        {
            user = "Anonymous";
        }
        
        // Limit lengths
        if (user.Length > 50)
        {
            user = user.Substring(0, 50);
        }
        
        if (message.Length > 500)
        {
            message = message.Substring(0, 500);
        }
        
        var chatMessage = new ChatMessage(
            Context.ConnectionId,
            user,
            message,
            DateTime.UtcNow,
            false
        );

        _logger.LogInformation("Chat message from {Sender}: {Message}", 
            chatMessage.SenderName, chatMessage.Message);

        // Broadcast through Orleans
        var worldManager = _grainFactory.GetGrain<IWorldManagerGrain>(0);
        await worldManager.BroadcastChatMessage(chatMessage);
    }

    /// <summary>
    /// Client subscribes to zone statistics updates.
    /// </summary>
    public async Task SubscribeToZoneStats(int intervalSeconds = 1)
    {
        // Validate interval
        intervalSeconds = Math.Max(1, Math.Min(60, intervalSeconds));
        
        _logger.LogInformation("Client {ConnectionId} subscribing to zone stats with {Interval}s interval", 
            Context.ConnectionId, intervalSeconds);
        
        // Cancel any existing subscription
        if (_statsSubscriptions.TryGetValue(Context.ConnectionId, out var existingCts))
        {
            existingCts.Cancel();
        }
        
        // Create new cancellation token for this subscription
        var cts = new CancellationTokenSource();
        _statsSubscriptions[Context.ConnectionId] = cts;
        
        // Start streaming stats to this client
        _ = StreamZoneStatsToClient(Context.ConnectionId, TimeSpan.FromSeconds(intervalSeconds), cts.Token);
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Unsubscribe from zone statistics updates.
    /// </summary>
    public Task UnsubscribeFromZoneStats()
    {
        _logger.LogInformation("Client {ConnectionId} unsubscribing from zone stats", Context.ConnectionId);
        
        if (_statsSubscriptions.TryGetValue(Context.ConnectionId, out var cts))
        {
            cts.Cancel();
            _statsSubscriptions.Remove(Context.ConnectionId);
        }
        
        return Task.CompletedTask;
    }

    private async Task StreamZoneStatsToClient(string connectionId, TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            var worldManager = _grainFactory.GetGrain<IWorldManagerGrain>(0);
            
            await foreach (var stats in worldManager.StreamZoneStatistics(interval).WithCancellation(cancellationToken))
            {
                await Clients.Client(connectionId).ReceiveZoneStats(stats);
                
                // Check if client is still connected
                if (!_statsSubscriptions.ContainsKey(connectionId))
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming zone stats to client {ConnectionId}", connectionId);
        }
        finally
        {
            _statsSubscriptions.Remove(connectionId);
            _logger.LogInformation("Stopped streaming zone stats to client {ConnectionId}", connectionId);
        }
    }
}