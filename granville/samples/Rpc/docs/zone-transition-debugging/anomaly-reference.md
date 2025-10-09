# Zone Transition Anomaly Reference Guide

This document provides detailed explanations of all warning messages and anomalies that can occur in the zone transition system.

## 🚨 Critical Anomalies (Immediate Action Required)

### CHRONIC_MISMATCH
```
[HEALTH_MONITOR] CHRONIC_MISMATCH: Zone mismatch detected 11 times consecutively!
```

**Meaning**: Zone mismatch has occurred more than 10 times in a row (with debouncing: 10+ seconds of continuous mismatch).

**Possible Causes**:
- Zone transition system failure
- Player stuck at zone boundary  
- Debouncer preventing legitimate transitions
- Server routing issues
- **Recent Fix**: Was caused by explosive counter growth (425+ consecutive), now controlled by debouncing

**Resolution Steps**:
1. Check debouncer state: `grep "ZONE_DEBOUNCE" logs/bot-0.log | tail -20`
2. Look for boundary crossing patterns: `grep "Distances from borders" logs/bot-0.log | tail -10`
3. Verify zone assignment logic: `grep "Ship moved to zone" logs/bot-0.log | tail -20`
4. Check if transitions are being attempted: `grep "FORCING transition" logs/bot-0.log | tail -5`
5. May require client reconnection if >100 consecutive

**Fixed Patterns**: No longer expect 400+ consecutive mismatches due to debouncing improvements.

---

### LOW_SUCCESS_RATE
```
[HEALTH_MONITOR] LOW_SUCCESS_RATE: Only 40% of recent transitions succeeded
```

**Meaning**: Less than 50% of recent zone transitions have succeeded.

**Possible Causes**:
- Systematic connection issues
- ActionServer availability problems
- Network problems  
- Configuration issues

**Resolution Steps**:
1. Check server logs for errors
2. Verify network connectivity: `grep "Failed to connect" logs/bot-0.log | tail -20`
3. Look for patterns in failed transitions: `grep "TRANSITION_FAILED" logs/bot-0.log | tail -20`
4. Check server resource usage
5. Verify ActionServer processes: `scripts/show-shooter-processes.sh | grep ActionServer`

---

### STUCK_TRANSITION
```
[HEALTH_MONITOR] STUCK_TRANSITION: Transition has been in progress for 10500ms
```

**Meaning**: A zone transition is taking longer than 10 seconds to complete.

**Possible Causes**:
- Network timeout
- ActionServer not responding
- RPC connection establishment failed
- Manifest synchronization issues

**Resolution Steps**:
1. Check for connection timeout errors: `grep "Connection timeout" logs/bot-0.log`
2. Verify target server is running: `scripts/show-shooter-processes.sh`
3. Look for RPC manifest errors: `grep "manifest" logs/bot-0.log`
4. Check network latency/packet loss

---

## ⚠️ Warning Anomalies (Monitor Closely)

### PROLONGED_MISMATCH
```
[HEALTH_MONITOR] PROLONGED_MISMATCH: Player in zone (1,0) but connected to server for zone (0,0) for 5234ms
```

**Meaning**: Player has been connected to the wrong server for over 5 seconds.

**Possible Causes**:
- Zone transition failed or stuck
- Server assignment incorrect
- Network issues preventing transition

**Resolution Steps**:
1. Check for `STUCK_TRANSITION` warnings
2. Look for failed transition attempts: `grep "ZONE_TRANSITION.*Failed" logs/bot-0.log`
3. Verify server availability for target zone
4. Check network connectivity
5. **Note**: May self-resolve within forced transition timeout (5s)

---

### EXCESSIVE_TRANSITIONS
```
[HEALTH_MONITOR] EXCESSIVE_TRANSITIONS: 6 transition attempts in current session!
```

**Meaning**: More than 5 zone transition attempts in the current session.

**Possible Causes**:
- Player moving rapidly between zones
- Zone boundary oscillation
- Failed transitions being retried
- Network instability

