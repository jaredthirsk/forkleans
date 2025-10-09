# Zone Transition Troubleshooting Guide

This guide explains how to use the comprehensive health monitoring and warning system to detect, diagnose, and resolve zone transition issues in the Granville RPC Shooter sample.

## Overview

The zone transition health monitoring system provides systematic detection of anomalies with detailed warning logs. All warnings use consistent tags for easy searching and filtering in production logs.

## Warning Log Tags

All zone transition warnings use standardized tags for easy grep/searching:

| Tag | Purpose | Example |
|-----|---------|---------|
| `[HEALTH_MONITOR]` | Health monitoring events and reports | Periodic health reports, anomaly detection |
| `[ZONE_TRANSITION]` | Zone transition events | Transition starts, completions, failures |
| `[POSITION_JUMP]` | Position anomalies | Teleportation, respawn detection |
| `[ZONE_MISMATCH]` | Server zone mismatches | Player in wrong zone for server |
| `[POSITION_SYNC]` | Position synchronization | Server position corrections |
| `[ZONE_DEBOUNCE]` | Debouncing events | Rapid transition prevention |

## Common Anomalies and Their Meanings

### 1. PROLONGED_MISMATCH
```
[HEALTH_MONITOR] PROLONGED_MISMATCH: Player in zone (1,0) but connected to server for zone (0,0) for 5234ms
```
**Meaning**: Player has been connected to the wrong server for over 5 seconds.

**Possible Causes**:
- Zone transition failed or stuck
- Server assignment incorrect
- Network issues preventing transition

**Resolution**:
1. Check for `STUCK_TRANSITION` warnings
2. Look for failed transition attempts in logs
3. Verify server availability for target zone
4. Check network connectivity

### 2. STUCK_TRANSITION
```
[HEALTH_MONITOR] STUCK_TRANSITION: Transition has been in progress for 10500ms
```
**Meaning**: A zone transition is taking longer than 10 seconds to complete.

**Possible Causes**:
- Network timeout
- Server not responding
- RPC connection establishment failed
- Manifest synchronization issues

**Resolution**:
1. Check for connection timeout errors
2. Verify target server is running
3. Look for RPC manifest errors
4. Check network latency/packet loss

### 3. POSITION_JUMP
```
[HEALTH_MONITOR] POSITION_JUMP detected: Player jumped 250.5 units from (100.0,450.0) to (350.5,450.0)
```
**Meaning**: Player position changed by more than 100 units in a single update.

**Possible Causes**:
- Player respawned
- Server-side teleportation
- Zone transition completed
- Network lag causing position correction

**Resolution**:
1. Check if this coincides with respawn (look for spawn point coordinates)
2. Verify if this happens during zone transitions
3. Look for `[POSITION_SYNC]` messages for context

### 4. CHRONIC_MISMATCH
```
[HEALTH_MONITOR] CHRONIC_MISMATCH: Zone mismatch detected 11 times consecutively!
```
**Meaning**: Zone mismatch has occurred more than 10 times in a row.

**Possible Causes**:
- Zone transition system failure
- Player stuck at zone boundary
- Debouncer preventing transitions
- Server routing issues

**Resolution**:
1. Check debouncer state in logs
2. Look for boundary crossing patterns
3. Verify zone assignment logic
4. May require client reconnection

### 5. EXCESSIVE_TRANSITIONS
```
[HEALTH_MONITOR] EXCESSIVE_TRANSITIONS: 6 transition attempts in current session!
```
**Meaning**: More than 5 zone transition attempts in the current session.

**Possible Causes**:
- Player moving rapidly between zones
- Zone boundary oscillation
- Failed transitions being retried
- Network instability

**Resolution**:
1. Check player movement patterns
2. Look for boundary hysteresis issues
3. Verify debouncer is working
4. Check network stability

### 6. LOW_SUCCESS_RATE
```
[HEALTH_MONITOR] LOW_SUCCESS_RATE: Only 40% of recent transitions succeeded
```
**Meaning**: Less than 50% of recent zone transitions have succeeded.

