# Zone Transition Deadlock Issue

## Issue Summary
Players get stuck in a prolonged zone mismatch state where they are physically in one zone but remain connected to the ActionServer for a different zone. This results in no world state updates and eventual connection timeout.

## Severity: CRITICAL

## Symptoms
- `[HEALTH_MONITOR] PROLONGED_MISMATCH` errors in client logs
- Player stuck in wrong zone for 28+ seconds
- No world state updates for 24+ seconds
- Position not updated for extended periods
- Zone transitions start but never complete

## Example Error
```
2025-09-12 11:09:39.658 [Error] Shooter.Client.Common.ZoneTransitionHealthMonitor: 
[HEALTH_MONITOR] PROLONGED_MISMATCH: Player in zone (0,0) but connected to server for zone (1,0) for 28684.909ms
```

## Root Cause Analysis
The zone transition logic appears to have a deadlock condition where:
1. Client detects player has moved to a new zone
2. Client initiates zone transition
3. Server never completes the handoff to the new zone's ActionServer
4. Client remains connected to the wrong ActionServer
5. No world state updates are received because the player is not in the connected server's zone

## Evidence from Logs
```
2025-09-12 11:08:45.354 [Information] Shooter.Client.Common.GranvilleRpcGameClientService: 
[ZONE TRANSITION] Ship moved to zone (0,0) but client still connected to server for zone (0,1) 
at position Vector2 { X = 495.06747, Y = 489.9326 } (starting transition)

2025-09-12 11:09:11.166 [Information] Shooter.Client.Common.GranvilleRpcGameClientService: 
[ZONE TRANSITION] Ship moved to zone (0,0) but client still connected to server for zone (0,1) 
at position Vector2 { X = 467.27957, Y = 487.73602 } (starting transition)
```

Multiple transition attempts are made but none complete successfully.

## Impact
- Players cannot move between zones
- Game becomes unplayable when crossing zone boundaries
- World state stops updating
- Eventually leads to connection timeout and disconnection

## Related Components
- `GranvilleRpcGameClientService.cs` - Handles zone transition logic
- `ZoneTransitionHealthMonitor.cs` - Detects and reports zone mismatches
- `GameRpcGrain.cs` - Server-side zone management
- ActionServer cross-zone communication

## Reproduction Steps
1. Start the game with AppHost
2. Move a player character near a zone boundary
3. Cross the zone boundary
4. Observe that the player gets stuck and stops receiving updates

## Potential Solutions
1. Add timeout and retry logic to zone transitions
2. Implement forced reconnection when prolonged mismatch detected
3. Add server-side validation and correction of zone assignments
4. Improve ActionServer-to-ActionServer handoff protocol
5. Add circuit breaker pattern to prevent infinite transition attempts

## Configuration Context
- Silo ports: 7071 (HTTP), 7080 (HTTPS), 7081 (dashboard)
- ActionServer ports: 7072-7075
- RPC ports: 12000-12003
- Transport: LiteNetLib (UDP)

## Detection Timestamp
2025-09-12 11:09:39 AM

## Environment
- .NET 9.0
- Orleans 9.1.2
- Granville RPC
- Running in WSL2/Linux environment