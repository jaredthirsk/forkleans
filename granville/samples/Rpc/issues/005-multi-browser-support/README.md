# Issue 005: Support Multiple Concurrent Browser Connections

## Status
Open - Architecture Issue

## Priority
Medium-High (affects testing and real-world usage)

## Description
The Shooter.Client Blazor Server application currently has issues when multiple browsers connect to the same client process simultaneously. This affects:
- AI development loop testing (multiple browser monitors)
- Real-world multiplayer scenarios
- Load testing

## Current Behavior
When multiple browsers connect:
- Player IDs get mixed up between sessions
- Camera tracking fails (player sprite missing errors)
- State appears to be shared improperly between connections

## Expected Behavior
Each browser connection should be completely isolated with:
- Independent player sessions
- Separate game state tracking
- No cross-contamination between connections

## Root Cause
Likely service registration issues in Blazor Server:
- Services registered as **Singleton** that should be **Scoped** (per-connection)
- Static state in services
- Improper lifecycle management

## Services to Investigate
1. `GranvilleRpcGameClientService` - Core game client service
2. `SignalRChatService` - Chat service
3. Any services holding player state, world state, or connection state

## Reproduction
1. Start Shooter.AppHost
2. Open browser 1 at http://localhost:5200/game
3. Open browser 2 at http://localhost:5200/game
4. Observe player ID confusion and camera tracking errors

## Fix Approach
1. Audit all service registrations in `Program.cs`
2. Change Singleton → Scoped for per-connection services
3. Ensure no static state in game services
4. Add integration tests for multi-user scenarios
5. Verify with concurrent browser monitors in dev loop

## Evidence
From dev loop session 20251007-214327:
```
[Browser] WARNING: [CAMERA] Player sprite missing. PlayerId: d292ee75-582a-42e1-8b1f-999113235b5e
[Browser] ERROR: [CAMERA] Cannot recover - no sprite for player d292ee75-582a-42e1-8b1f-999113235b5e
```

Two browsers showed different player IDs but had sprite lookup conflicts.

## Related Issues
- None yet

## Next Steps
1. [ ] Audit service registrations in Shooter.Client/Program.cs
2. [ ] Identify singleton services that hold per-connection state
3. [ ] Change to scoped registration where appropriate
4. [ ] Test with multiple concurrent browsers
5. [ ] Add multi-connection integration tests