**Possible Causes**:
- Systematic connection issues
- Server availability problems
- Network problems
- Configuration issues

**Resolution**:
1. Check server logs for errors
2. Verify network connectivity
3. Look for patterns in failed transitions
4. Check server resource usage

### 7. STALE_WORLD_STATE
```
[HEALTH_MONITOR] STALE_WORLD_STATE: No world state received for 3250ms
```
**Meaning**: No world state update received for over 3 seconds.

**Possible Causes**:
- Server stopped sending updates
- Network connection lost
- RPC channel disrupted
- Server overloaded

**Resolution**:
1. Check server health
2. Verify RPC connection status
3. Look for network issues
4. Check server CPU/memory

### 8. STALE_PRE_ESTABLISHED
```
[HEALTH_MONITOR] STALE_PRE_ESTABLISHED: Connection to action-server-1-0 unused for 31000ms
```
**Meaning**: A pre-established connection hasn't been used for over 30 seconds.

**Possible Causes**:
- Player not moving to that zone
- Connection may have degraded
- Server may have restarted

**Resolution**:
1. This is usually informational
2. Connection will be refreshed if needed
3. Check if server is still available

## Health Report Interpretation

Every 30 seconds, a comprehensive health report is logged:

```
[HEALTH_MONITOR] === Zone Transition Health Report ===
[HEALTH_MONITOR] Player Zone: (1,0), Server Zone: (1,0)
[HEALTH_MONITOR] Connection Uptime: 125.3s, Success Rate: 85%
[HEALTH_MONITOR] Transitions: 4 successful, 1 failed
[HEALTH_MONITOR] =====================================
```

**Key Metrics**:
- **Player Zone vs Server Zone**: Should match; mismatch indicates problems
- **Connection Uptime**: How long current connection has been active
- **Success Rate**: Percentage of recent transitions that succeeded
- **Transition Counts**: Total successful and failed transitions

## Troubleshooting Workflows

### Workflow 1: Player Stuck in Wrong Zone

1. **Search for PROLONGED_MISMATCH**:
   ```bash
   grep "PROLONGED_MISMATCH" game.log
   ```

2. **Check transition attempts**:
   ```bash
   grep "ZONE_TRANSITION.*Starting transition" game.log | tail -20
   ```

3. **Look for failures**:
   ```bash
   grep "ZONE_TRANSITION.*Failed" game.log
   ```

4. **Check debouncer state**:
   ```bash
   grep "ZONE_DEBOUNCE" game.log | tail -20
   ```

### Workflow 2: Frequent Disconnections

1. **Check transition success rate**:
   ```bash
   grep "Success Rate:" game.log | tail -10
   ```

2. **Look for connection errors**:
   ```bash
   grep -E "Failed to connect|Connection timeout|RPC.*failed" game.log
   ```

3. **Check for excessive transitions**:
   ```bash
   grep "EXCESSIVE_TRANSITIONS" game.log
   ```

### Workflow 3: Performance Issues

1. **Check for position jumps**:
   ```bash
   grep "POSITION_JUMP" game.log | wc -l
   ```

2. **Monitor world state freshness**:
   ```bash
   grep "STALE_WORLD_STATE" game.log | tail -20
   ```

3. **Check pre-established connections**:
   ```bash
   grep "Pre-establishing connection" game.log | tail -20
   ```

## Configuration Tuning

The health monitor uses these thresholds (defined in `ZoneTransitionHealthMonitor.cs`):

```csharp
private const int MAX_MISMATCH_DURATION_MS = 5000;      // Prolonged mismatch threshold
private const int MAX_TRANSITION_DURATION_MS = 10000;   // Stuck transition threshold
private const int STALE_WORLD_STATE_THRESHOLD_MS = 3000; // Stale world state
private const int MAX_CONSECUTIVE_MISMATCHES = 10;      // Chronic mismatch threshold
private const int MAX_TRANSITION_ATTEMPTS = 5;          // Excessive transitions
private const float POSITION_JUMP_THRESHOLD = 100f;     // Position jump detection
```

