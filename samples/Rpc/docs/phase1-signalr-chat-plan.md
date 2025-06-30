# Phase 1: SignalR Chat Implementation Plan

## Architecture Decision: SignalR on Silo with OrgnalR

### Why SignalR on Silo?
- **Security**: SignalR is designed for internet-facing communication (HTTPS, auth, CORS)
- **Orleans is not secure**: Orleans RPC is for internal cluster communication only
- **Single source of truth**: Silo already coordinates all zones
- **Clean separation**: RPC for game logic, SignalR for chat/stats

### Why OrgnalR?
- **Active development**: GitHub activity as of last week
- **Orleans v9 support**: Has v9 NuGet packages
- **SignalR backplane**: Enables multi-silo scenarios
- **Better than alternatives**: UFX.Orleans.SignalRBackplane and others are less maintained

## Implementation Plan

### 1. Add SignalR to Silo Project
```csharp
// Program.cs
builder.Services.AddSignalR()
    .AddOrgnalR(); // Orleans backplane for multi-silo support

builder.Services.AddCors(options =>
{
    options.AddPolicy("GameClients", policy =>
    {
        policy.WithOrigins("http://localhost:5000") // Client URL
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});
```

### 2. Create SignalR Hubs
```csharp
public interface IGameHub
{
    // Server -> Client
    Task ReceiveChatMessage(ChatMessage message);
    Task ReceiveZoneStats(GlobalZoneStats stats);
    Task GameOver(GameOverMessage message);
    Task GameRestarted();
}

public class GameHub : Hub<IGameHub>
{
    private readonly IGrainFactory _grainFactory;
    
    // Client -> Server
    public async Task SendMessage(string message)
    {
        var chatMessage = new ChatMessage(...);
        var worldManager = _grainFactory.GetGrain<IWorldManagerGrain>(0);
        await worldManager.BroadcastChatMessage(chatMessage);
    }
    
    public async Task SubscribeToZoneStats()
    {
        // Start streaming zone stats to this connection
    }
}
```

### 3. Orleans Observer for ActionServer -> Silo Communication
```csharp
public interface IChatObserver : IGrainObserver
{
    void OnChatMessage(ChatMessage message);
}

// In WorldManagerGrain
private readonly ObserverManager<IChatObserver> _chatObservers;

public Task Subscribe(IChatObserver observer)
{
    _chatObservers.Subscribe(observer, observer);
    return Task.CompletedTask;
}

public async Task BroadcastChatMessage(ChatMessage message)
{
    // Notify SignalR hub
    await _hubContext.Clients.All.ReceiveChatMessage(message);
    
    // Also notify any Orleans observers (ActionServers)
    _chatObservers.Notify(o => o.OnChatMessage(message));
}
```

### 4. Client Implementation
```javascript
// Blazor/JavaScript client
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/gamehub")
    .build();

connection.on("ReceiveChatMessage", (message) => {
    // Add to chat UI
});

connection.on("ReceiveZoneStats", (stats) => {
    // Update stats display
});

await connection.start();
await connection.invoke("SubscribeToZoneStats");
```

### 5. Security Considerations
- Add authentication to SignalR hub
- Use HTTPS in production
- Implement rate limiting for chat messages
- Validate all client input
- Consider JWT tokens for auth

## Benefits of This Approach

1. **Internet-safe**: SignalR handles WebSockets, SSE, long-polling
2. **Scalable**: OrgnalR enables multi-silo scenarios
3. **Real-time**: Push notifications for chat and stats
4. **Clean architecture**: Clear separation of concerns
5. **Future-proof**: Easy to add more real-time features

## Migration Path

1. Keep existing RPC chat for zone-local messages (low latency)
2. Add SignalR for global chat and stats
3. Clients can use both: RPC for game, SignalR for chat
4. Eventually migrate all non-game communication to SignalR