**Resolution Steps**:
1. Check player movement patterns: `grep "Ship moved to zone" logs/bot-0.log | tail -20`
2. Look for boundary hysteresis issues: `grep "ZONE_DEBOUNCE.*BLOCKED" logs/bot-0.log | tail -20`
3. Verify debouncer is working properly
4. Check network stability

---

### STALE_WORLD_STATE
```
[HEALTH_MONITOR] STALE_WORLD_STATE: No world state received for 3250ms
```

**Meaning**: No world state update received for over 3 seconds.

**Possible Causes**:
- ActionServer stopped sending updates
- Network connection lost
- RPC channel disrupted
- Server overloaded

**Resolution Steps**:
1. Check ActionServer health: `scripts/show-shooter-processes.sh | grep ActionServer`
2. Verify RPC connection status: `grep "IsConnected" logs/bot-0.log | tail -10`
3. Look for network issues: `grep "Connection refused\|Network error" logs/bot-0.log`
4. Check server CPU/memory usage

---

## ℹ️ Informational Anomalies (Normal Operations)

### POSITION_JUMP  
```
[HEALTH_MONITOR] POSITION_JUMP detected: Player jumped 250.5 units from (100.0,450.0) to (350.5,450.0)
```

**Meaning**: Player position changed by more than 100 units in a single update.

**Normal Causes** (No Action Required):
- Player respawned (check for spawn point coordinates ~(500,500))
- Zone transition completed successfully  
- Server-side position correction

**Concerning Patterns**:
- Multiple jumps per minute (may indicate sync issues)
- Jumps without corresponding zone transitions or respawns

**Analysis Commands**:
```bash
# Check if related to respawns (spawn point is typically around 500,500)
grep "POSITION_JUMP" logs/bot-0.log | grep -E "500\.,.*500\."

# Check if related to zone transitions  
grep -B2 -A2 "POSITION_JUMP" logs/bot-0.log | grep "ZONE_TRANSITION"

# Count frequency (>5 per minute may indicate issues)
grep "POSITION_JUMP" logs/bot-0.log | grep "$(date '+%Y-%m-%d %H:%M')" | wc -l
```

---

### STALE_PRE_ESTABLISHED
```
[HEALTH_MONITOR] STALE_PRE_ESTABLISHED: Connection to action-server-1-0 unused for 31000ms
```

**Meaning**: A pre-established connection hasn't been used for over 30 seconds.

**Normal Causes** (Usually Informational):
- Player not moving to that zone  
- Efficient zone routing (player taking different path)

**Concerning Patterns**:
- All pre-established connections becoming stale (may indicate movement issues)
- Rapid stale/refresh cycles (may indicate connection instability)

**Resolution**: Connections are automatically refreshed when needed - no action usually required.

---

### POSITION_SYNC
```
[HEALTH_MONITOR] POSITION_SYNC: Server corrected position from (100.1,200.2) to (100.0,200.0)
```

**Meaning**: Server made a small position correction (normal anti-cheat/sync behavior).

**Normal Operations**: Small corrections (<10 units) are expected and healthy.

**Concerning Patterns**: Large corrections (>50 units) or very frequent corrections may indicate client-server sync issues.

---

## 🔍 Anomaly Correlation Patterns

### Normal Event Sequences

#### Successful Zone Transition
```
[ZONE_TRANSITION] Ship moved to zone (1,0) but client still connected to server for zone (0,0)
[ZONE_DEBOUNCE] Allowing transition to zone (1,0) after debounce  
[ZONE_TRANSITION] Successfully connected to zone (1,0) in 0.5s
[HEALTH_MONITOR] Connected to server for zone (1,0)
[POSITION_JUMP] detected: Player jumped 150.0 units (normal for zone transition)
```

#### Normal Boundary Rejection
```
[ZONE_DEBOUNCE] BLOCKED: Player not far enough into zone (1,0). Distances: L=499.9, R=0.1, B=250.0, T=250.0 (need 2+ units)
```

