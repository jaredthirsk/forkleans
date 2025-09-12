# SignalR Connection Closures Issue

## Issue Summary
SignalR connections are unexpectedly closing for both the client and bot applications. This appears to be a cascading failure following other connection issues.

## Severity: MEDIUM

## Symptoms
- `SignalR connection closed` errors in client and bot logs
- Chat functionality stops working
- Real-time updates cease
- Clients lose ability to receive push notifications

## Example Errors
```
2025-09-12 11:11:31.156 [Error] SignalR connection closed
2025-09-12 11:09:45.865 [Error] Shooter.Bot.Services.BotSignalRChatService: Bot Bot SignalR connection closed
```

## Root Cause Analysis

### For Bots
The SignalR connection closure appears to be a **consequence** of the primary registration failure:
1. Bot fails to register with the game (HTTP connection refused)
2. Without successful registration, SignalR connection cannot be established/maintained
3. SignalR connection closes immediately

### For Clients
The SignalR closure may be related to:
1. **Zone transition issues** - When stuck in wrong zone for extended period
2. **Timeout** - Connection times out after no activity
3. **Server-side closure** - Server closes connection due to invalid state

## Investigation Timeline
1. 11:09:45 - Bot registration fails
2. 11:09:45 - Bot SignalR connection closes (immediately after)
3. 11:11:31 - Client SignalR connection closes (after prolonged zone mismatch)

## Impact
- Loss of real-time chat functionality
- No push notifications for game events
- Degraded user experience
- Players appear offline to each other

## Related Components
- `SignalRChatService.cs` - Client-side SignalR management
- `BotSignalRChatService.cs` - Bot-side SignalR management
- `GameHub.cs` - Server-side SignalR hub
- `UFX.Orleans.SignalRBackplane` - Orleans SignalR integration

## Connection Configuration
```csharp
// From the codebase
builder.Services.AddHttpClient("SiloClient", client =>
{
    client.BaseAddress = new Uri(siloUrl);
})
.ConfigurePrimaryHttpMessageHandler(() =>
{
    var handler = new HttpClientHandler();
    if (builder.Environment.IsDevelopment())
    {
        handler.ServerCertificateCustomValidationCallback = 
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
    }
    return handler;
});
```

## Correlation with Other Issues
1. **Bot Connection Failures** - SignalR closes as consequence
2. **Zone Transition Deadlock** - May trigger client SignalR closure
3. Both are symptoms of deeper connectivity/state management issues

## Potential Solutions
1. **Implement reconnection logic**:
   - Automatic reconnection with exponential backoff
   - State recovery after reconnection

2. **Add connection health monitoring**:
   - Periodic ping/pong to keep connection alive
   - Early detection of connection issues

3. **Fix root causes**:
   - Resolve bot registration issues first
   - Fix zone transition deadlocks

4. **Improve error handling**:
   - Graceful degradation when SignalR unavailable
   - Queue messages for delivery when reconnected

5. **Add circuit breaker**:
   - Prevent cascade failures
   - Fail fast with clear error messages

## Observed Patterns
- SignalR failures always follow other connection issues
- Never occurs in isolation
- Timing suggests it's a symptom, not a cause

## Environment Context
- Running in Aspire orchestrated environment
- Using UFX.Orleans.SignalRBackplane for scaling
- WebSocket transport for SignalR
- Development certificates in use

## Detection Timestamp
- Bot: 2025-09-12 11:09:45 AM
- Client: 2025-09-12 11:11:31 AM

## Priority Note
This issue should be addressed **after** fixing:
1. Bot connection/registration failures
2. Zone transition deadlocks

As those are likely the root causes of SignalR closures.