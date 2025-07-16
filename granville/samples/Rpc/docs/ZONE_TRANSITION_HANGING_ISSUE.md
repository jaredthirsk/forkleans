# Zone Transition Hanging Issue

## Problem Summary

Players experience periods where they can't see anything move due to zone transitions getting stuck during RPC handshake, causing world state polling to stop for 15-30 seconds until the watchdog restarts it.

## Root Cause

Zone transitions are hanging during the RPC handshake phase, leaving clients in a permanently disconnected state until the watchdog timer detects the issue and forces a reconnection.

## Problem Chain

1. **Player crosses zone boundary** → Zone transition triggered
2. **`Cleanup()` called** → Sets `IsConnected = false`, stops all timers 
3. **RPC handshake initiated** → "Waiting for RPC handshake..." logged
4. **Handshake hangs/times out** → Never completes, no error logged
5. **Client stuck disconnected** → `IsConnected` never set back to `true`
6. **World state polling stops** → No game updates visible to player
7. **Watchdog detects issue** → After 15-30 seconds, forces reconnection
8. **Cycle repeats** → Next zone transition causes same issue

## Symptoms

- **Player perspective**: Game freezes for 15-30 seconds, entities stop moving
- **Client logs**: Repeated "World state polling appears to have stopped" warnings
- **Zone transition logs**: "Waiting for RPC handshake..." without completion
- **Slow cleanup times**: 1500ms+ cleanup operations during hanging
- **Endless reconnection attempts**: Watchdog triggers reconnection instead of timer restart

## Evidence from Logs

### Typical Hanging Sequence:
```
21:02:56.622 [Info] [ZONE_TRANSITION] Waiting for RPC handshake...
21:02:59.143 [Info] [ZONE_TRANSITION] Cleanup took 1524.9688ms
21:03:01.983 [Info] Connecting to new Orleans RPC server at 127.0.0.1:12002
```

### Watchdog Detection:
```
21:03:32.495 [Warning] World state polling appears to have stopped. Last poll was 18.1 seconds ago
21:03:32.496 [Warning] Polling stopped and client is disconnected. Attempting reconnection.
21:03:32.500 [Info] Connection test successful, resetting failure count and restarting timers
```

### Pattern:
- Zone transitions every 30-60 seconds
- Handshake hangs for 15-30 seconds
- Watchdog recovery works but creates poor user experience

## Current Workarounds

1. **Automatic Recovery**: Watchdog timer detects and restarts after 15-30 seconds
2. **Manual Recovery**: Browser refresh restarts client state
3. **Avoidance**: Stay in single zone to prevent transitions

## Impact

- **Poor User Experience**: Regular game freezes during movement
- **Gameplay Disruption**: Combat and navigation affected
- **Performance Perception**: Game appears buggy/unstable

## Technical Details

### Files Involved:
- `Shooter.Client.Common/GranvilleRpcGameClientService.cs` - Zone transition logic
- `CheckForServerTransitionInternal()` - Calls Cleanup(), starts transition
- `ConnectToActionServer()` - RPC handshake logic
- Watchdog timer in `CheckPollingHealth()` - Recovery mechanism

### Key Variables:
- `IsConnected` - Set to false during Cleanup(), should be restored after successful connection
- `_isTransitioning` - Should prevent multiple concurrent transitions
- `_worldStateTimer` - Stopped during Cleanup(), needs restart after connection

### Handshake Process:
1. Connect to new RPC server endpoint
2. Initialize LiteNetLib UDP transport  
3. Wait for Orleans RPC client handshake
4. Obtain game grain reference
5. Call ConnectPlayer RPC
6. Test connection with GetWorldState
7. Set IsConnected = true, restart timers

## Potential Root Causes

1. **Network Issues**: UDP handshake failing or timing out
2. **Orleans RPC Client**: Grain resolution or connection problems  
3. **Server Load**: Target action server overwhelmed
4. **Concurrency Issues**: Multiple transitions interfering
5. **Resource Cleanup**: Improper disposal of previous connections
6. **Timeout Configuration**: Insufficient timeouts for handshake completion

## Next Steps

1. **Add timeout handling** to RPC handshake process
2. **Improve error logging** during handshake phase
3. **Implement fallback logic** for failed handshakes
4. **Add connection retry logic** with exponential backoff
5. **Consider connection pooling** to reduce handshake overhead
6. **Optimize cleanup process** to reduce transition time

## Fix Priority

**HIGH** - This significantly impacts gameplay experience and occurs frequently during normal play.

---

*Last updated: 2025-07-15*
*Issue discovered during client hanging investigation*