### Problem Event Sequences

#### Stuck Zone Transition
```
[ZONE_TRANSITION] Ship moved to zone (1,0) but client still connected to server for zone (0,0)
[ZONE_TRANSITION] FORCING transition after 42.1s in wrong zone
[HEALTH_MONITOR] PROLONGED_MISMATCH: Player in zone (1,0) but connected to server for zone (0,0) for 13870ms
[HEALTH_MONITOR] CHRONIC_MISMATCH: Zone mismatch detected 425 times consecutively!
```

#### RPC Timeout Crisis
```
Player input timed out after 5 seconds, marking connection as lost
Bot LiteNetLibTest0 lost connection, attempting to reconnect (attempt 1/5)
RPC request 86a57fa5-78f3-43b4-be60-ed127fe66ee3 timed out after 30000ms
System.TimeoutException: RPC request timed out after 30000ms
```

---

## 📊 Health Report Interpretation

### Sample Health Report
```
[HEALTH_MONITOR] === Zone Transition Health Report ===
[HEALTH_MONITOR] Player Zone: (1,0), Server Zone: (1,0)
[HEALTH_MONITOR] Connection Uptime: 125.3s, Success Rate: 85%
[HEALTH_MONITOR] Transitions: 4 successful, 1 failed
[HEALTH_MONITOR] =====================================
```

### Metric Analysis

#### **Player Zone vs Server Zone**
- **Match**: ✅ System healthy, no immediate issues
- **Mismatch**: ⚠️ Transition in progress or stuck
- **Persistent Mismatch**: 🚨 System problem, investigate immediately

#### **Success Rate**
- **>90%**: ✅ Excellent performance
- **80-90%**: ✅ Good performance  
- **70-80%**: ⚠️ Acceptable but monitor trends
- **50-70%**: ⚠️ Degraded performance, investigate
- **<50%**: 🚨 Poor performance, immediate action required

#### **Connection Uptime**
- **>1800s (30min)**: ✅ Very stable
- **600-1800s (10-30min)**: ✅ Stable
- **300-600s (5-10min)**: ⚠️ Frequent reconnections
- **<300s (5min)**: 🚨 Highly unstable, investigate

#### **Transition Counts**
- **Successful >> Failed**: ✅ Normal operations
- **Successful ≈ Failed**: ⚠️ Moderate issues
- **Failed > Successful**: 🚨 Systematic problems

---

## 🎯 Advanced Correlation Analysis

### Multi-Log Correlation

#### Correlating Client and Server Events
```bash
# Find matching events across client and server logs
TRANSITION_TIME="14:46:14"
echo "Client perspective:"
grep "$TRANSITION_TIME" logs/client.log | grep -E "(ZONE_TRANSITION|Connected)"

echo "Server perspective:"  
grep "$TRANSITION_TIME" logs/actionserver*.log | grep -E "(Player.*connected|Zone.*assigned)"

echo "Bot perspective:"
grep "$TRANSITION_TIME" logs/bot-0.log | grep -E "(Successfully connected|lost connection)"
```

#### Timing Correlation Analysis
```bash
# Analyze timing patterns between detection and completion
grep "Ship moved to zone" logs/bot-0.log | while read line; do
    timestamp=$(echo "$line" | grep -o "^[0-9-]* [0-9:]*\.[0-9]*")
    zone=$(echo "$line" | grep -o "zone ([0-9],[0-9])" | grep -o "([0-9],[0-9])")
    
    # Look for completion within next 10 seconds
    completion=$(grep -A 100 "$timestamp" logs/bot-0.log | \
        grep "Successfully connected to zone $zone" | head -1)
    
    if [ -n "$completion" ]; then
        echo "✅ $timestamp → Zone $zone → Completed"
    else
        echo "❌ $timestamp → Zone $zone → No completion found"
    fi
done | tail -20
```

This anomaly reference provides comprehensive understanding of all warning messages and their operational significance in the zone transition system.