# Bot Connection Failures Issue

## Issue Summary
Bots are unable to connect to the game, experiencing registration failures and immediate disconnection. This appears to be related to HTTP connection issues when trying to reach ActionServers.

## Severity: HIGH

## Symptoms
- `Error registering player` in bot logs
- `Bot failed to connect to game` messages
- HTTP connection refused errors
- SignalR connections closing immediately after registration failure

## Example Errors
```
2025-09-12 11:09:45.849 [Error] Shooter.Client.Common.GranvilleRpcGameClientService: Error registering player
2025-09-12 11:09:45.850 [Error] Shooter.Bot.Services.BotService: Bot LiteNetLibTest0 failed to connect to game
2025-09-12 11:09:45.865 [Error] Shooter.Bot.Services.BotSignalRChatService: Bot Bot SignalR connection closed
```

## Root Cause Analysis

### Primary Issue: HTTP Connection Refused
```
Exception: System.Net.Http.HttpRequestException: Connection refused (localhost:7072)
 ---> System.Net.Sockets.SocketException (111): Connection refused
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error, CancellationToken cancellationToken)
```

The bot is trying to connect to port 7072 (ActionServer) but getting connection refused. 

### Possible Causes
1. **Timing Issue**: Bot tries to connect before ActionServer is fully ready
2. **Service Discovery**: Bot may be getting incorrect endpoint from service discovery
3. **Port Binding**: ActionServer may not be properly bound to the expected port
4. **Certificate Issues**: SSL/TLS certificate validation failures in development

## Investigation Notes
- Port 7072 is confirmed to be bound to ActionServer (verified with netstat)
- The error occurs during HTTP request to register player
- Resilience handler and service discovery are in the call stack

## Impact
- Bots cannot join the game
- Automated testing is impossible
- Load testing cannot be performed
- Multiplayer scenarios cannot be tested

## Related Components
- `BotService.cs` - Bot connection logic
- `GranvilleRpcGameClientService.cs` - Player registration
- `BotSignalRChatService.cs` - Chat connection
- Service discovery and HTTP client configuration

## Attempted Fixes
1. Modified `Shooter.Bot/Program.cs` to bypass certificate validation in development:
```csharp
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

2. Created `appsettings.Development.json` with HTTP endpoint configuration

## Current Configuration
- Bot connects to Silo via HTTPS
- ActionServers on ports 7072-7075
- Service discovery via Aspire
- Resilience policies applied to HTTP requests

## Reproduction Steps
1. Start AppHost
2. Wait for all services to start
3. Observe bot-0 logs
4. Bot will fail to connect with registration error

## Potential Solutions
1. Add retry logic with exponential backoff for initial connection
2. Implement health checks to ensure ActionServer is ready before bot connects
3. Add explicit wait/dependency in AppHost configuration
4. Review service discovery configuration for correct endpoint resolution
5. Add connection pooling and connection reuse
6. Implement graceful degradation when initial connection fails

## Environment Context
- Running in .NET Aspire orchestrated environment
- WSL2/Linux environment
- Development certificates in use
- Multiple ActionServer instances (4)
- LiteNetLib transport for game communication

## Detection Timestamp
2025-09-12 11:09:45 AM

## Related Issues
- May be related to zone transition issues if bot manages to connect briefly
- SignalR connection failures are a consequence of registration failure