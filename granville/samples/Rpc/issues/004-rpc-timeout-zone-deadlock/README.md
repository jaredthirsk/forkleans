# Issue 004: RPC Timeout Causing Zone Transition Deadlock

## Status
**OPEN** - Discovered 2025-10-07

## Summary
Zone transitions are failing due to 30-second RPC request timeouts, causing players to be stuck in mismatched zones for extended periods (31+ seconds observed).

## Symptoms
- Player position indicates zone (0,1) but still connected to server for zone (0,0)
- Zone mismatch persists for 30+ seconds
- RPC requests timing out after 30000ms
- Zone transition never completes

## Evidence
From `ai-dev-loop/20251007-134106`:
```
[HEALTH_MONITOR] PROLONGED_MISMATCH: Player in zone (0,1) but connected to server for zone (0,0) for 31933.1115ms

System.TimeoutException: RPC request cb5942e0-48df-43ed-9ee2-5b45ad057475 timed out after 30000ms
```

## Root Cause Analysis
1. Zone transition initiates when player moves to new zone
2. `PerformZoneTransition()` attempts to connect to new ActionServer via RPC
3. RPC request times out after 30 seconds
4. Zone transition fails, leaving player in mismatched state
5. Health monitor detects prolonged mismatch (>10s threshold)

## Potential Causes
- Network connectivity issues between client and ActionServer
- ActionServer overload/unresponsive
- RPC connection pool exhaustion
- Deadlock in RPC layer
- UDP packet loss

## Related Fixes
- ✅ Fixed intra-server zone tracking (GranvilleRpcGameClientService.cs:4000-4002)
- ✅ Adjusted zone mismatch thresholds (5-10s = warning, >10s = critical)
- ⏳ RPC timeout root cause still under investigation

## Next Steps
1. Add detailed RPC performance logging
2. Investigate why RPC requests are timing out
3. Consider reducing RPC timeout or adding retries
4. Check ActionServer health/responsiveness
5. Review UDP transport layer for packet loss

## Workaround
None currently - this is a blocking issue for zone transitions.