Adjust these values based on your network conditions and requirements.

## Monitoring in Production

### Real-time Monitoring
```bash
# Watch for all warnings in real-time
tail -f game.log | grep -E "\[HEALTH_MONITOR\]|\[ZONE_TRANSITION\]"

# Watch for critical issues only
tail -f game.log | grep -E "PROLONGED_MISMATCH|STUCK_TRANSITION|CHRONIC_MISMATCH"
```

### Log Analysis
```bash
# Count anomalies by type
grep -o "\[HEALTH_MONITOR\] [A-Z_]*:" game.log | sort | uniq -c | sort -rn

# Get transition success statistics
grep "Health Report" game.log -A 3 | grep "Transitions:"

# Find zones with most issues
grep "PROLONGED_MISMATCH" game.log | grep -o "zone ([0-9]*,[0-9]*)" | sort | uniq -c
```

### Alerting

Set up alerts for critical patterns:

1. **Critical Alert**: CHRONIC_MISMATCH or LOW_SUCCESS_RATE
2. **Warning Alert**: PROLONGED_MISMATCH lasting > 10 seconds
3. **Info Alert**: EXCESSIVE_TRANSITIONS or frequent POSITION_JUMP

## Best Practices

1. **Enable Verbose Logging During Development**:
   - Set log level to Debug for detailed transition information
   - Use Trace level for packet-level debugging

2. **Monitor Health Reports**:
   - Check health reports regularly in production
   - Look for degrading success rates
   - Watch for increasing failed transition counts

3. **Correlate Events**:
   - Zone transitions often trigger position jumps (normal)
   - Stale world state often precedes connection issues
   - Chronic mismatches usually indicate systematic problems

4. **Pre-emptive Action**:
   - If success rate drops below 70%, investigate immediately
   - Multiple STUCK_TRANSITION warnings indicate server issues
   - CHRONIC_MISMATCH requires immediate attention

## Common Solutions

### Force Reconnection
If a client is stuck with chronic mismatches:
```csharp
// In the game client
await RpcGameClient.DisconnectAsync();
await Task.Delay(1000);
await RpcGameClient.ConnectAsync(playerName);
```

### Reset Debouncer
If transitions are being blocked:
```csharp
// Force end cooldown if stuck
_zoneTransitionDebouncer.ForceEndCooldown();
```

### Manual Zone Transition
To force a specific zone transition:
```csharp
// Force transition to specific zone
var targetZone = new GridSquare(1, 0);
await RpcGameClient.PerformZoneTransition(targetZone);
```

## Integration with Monitoring Systems

The health monitor can be integrated with monitoring systems like:

- **Prometheus**: Export metrics from health reports
- **Grafana**: Visualize transition success rates
- **ELK Stack**: Aggregate and search logs
- **Application Insights**: Track custom events

Example Prometheus metrics:
```
# HELP zone_transition_success_rate Success rate of zone transitions
# TYPE zone_transition_success_rate gauge
zone_transition_success_rate 0.85

# HELP zone_transition_duration_seconds Duration of zone transitions
# TYPE zone_transition_duration_seconds histogram
zone_transition_duration_seconds_bucket{le="1"} 15
zone_transition_duration_seconds_bucket{le="5"} 18
zone_transition_duration_seconds_bucket{le="10"} 19
```

## Conclusion

The zone transition health monitoring system provides comprehensive visibility into the health of zone transitions. By following the warning logs and using the troubleshooting workflows, you can quickly identify and resolve issues in production environments.

Remember:
- Monitor health reports regularly
- Set up alerts for critical warnings
- Correlate multiple warning types for root cause analysis
- Use the standardized tags for efficient log searching
- Adjust thresholds based on your specific requirements