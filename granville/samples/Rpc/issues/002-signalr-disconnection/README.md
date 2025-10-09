# Issue 002: SignalR Disconnection

## Summary
SignalR connections between client and server are being unexpectedly closed, affecting real-time communication for chat and game state updates.

## Status
**Active** - Occurs regularly, may be related to client hang issue

## Symptoms
- SignalR connection closes unexpectedly
- Client loses real-time updates
- Chat functionality stops working
- Game state synchronization interrupted

## Detection
- Error logs show "SignalR connection closed"
- Detected by AI dev loop monitoring
- Occurs in conjunction with other service disruptions

## Evidence

### From AI Dev Loop (2025-09-25 17:45)
```
[17:45:41] SignalR disconnection - Source: client-console.log
        SignalR connection closed
[17:45:42] SignalR disconnection - Source: client.log
  2025-09-25 17:45:35.286 [Error] Shooter.Client.Services.SignalRChatService: SignalR connection closed
```

### From AI Dev Loop (2025-09-25 17:37)
```
[17:37:27] SignalR disconnection - Source: client.log
  2025-09-25 17:37:18.920 [Error] Shooter.Client.Services.SignalRChatService: SignalR connection closed
```

## Impact
- **Chat System**: Users cannot send or receive messages
- **Game State**: Real-time updates stop working
- **User Experience**: Degraded experience requiring manual refresh

## Related Components
- `/Shooter.Client/Services/SignalRChatService.cs`
- `/Shooter.Silo/Hubs/GameHub.cs`
- SignalR backplane (UFX.Orleans.SignalRBackplane)

## Potential Causes
1. **Network interruption** - Temporary network issues
2. **Server timeout** - Server closing idle connections
3. **Client hang** - Related to Issue 001 where client becomes unresponsive
4. **SSL/TLS issues** - Previously had certificate trust problems
5. **Resource exhaustion** - Server running out of connections/memory

## Current Mitigations
- SSL certificate issue resolved (was causing connection failures)
- Connection retry logic exists but may not be working properly

## Next Steps
1. Implement automatic reconnection with exponential backoff
2. Add connection state monitoring and logging
3. Investigate correlation with client hang (Issue 001)
4. Add heartbeat/keepalive mechanism
5. Review SignalR timeout configurations

## Configuration
Current SignalR configuration in client:
- Automatic reconnection enabled
- Default timeout settings
- Using HTTPS transport

## Related Issues
- [Issue 001: Client Hang](../001-client-hang/README.md) - SignalR disconnection occurs around same time as hangs
- [Issue 003: SSL Certificate](../003-ssl-certificate-resolved/README.md) - Previously caused connection failures (now resolved)