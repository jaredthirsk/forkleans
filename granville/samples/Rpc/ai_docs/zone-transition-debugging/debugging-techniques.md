# Effective Debugging Techniques for Zone Transitions

This document outlines the most effective methods discovered for diagnosing zone transition issues.

## 📊 Log Analysis Patterns

### Standard Warning Log Tags

All zone transition warnings use standardized tags for easy grep/searching:

| Tag | Purpose | Example |
|-----|---------|---------|
| `[HEALTH_MONITOR]` | Health monitoring events and reports | Periodic health reports, anomaly detection |
| `[ZONE_TRANSITION]` | Zone transition events | Transition starts, completions, failures |
| `[POSITION_JUMP]` | Position anomalies | Teleportation, respawn detection |
| `[ZONE_MISMATCH]` | Server zone mismatches | Player in wrong zone for server |
| `[POSITION_SYNC]` | Position synchronization | Server position corrections |
| `[ZONE_DEBOUNCE]` | Debouncing events | Rapid transition prevention |

### High-Impact Grep Patterns

**Chronic Mismatch Analysis**:
```bash
# Count total chronic mismatch occurrences
grep "CHRONIC_MISMATCH" logs/bot-0.log | wc -l

# Show recent chronic mismatch progression
grep -E "CHRONIC_MISMATCH.*[0-9]+ times" logs/bot-0.log | tail -20

# Check if mismatch count is growing rapidly (indicates debouncing issue)
grep "CHRONIC_MISMATCH" logs/bot-0.log | grep "14:4[0-9]" | head -5
```

**Zone Transition Success Analysis**:
```bash
# Monitor recent successful transitions with timing
grep "Successfully connected to zone" logs/bot-0.log | tail -10

# Check for stuck/slow transitions
grep "FORCING transition after" logs/bot-0.log | tail -5

# Verify health reports show good success rates
grep "Success Rate:" logs/bot-0.log | tail -5
```

**RPC Timeout Detection**:
```bash
# Check for the critical RPC timeout crashes
grep "timed out after 30000ms" logs/bot-0-console.log | tail -5

# Monitor player input timeouts (should be controlled)
grep "Player input timed out after 5 seconds" logs/bot-0.log | tail -5

# Check for unobserved task exceptions
grep "Unobserved task exception" logs/bot-0-console.log | tail -5
```

**Zone Oscillation Analysis**:
```bash
# Track zone change patterns to detect oscillation
grep "Ship moved to zone" logs/bot-0.log | tail -20

# Check debouncer blocking behavior (should prevent oscillation) 
grep "ZONE_DEBOUNCE.*BLOCKED" logs/bot-0.log | tail -10
```

### Time-Based Analysis

**Recent Error Pattern**:
```bash
# Focus on errors from specific time period (replace time as needed)
grep -E "(Error|Exception|Failed|FAILED)" logs/bot-0.log | grep "14:4[6-9]" | head -10

# Compare error frequency across time periods
grep "CHRONIC_MISMATCH" logs/bot-0.log | grep "14:30" | wc -l
grep "CHRONIC_MISMATCH" logs/bot-0.log | grep "14:45" | wc -l
```

**Session Boundary Detection**:
```bash
# Find when bot service restarts (useful for before/after analysis)
grep "Bot.*starting in.*mode" logs/bot-0.log

# Find when AppHost restarts  
grep "Distributed application started" logs/apphost.log
```

---

## 🔍 Diagnostic Workflows

### Workflow 1: Diagnosing Chronic Zone Mismatches

**Step 1**: Quantify the problem
```bash
# Get count and rate of chronic mismatches
grep "CHRONIC_MISMATCH" logs/bot-0.log | wc -l
grep "CHRONIC_MISMATCH" logs/bot-0.log | tail -20
```

**Step 2**: Check debouncing effectiveness  
```bash
# Look for rapid count growth (indicates debouncing failure)
grep -E "CHRONIC_MISMATCH.*detected [0-9]+ times" logs/bot-0.log | tail -20
```

