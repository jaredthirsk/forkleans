# Phase 1: UFX.Orleans.SignalRBackplane Implementation Plan

## Overview
Implementing SignalR with UFX.Orleans.SignalRBackplane for secure, internet-facing real-time communication.

## Architecture

```
Internet Clients ←→ SignalR Hub (Silo) ←→ UFX Orleans Backplane ←→ Orleans Grains
                                              ↓
                                        ActionServers
```

## Implementation Steps

### 1. Add NuGet Packages to Silo

```xml
<PackageReference Include="UFX.Orleans.SignalRBackplane" Version="8.2.2" />
<PackageReference Include="Microsoft.AspNetCore.SignalR" Version="8.0.*" />
```

### 2. Configure Orleans Silo (Program.cs)

```csharp
// Add SignalR storage
siloBuilder
    .AddMemoryGrainStorage("SignalRStorage")
    .AddSignalRBackplane(); // UFX backplane

// Add SignalR to services
builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    options.AddPolicy("GameClients", policy =>
    {
        policy.AllowAnyOrigin() // Configure appropriately for production
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Map SignalR hub
app.UseRouting();
app.UseCors("GameClients");
app.MapHub<GameHub>("/gamehub");
```

### 3. Create GameHub (Silo)

```csharp
using Microsoft.AspNetCore.SignalR;
using Shooter.Shared.Models;
using Shooter.Shared.GrainInterfaces;

namespace Shooter.Silo.Hubs;

public interface IGameHubClient
{
    Task ReceiveChatMessage(ChatMessage message);
    Task ReceiveZoneStats(GlobalZoneStats stats);
    Task GameOver(GameOverMessage message);
    Task GameRestarted();
}

public class GameHub : Hub<IGameHubClient>
{
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<GameHub> _logger;

    public GameHub(IGrainFactory grainFactory, ILogger<GameHub> logger)
    {
        _grainFactory = grainFactory;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    // Client -> Server: Send chat message
    public async Task SendMessage(string message)
    {
        var chatMessage = new ChatMessage(
            Context.ConnectionId,
            Context.UserIdentifier ?? "Anonymous",
            message,
            DateTime.UtcNow,
            false
        );

        var worldManager = _grainFactory.GetGrain<IWorldManagerGrain>(0);
        await worldManager.BroadcastChatMessage(chatMessage);
    }

    // Client -> Server: Subscribe to zone stats
    public async Task SubscribeToZoneStats()
    {
        // Start streaming stats to this connection
        _ = StreamZoneStatsToClient(Context.ConnectionId);
        await Task.CompletedTask;
    }

    private async Task StreamZoneStatsToClient(string connectionId)
    {
        var worldManager = _grainFactory.GetGrain<IWorldManagerGrain>(0);
        
        await foreach (var stats in worldManager.StreamZoneStatistics(TimeSpan.FromSeconds(1)))
        {
            await Clients.Client(connectionId).ReceiveZoneStats(stats);
        }
    }
}
```

### 4. Update WorldManagerGrain to use SignalR

```csharp
public class WorldManagerGrain : Grain, IWorldManagerGrain
{
    private IHubContext<GameHub, IGameHubClient>? _hubContext;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // Get SignalR hub context
        _hubContext = ServiceProvider.GetService<IHubContext<GameHub, IGameHubClient>>();
        return base.OnActivateAsync(cancellationToken);
    }

    public async Task BroadcastChatMessage(ChatMessage message)
    {
        _logger.LogInformation("Broadcasting chat message from {Sender}: {Message}", 
            message.SenderName, message.Message);
        
        // Broadcast to all SignalR clients
        if (_hubContext != null)
        {
            await _hubContext.Clients.All.ReceiveChatMessage(message);
        }
        
        // Also send to ActionServers (existing logic)
        // ...
    }
}
```

### 5. Client Implementation (Blazor)

```csharp
@using Microsoft.AspNetCore.SignalR.Client

@code {
    private HubConnection? hubConnection;
    private List<ChatMessage> messages = new();
    private GlobalZoneStats? currentStats;

    protected override async Task OnInitializedAsync()
    {
        hubConnection = new HubConnectionBuilder()
            .WithUrl(Navigation.ToAbsoluteUri("/gamehub"))
            .Build();

        hubConnection.On<ChatMessage>("ReceiveChatMessage", (message) =>
        {
            messages.Add(message);
            InvokeAsync(StateHasChanged);
        });

        hubConnection.On<GlobalZoneStats>("ReceiveZoneStats", (stats) =>
        {
            currentStats = stats;
            InvokeAsync(StateHasChanged);
        });

        await hubConnection.StartAsync();
        await hubConnection.SendAsync("SubscribeToZoneStats");
    }

    private async Task SendMessage(string message)
    {
        if (hubConnection is not null)
        {
            await hubConnection.SendAsync("SendMessage", message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (hubConnection is not null)
        {
            await hubConnection.DisposeAsync();
        }
    }
}
```

### 6. External Client Support

For external applications (non-browser):

```csharp
// Install: UFX.Orleans.SignalRBackplane.Client

var client = new SignalRBackplaneClient("https://silo-url");
await client.SendToAllAsync("ReceiveChatMessage", new[] { chatMessage });
```

## Security Considerations

1. **Authentication**: Add JWT authentication to SignalR hub
2. **CORS**: Configure appropriate origins for production
3. **Rate Limiting**: Implement message rate limiting
4. **Input Validation**: Validate all client messages
5. **HTTPS**: Always use HTTPS in production

## Benefits of UFX Approach

1. **Production Ready**: Handles crashes, cleanup, scale-out
2. **Performance**: Direct grain-to-grain messaging
3. **External Clients**: Dedicated client package for non-browser apps
4. **No Extra Infrastructure**: No Redis/Service Bus needed
5. **Orleans Integration**: Each connection/user/group is a grain

## Migration Path

1. Keep RPC for game logic (low latency)
2. Use SignalR for chat and stats (internet-safe)
3. ActionServers continue using RPC
4. Web clients use SignalR
5. Future: Add authentication and groups