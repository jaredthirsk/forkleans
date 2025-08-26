# Zone Transition Monitoring - Quick Reference Card

## 🚨 Critical Warnings (Immediate Action Required)

| Warning | Threshold | Action |
|---------|-----------|--------|
| **CHRONIC_MISMATCH** | >10 consecutive mismatches | Force reconnect client |
| **LOW_SUCCESS_RATE** | <50% success rate | Check server health, network |
| **STUCK_TRANSITION** | >10 seconds | Check target server, timeout |

## ⚠️ Important Warnings (Monitor Closely)

| Warning | Threshold | Action |
|---------|-----------|--------|
| **PROLONGED_MISMATCH** | >5 seconds | Monitor, may self-resolve |
| **EXCESSIVE_TRANSITIONS** | >5 attempts | Check player movement, boundaries |
| **STALE_WORLD_STATE** | >3 seconds | Check server load, RPC connection |

## ℹ️ Informational Warnings (Normal Operations)

| Warning | Threshold | Notes |
|---------|-----------|-------|
| **POSITION_JUMP** | >100 units | Normal during respawn/transition |
| **STALE_PRE_ESTABLISHED** | >30 seconds | Connections cleaned up automatically |
| **POSITION_SYNC** | N/A | Server correcting client position |

## 🔍 Quick Diagnostics Commands

```bash
# Show current health status
grep "Health Report" game.log | tail -1 -A 3

# Count warnings in last hour
grep "\[HEALTH_MONITOR\]" game.log | grep "$(date -d '1 hour ago' '+%Y-%m-%d %H')" | wc -l

# Find problem zones
grep "PROLONGED_MISMATCH" game.log | grep -o "zone ([0-9]*,[0-9]*)" | sort | uniq -c | sort -rn

# Check success rate trend
grep "Success Rate:" game.log | tail -10

# Monitor in real-time
tail -f game.log | grep -E "MISMATCH|STUCK|EXCESSIVE"
```

## 🛠️ Quick Fixes

### Client Stuck in Wrong Zone
```bash
# Find affected player
grep "CHRONIC_MISMATCH" game.log | tail -1

# Solution: Force reconnection in client code
```

### Low Success Rate
```bash
# Check which transitions are failing
grep "TRANSITION_FAILED" game.log | tail -20

# Check server availability
grep "action-server" game.log | grep "Failed to connect"
```

### Excessive Position Jumps
```bash
# Check if related to respawns
grep "POSITION_JUMP" game.log | grep -E "0\..*,.*500" # Spawn point

# Or zone transitions
grep -B2 -A2 "POSITION_JUMP" game.log | grep "ZONE_TRANSITION"
```

## 📊 Health Report Format

```
[HEALTH_MONITOR] === Zone Transition Health Report ===
[HEALTH_MONITOR] Player Zone: (X,Y), Server Zone: (X,Y)  ← Should match!
[HEALTH_MONITOR] Connection Uptime: XXXs, Success Rate: XX% ← >70% good
[HEALTH_MONITOR] Transitions: X successful, X failed ← Watch failed count
```

## 🎯 Key Metrics to Monitor

| Metric | Good | Warning | Critical |
|--------|------|---------|----------|
| Success Rate | >80% | 50-80% | <50% |
| Failed Transitions | <2/hour | 2-10/hour | >10/hour |
| Mismatch Duration | <1s | 1-5s | >5s |
| Position Jumps | <5/min | 5-20/min | >20/min |

## 🔧 Configuration Locations

- **Thresholds**: `ZoneTransitionHealthMonitor.cs` (constants at top)
- **Debouncer Settings**: `ZoneTransitionDebouncer.cs` (constants)
- **Connection Timeouts**: `GranvilleRpcGameClientService.cs`
- **Health Report Interval**: Constructor in `GranvilleRpcGameClientService.cs` (default: 30s)

## 📝 Log Locations

- **Client Logs**: `Shooter.Client/logs/`
- **Server Logs**: `Shooter.ActionServer/logs/`
- **Silo Logs**: `Shooter.Silo/logs/`

## 🚦 Status Indicators

✅ **Healthy**: Success rate >80%, no CHRONIC warnings
⚠️ **Degraded**: Success rate 50-80%, occasional PROLONGED warnings  
❌ **Critical**: Success rate <50%, CHRONIC_MISMATCH present

## 📞 Escalation Path

1. **Level 1**: PROLONGED_MISMATCH (<3 occurrences) - Monitor
2. **Level 2**: Multiple STUCK_TRANSITION or LOW_SUCCESS_RATE - Investigate
3. **Level 3**: CHRONIC_MISMATCH or system-wide LOW_SUCCESS_RATE - Immediate action

---
*Generated for Granville RPC Shooter Sample - Zone Transition Health Monitoring v1.0*