**Step 3**: Analyze zone transition attempts
```bash
# Check if transitions are being attempted
grep "FORCING transition" logs/bot-0.log | tail -5

# Check transition success rate
grep "Successfully connected to zone" logs/bot-0.log | tail -10
```

**Step 4**: Look for root cause patterns
```bash
# Check for zone oscillation
grep "Ship moved to zone" logs/bot-0.log | tail -20

# Check for connection state issues  
grep "lost connection.*attempting to reconnect" logs/bot-0.log | tail -10
```

### Workflow 2: Diagnosing RPC Timeout Issues

**Step 1**: Identify timeout type
```bash
# Check for critical 30-second timeouts (crashes)
grep "timed out after 30000ms" logs/bot-0-console.log | tail -5

# Check for controlled 5-second timeouts (expected)
grep "Player input timed out after 5 seconds" logs/bot-0.log | tail -5
```

**Step 2**: Check system stability
```bash
# Look for unobserved task exceptions (indicates fire-and-forget issues)
grep "Unobserved task exception" logs/bot-0-console.log | tail -5

# Check if bot loop is still running
grep "Bot.*status - Connected:" logs/bot-0.log | tail -5
```

**Step 3**: Verify fix effectiveness
```bash
# Confirm fire-and-forget pattern is working (no blocking)
# Should see regular bot status updates even during timeouts
grep "🤖 Bot.*status" logs/bot-0.log | tail -10
```

### Workflow 3: Performance and Success Rate Analysis

**Step 1**: Check health reports
```bash
# Look for recent health summaries
grep -A 5 "Zone Transition Health Report" logs/bot-0.log | tail -20
```

**Step 2**: Analyze transition timing
```bash
# Check average transition speed
grep "Successfully connected to zone.*in.*s" logs/bot-0.log | tail -20

# Look for slow transitions (>2 seconds)
grep "Successfully connected to zone.*[2-9]\.[0-9]s" logs/bot-0.log
```

**Step 3**: Monitor connection health
```bash
# Check pre-established connection status
grep "Pre-connection status after update" logs/bot-0.log | tail -5

# Look for connection refresh activity
grep "Pre-establishing connection to zone" logs/bot-0.log | tail -10
```

---

## 🧪 Testing and Validation Techniques

### Before/After Comparison Method

**Process**:
1. **Record baseline metrics** before making changes
2. **Apply fix** and restart system
3. **Compare same metrics** after stabilization period

**Key Metrics**:
```bash
# Count errors in 10-minute period before fix
grep "CHRONIC_MISMATCH" logs/bot-0.log | grep "14:30" | wc -l

# Count errors in 10-minute period after fix  
grep "CHRONIC_MISMATCH" logs/bot-0.log | grep "14:45" | wc -l

# Success rate comparison
grep "Success Rate:" logs/bot-0.log | grep "14:30" | tail -1
grep "Success Rate:" logs/bot-0.log | grep "14:45" | tail -1
```

### Frequency Analysis

**Most Valuable Pattern**:
```bash
# Count occurrences of different error types
grep -o "\[HEALTH_MONITOR\] [A-Z_]*:" logs/bot-0.log | sort | uniq -c | sort -rn
```

**Example Output**:
```
    425 [HEALTH_MONITOR] CHRONIC_MISMATCH:
     12 [HEALTH_MONITOR] POSITION_JUMP:  
      3 [HEALTH_MONITOR] STALE_WORLD_STATE:
      1 [HEALTH_MONITOR] PROLONGED_MISMATCH:
```

**Interpretation**: Focus on fixing the most frequent issues first.

### Real-Time Monitoring

**Live Error Monitoring**:
```bash
# Watch for new errors in real-time
tail -f logs/bot-0.log | grep -E "(Error|Exception|FAILED|CHRONIC)"

# Monitor specific pattern
tail -f logs/bot-0.log | grep "CHRONIC_MISMATCH"

# Watch health reports  
tail -f logs/bot-0.log | grep -A 5 "Health Report"
```

