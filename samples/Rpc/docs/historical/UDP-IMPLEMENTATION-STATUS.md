# UDP Implementation Status

## Overview
This document tracks the implementation of UDP-based communication for the Shooter sample, including both direct LiteNetLib and Forkleans RPC approaches.

## Update: Forkleans RPC Now Supported!
We discovered that the incompatibility between Orleans RPC and Orleans clustering was due to a property key validation issue. After fixing this in the Forkleans fork, we can now use both:
- **Orleans clustering** for distributed state management
- **Forkleans RPC** for UDP-based RPC communication
- **Direct LiteNetLib** for custom game protocols

See [FORKLEANS-RPC-STATUS.md](FORKLEANS-RPC-STATUS.md) for details on the RPC implementation.

## Current Status: COMPLETED ✅

### What's Implemented

1. **UDP Server in ActionServer**
   - `UdpGameServer` class using LiteNetLib
   - Dynamic UDP port allocation (9000-9100 range)
   - World state broadcasting at 60Hz
   - Player connection management
   - Heartbeat monitoring (30s timeout)
   - Binary serialization using Orleans serializers

2. **UDP Client in Blazor Client**
   - `UdpGameClientService` class
   - Connects to ActionServer's UDP port
   - Receives real-time world state updates
   - Sends player input with low latency
   - Automatic heartbeat sending
   - Connection state management

3. **Message Protocol**
   - Shared `MessageType` enum in `Shooter.Shared`
   - Efficient binary protocol
   - Message types:
     - Connect/Disconnect
     - PlayerInput
     - WorldStateUpdate
     - Heartbeat
     - ServerInfo

4. **Integration**
   - UDP port registered with Orleans silo
   - Configuration switch between HTTP/UDP modes
   - Seamless fallback to HTTP if needed
   - UI displays connection type

## Benefits Achieved

1. **Lower Latency** - Direct UDP vs HTTP polling
2. **Higher Update Rate** - 60Hz updates vs polling interval
3. **Reduced Bandwidth** - Binary protocol vs JSON
4. **Better Scalability** - Less server overhead
5. **Real-time Gameplay** - Immediate input response

## Architecture

```
┌─────────────┐         UDP          ┌────────────────┐
│   Client    │◄────────────────────►│  ActionServer  │
│ (Blazor)    │      LiteNetLib      │  (UDP Server)  │
└─────────────┘                      └────────────────┘
      │                                       │
      │ HTTP (Registration)                   │ HTTP
      ▼                                       ▼
┌─────────────┐                      ┌────────────────┐
│    Silo     │◄────────────────────►│ World Manager  │
│  (Orleans)  │      Orleans RPC     │    (Grain)     │
└─────────────┘                      └────────────────┘
```

## Configuration

### Enable UDP Mode (default)
In `Shooter.Client/appsettings.json`:
```json
{
  "UseUdpClient": true
}
```

### Switch to HTTP Mode
```json
{
  "UseUdpClient": false
}
```

## Code Locations

### Server Side
- `/Shooter.ActionServer/Services/UdpGameServer.cs` - UDP server implementation
- `/Shooter.ActionServer/Program.cs` - Server registration

### Client Side
- `/Shooter.Client/Services/UdpGameClientService.cs` - UDP client
- `/Shooter.Client/Pages/Game.razor` - UI integration

### Shared
- `/Shooter.Shared/Models/UdpMessages.cs` - Message types

## Testing

1. Start the Aspire AppHost:
   ```bash
   cd Shooter.AppHost
   dotnet run
   ```

2. Open the game in browser
3. Check console logs for "UDP Game Server started on port XXXX"
4. Connect to the game - UI should show "Connection: UDP"
5. Monitor network traffic to verify UDP packets

## Future Enhancements

1. **Reliability Layer** - Add reliable messaging for critical updates
2. **Compression** - Implement delta compression for world states
3. **Encryption** - Add packet encryption for security
4. **NAT Traversal** - Support players behind NAT
5. **Lag Compensation** - Implement client-side prediction
6. **Network Smoothing** - Add interpolation/extrapolation

## Troubleshooting

### Client Can't Connect
- Check firewall rules for UDP ports 9000-9100
- Verify ActionServer registered with correct IP
- Check client has correct server endpoint

### High Latency
- Monitor network conditions
- Check serialization performance
- Verify update rate (should be 60Hz)

### Connection Drops
- Check heartbeat timeout (30s)
- Monitor for packet loss
- Verify network stability