---

## 🎯 Targeted Investigation Techniques

### Connection State Debugging

**Questions to Answer**:
- Is IsConnected property accurate?
- Are there race conditions in state updates?
- Do connection attempts succeed but state tracking fails?

**Diagnostic Approach**:
```bash
# Trace connection state changes
grep -E "(lost connection|reconnected successfully|IsConnected=)" logs/bot-0.log | tail -20

# Look for state inconsistencies
grep "Skipping connection - already connecting/connected" logs/bot-0.log | tail -10

# Check for rapid connection state flipping
grep -E "(lost connection|reconnected)" logs/bot-0.log | tail -20
```

### Zone Boundary Analysis  

**Questions to Answer**:
- Is hysteresis working correctly?
- Are players oscillating at boundaries?
- Is zone calculation accurate?

**Diagnostic Approach**:
```bash
# Check debouncer activity
grep "ZONE_DEBOUNCE.*BLOCKED" logs/bot-0.log | tail -20

# Look for position near boundaries  
grep "Distances from borders" logs/bot-0.log | tail -10

# Track zone change patterns
grep "Ship moved to zone" logs/bot-0.log | tail -20 | grep -o "zone ([0-9],[0-9])"
```

### Performance Investigation

**Questions to Answer**:
- Are pre-established connections helping or hurting?
- How much time do different transition phases take?
- Are there resource leaks or memory issues?

**Diagnostic Approach**:
```bash
# Measure transition timing
grep "ConnectToActionServer took.*ms" logs/bot-0.log | tail -20

# Check connection establishment success
grep "Pre-established connection test successful" logs/bot-0.log | tail -10

# Look for resource cleanup
grep -E "(Cleanup took|Ending transition)" logs/bot-0.log | tail -20
```

---

## 🚨 Warning Signs to Watch For

### Immediate Action Required
- **RPC timeout count increasing**: `grep "timed out after 30000ms" logs/bot-0-console.log | wc -l`
- **Success rate dropping below 70%**: Check recent health reports
- **Chronic mismatch count growing exponentially**: Compare counts across time periods

### System Degradation Indicators  
- **Transition timing increasing**: Average >2 seconds indicates problems
- **Connection retry attempts increasing**: More "lost connection" messages
- **Position jump frequency increasing**: May indicate sync issues

### Health Check Commands
```bash
# Quick system health check
echo "=== ZONE TRANSITION HEALTH CHECK ==="
echo "Chronic mismatches in last hour:"
grep "CHRONIC_MISMATCH" logs/bot-0.log | grep "$(date '+%Y-%m-%d %H:')" | wc -l
echo "Recent success rate:"  
grep "Success Rate:" logs/bot-0.log | tail -1
echo "Recent transition timing:"
grep "Successfully connected to zone.*in.*s" logs/bot-0.log | tail -5
echo "RPC timeout crashes:"
grep "timed out after 30000ms" logs/bot-0-console.log | wc -l
```

---

## 🔧 Development Environment Tips

### Log File Management
```bash
# Clean old logs before testing
rm -f logs/*.log

# Monitor multiple log files simultaneously  
tail -f logs/bot-0.log logs/client.log logs/apphost.log

# Archive logs with timestamps for comparison
cp logs/bot-0.log "logs/bot-$(date '+%Y%m%d_%H%M%S').log"
```

### Testing Scenarios
1. **Normal Operation**: Let bot run for 10+ minutes, check health reports
2. **Boundary Stress Test**: Place bot near zone boundaries, observe debouncer
3. **Network Stress Test**: Introduce artificial latency/packet loss
4. **Restart Recovery**: Kill/restart individual services, check recovery

### Validation Criteria
- No RPC timeout crashes for 30+ minutes  
- Chronic mismatch count stays <50 consecutive
- Health reports show >80% success rate
- Zone transitions complete in <2 seconds average