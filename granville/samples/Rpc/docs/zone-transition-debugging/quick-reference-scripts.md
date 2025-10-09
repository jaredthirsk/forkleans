# Quick Reference Scripts for Zone Transition Debugging

This document provides copy-paste scripts for immediate use during debugging sessions.

## 🚨 Emergency Diagnostic Scripts

### Immediate Health Check
```bash
#!/bin/bash
# File: quick-health-check.sh
# Run this first when investigating zone transition issues

echo "=== ZONE TRANSITION EMERGENCY HEALTH CHECK ==="
echo "Timestamp: $(date)"
echo ""

# 1. Check for RPC timeout crashes (most critical)
echo "1. RPC Timeout Crashes:"
RPC_CRASHES=$(grep "timed out after 30000ms" logs/bot-0-console.log 2>/dev/null | wc -l)
if [ $RPC_CRASHES -gt 0 ]; then
    echo "   🚨 CRITICAL: $RPC_CRASHES RPC timeout crashes detected!"
    echo "   → Check fire-and-forget pattern in SendPlayerInputEx"
else
    echo "   ✅ No RPC timeout crashes"
fi

# 2. Check if bot is responsive
echo ""
echo "2. Bot Responsiveness:"
LAST_STATUS=$(grep "🤖 Bot.*status" logs/bot-0.log 2>/dev/null | tail -1)
if [ -n "$LAST_STATUS" ]; then
    echo "   ✅ Bot active: $LAST_STATUS"
else
    echo "   🚨 Bot may be frozen - no status updates found"
fi

# 3. Check chronic mismatch severity
echo ""
echo "3. Chronic Mismatch Status:"
LATEST_MISMATCH=$(grep "CHRONIC_MISMATCH.*detected [0-9]\+ times" logs/bot-0.log 2>/dev/null | tail -1)
if [ -n "$LATEST_MISMATCH" ]; then
    MISMATCH_COUNT=$(echo "$LATEST_MISMATCH" | grep -o "detected [0-9]\+ times" | grep -o "[0-9]\+")
    if [ $MISMATCH_COUNT -gt 100 ]; then
        echo "   🚨 SEVERE: $MISMATCH_COUNT consecutive mismatches"
        echo "   → Check debouncing logic"
    elif [ $MISMATCH_COUNT -gt 50 ]; then
        echo "   ⚠️  MODERATE: $MISMATCH_COUNT consecutive mismatches"
    else
        echo "   ✅ ACCEPTABLE: $MISMATCH_COUNT consecutive mismatches"
    fi
else
    echo "   ✅ No chronic mismatches detected"
fi

# 4. Check recent success rate
echo ""
echo "4. Recent Success Rate:"
RECENT_SUCCESS=$(grep "Successfully connected to zone" logs/bot-0.log 2>/dev/null | tail -20 | wc -l)
RECENT_FORCED=$(grep "FORCING transition after" logs/bot-0.log 2>/dev/null | tail -20 | wc -l)
TOTAL_RECENT=$((RECENT_SUCCESS + RECENT_FORCED))

if [ $TOTAL_RECENT -gt 5 ]; then
    SUCCESS_RATE=$(echo "scale=1; $RECENT_SUCCESS * 100 / $TOTAL_RECENT" | bc 2>/dev/null)
    if [ $(echo "$SUCCESS_RATE < 70" | bc 2>/dev/null) -eq 1 ]; then
        echo "   🚨 POOR: $SUCCESS_RATE% success rate ($RECENT_SUCCESS/$TOTAL_RECENT)"
    elif [ $(echo "$SUCCESS_RATE < 85" | bc 2>/dev/null) -eq 1 ]; then
        echo "   ⚠️  MODERATE: $SUCCESS_RATE% success rate ($RECENT_SUCCESS/$TOTAL_RECENT)"
    else
        echo "   ✅ GOOD: $SUCCESS_RATE% success rate ($RECENT_SUCCESS/$TOTAL_RECENT)"
    fi
else
    echo "   ℹ️  Insufficient data (only $TOTAL_RECENT recent transitions)"
fi

echo ""
echo "=== END HEALTH CHECK ==="
```

### System State Snapshot
```bash
#!/bin/bash
# File: system-snapshot.sh
# Captures complete system state for analysis

SNAPSHOT_DIR="snapshots/$(date +%Y%m%d_%H%M%S)"
mkdir -p $SNAPSHOT_DIR

echo "Creating system snapshot in $SNAPSHOT_DIR..."

# 1. Copy current logs
cp logs/*.log $SNAPSHOT_DIR/ 2>/dev/null

# 2. Capture running processes
scripts/show-shooter-processes.sh > $SNAPSHOT_DIR/running-processes.txt

# 3. Capture recent system state
echo "=== RECENT HEALTH REPORTS ===" > $SNAPSHOT_DIR/health-summary.txt
grep -A 5 "Zone Transition Health Report" logs/bot-0.log | tail -20 >> $SNAPSHOT_DIR/health-summary.txt

echo "" >> $SNAPSHOT_DIR/health-summary.txt
echo "=== RECENT TRANSITIONS ===" >> $SNAPSHOT_DIR/health-summary.txt
grep "Successfully connected to zone" logs/bot-0.log | tail -20 >> $SNAPSHOT_DIR/health-summary.txt

echo "" >> $SNAPSHOT_DIR/health-summary.txt  
echo "=== RECENT ERRORS ===" >> $SNAPSHOT_DIR/health-summary.txt
grep -E "(Error|Exception|FAILED)" logs/bot-0.log | tail -30 >> $SNAPSHOT_DIR/health-summary.txt

# 4. Performance analysis
echo "=== PERFORMANCE METRICS ===" > $SNAPSHOT_DIR/performance.txt
./quick-health-check.sh >> $SNAPSHOT_DIR/performance.txt 2>/dev/null

echo "Snapshot created: $SNAPSHOT_DIR"
echo "Key files:"
ls -la $SNAPSHOT_DIR/
```

---

## 🔍 Real-Time Monitoring Scripts

### Live Issue Tracking
```bash
#!/bin/bash
# File: live-monitor.sh
# Monitor zone transitions in real-time

echo "Starting live zone transition monitoring..."
echo "Press Ctrl+C to stop"
echo ""

# Monitor key events in parallel
{
    echo "=== SUCCESSFUL TRANSITIONS ==="
    tail -f logs/bot-0.log | grep --line-buffered "Successfully connected to zone" | \
        while read line; do
            echo "$(date '+%H:%M:%S') $line"
        done
} &

{
    echo "=== FORCED TRANSITIONS ==="  
    tail -f logs/bot-0.log | grep --line-buffered "FORCING transition after" | \
        while read line; do
            echo "$(date '+%H:%M:%S') $line"
        done
} &

{
    echo "=== CHRONIC MISMATCHES ==="
    tail -f logs/bot-0.log | grep --line-buffered "CHRONIC_MISMATCH" | \
        while read line; do
            echo "$(date '+%H:%M:%S') $line"
        done
} &

{
    echo "=== RPC TIMEOUTS ==="
    tail -f logs/bot-0-console.log | grep --line-buffered "timed out after 30000ms" | \
        while read line; do
            echo "$(date '+%H:%M:%S') 🚨 CRITICAL: $line"
        done
} &

# Wait for Ctrl+C
wait
```

### Performance Dashboard  
```bash
#!/bin/bash
# File: performance-dashboard.sh
# Real-time performance metrics display

while true; do
    clear
    echo "╔══════════════════════════════════════════════════════════════╗"
    echo "║              ZONE TRANSITION PERFORMANCE DASHBOARD           ║" 
    echo "╚══════════════════════════════════════════════════════════════╝"
    echo "Updated: $(date)"
    echo ""
    
    # Success Rate (last 20 transitions)
    RECENT_SUCCESS=$(grep "Successfully connected to zone" logs/bot-0.log 2>/dev/null | tail -20 | wc -l)
    RECENT_FORCED=$(grep "FORCING transition after" logs/bot-0.log 2>/dev/null | tail -20 | wc -l)  
    TOTAL_RECENT=$((RECENT_SUCCESS + RECENT_FORCED))
    
    if [ $TOTAL_RECENT -gt 0 ]; then
        SUCCESS_RATE=$(echo "scale=1; $RECENT_SUCCESS * 100 / $TOTAL_RECENT" | bc)
        printf "📊 Success Rate: %6s%% (%d/%d recent transitions)\n" "$SUCCESS_RATE" $RECENT_SUCCESS $TOTAL_RECENT
    else
        echo "📊 Success Rate: No recent transitions"
    fi
    
    # Average Transition Time
    AVG_TIME=$(grep "Successfully connected to zone.*in.*s" logs/bot-0.log 2>/dev/null | tail -20 | \
        grep -o "in [0-9]\+\.[0-9]\+s" | sed 's/in //; s/s//' | \
        awk '{sum+=$1; count++} END {if(count>0) printf "%.2f", sum/count; else print "N/A"}')
    printf "⏱️  Average Time: %6s seconds\n" "$AVG_TIME"
    
    # Current Mismatch Count
    LATEST_MISMATCH=$(grep "CHRONIC_MISMATCH.*detected [0-9]\+ times" logs/bot-0.log 2>/dev/null | tail -1)
    if [ -n "$LATEST_MISMATCH" ]; then
        MISMATCH_COUNT=$(echo "$LATEST_MISMATCH" | grep -o "detected [0-9]\+ times" | grep -o "[0-9]\+")
        printf "🔄 Mismatch Count: %4s consecutive\n" "$MISMATCH_COUNT"
    else
        echo "🔄 Mismatch Count:    0 consecutive"
    fi
    
    # Connection Uptime
    LATEST_HEALTH=$(grep "Connection Uptime:" logs/bot-0.log 2>/dev/null | tail -1)
    if [ -n "$LATEST_HEALTH" ]; then
        UPTIME=$(echo "$LATEST_HEALTH" | grep -o "[0-9]\+\.[0-9]\+s")
        printf "🔗 Connection Uptime: %s\n" "$UPTIME"
    else
        echo "🔗 Connection Uptime: Unknown"
    fi
    
    # RPC Timeout Status
    RPC_TIMEOUTS=$(grep "timed out after 30000ms" logs/bot-0-console.log 2>/dev/null | wc -l)
    if [ $RPC_TIMEOUTS -gt 0 ]; then
        printf "💥 RPC Timeouts: %4d 🚨 CRITICAL ISSUE!\n" $RPC_TIMEOUTS
    else
        echo "💥 RPC Timeouts:    0 ✅"
    fi
    
    # Recent Activity
    echo ""
    echo "📋 Recent Activity (last 5 events):"
    grep -E "(Successfully connected|FORCING transition|CHRONIC_MISMATCH)" logs/bot-0.log 2>/dev/null | \
        tail -5 | while read line; do
        timestamp=$(echo "$line" | cut -d' ' -f1-2)
        event=$(echo "$line" | cut -d' ' -f4-)
        printf "   %s %s\n" "$timestamp" "$event"
    done
    
    echo ""
    echo "Press Ctrl+C to exit..."
    sleep 5
done
```

---

## 📋 Quick Diagnostic Commands

### Warning Priority Matrix

| Warning | Threshold | Action Priority |
|---------|-----------|-----------------|
| **CHRONIC_MISMATCH** | >10 consecutive mismatches | 🚨 **Critical** - Force reconnect client |
| **LOW_SUCCESS_RATE** | <50% success rate | 🚨 **Critical** - Check server health, network |
| **STUCK_TRANSITION** | >10 seconds | 🚨 **Critical** - Check target server, timeout |
| **PROLONGED_MISMATCH** | >5 seconds | ⚠️ **Warning** - Monitor, may self-resolve |
| **EXCESSIVE_TRANSITIONS** | >5 attempts | ⚠️ **Warning** - Check boundaries |
| **STALE_WORLD_STATE** | >3 seconds | ⚠️ **Warning** - Check server load |
| **POSITION_JUMP** | >100 units | ℹ️ **Info** - Normal during respawn/transition |
| **STALE_PRE_ESTABLISHED** | >30 seconds | ℹ️ **Info** - Auto-cleaned |

### Monitoring Commands from Original Documentation
```bash
# Watch for all warnings in real-time
tail -f logs/bot-0.log | grep -E "\[HEALTH_MONITOR\]|\[ZONE_TRANSITION\]"

# Watch for critical issues only
tail -f logs/bot-0.log | grep -E "PROLONGED_MISMATCH|STUCK_TRANSITION|CHRONIC_MISMATCH"

# Count anomalies by type
grep -o "\[HEALTH_MONITOR\] [A-Z_]*:" logs/bot-0.log | sort | uniq -c | sort -rn

# Get transition success statistics
grep "Health Report" logs/bot-0.log -A 3 | grep "Transitions:"

# Find zones with most issues
grep "PROLONGED_MISMATCH" logs/bot-0.log | grep -o "zone ([0-9]*,[0-9]*)" | sort | uniq -c
```

### One-Liner Health Checks
```bash
# Quick success rate check
echo "Success Rate: $(echo "scale=1; $(grep "Successfully connected to zone" logs/bot-0.log | tail -20 | wc -l) * 100 / $(grep -E "(Successfully connected|FORCING transition)" logs/bot-0.log | tail -20 | wc -l)" | bc)%"

# Quick mismatch status  
grep "CHRONIC_MISMATCH.*detected [0-9]\+ times" logs/bot-0.log | tail -1 | grep -o "detected [0-9]\+ times"

# Quick RPC timeout check
echo "RPC Timeouts: $(grep "timed out after 30000ms" logs/bot-0-console.log | wc -l)"

# Quick transition timing
grep "Successfully connected to zone.*in.*s" logs/bot-0.log | tail -10 | grep -o "in [0-9]\+\.[0-9]\+s"

# Quick bot status
grep "🤖 Bot.*status" logs/bot-0.log | tail -1

# Quick health report
grep -A 5 "Zone Transition Health Report" logs/bot-0.log | tail -6
```

### Log Analysis Shortcuts
```bash
# Find all issues in current hour
CURRENT_HOUR="$(date '+%Y-%m-%d %H:')"
grep -E "(Error|Exception|Failed|FAILED|WARNING)" logs/bot-0.log | grep "$CURRENT_HOUR"

# Compare error counts between time periods
echo "Hour 14:30 errors: $(grep -E "(Error|Exception)" logs/bot-0.log | grep "14:30" | wc -l)"
echo "Hour 14:45 errors: $(grep -E "(Error|Exception)" logs/bot-0.log | grep "14:45" | wc -l)"

# Zone transition pattern analysis
grep "Ship moved to zone" logs/bot-0.log | tail -20 | grep -o "zone ([0-9],[0-9])" | sort | uniq -c

# Boundary blocking analysis
grep "ZONE_DEBOUNCE.*BLOCKED" logs/bot-0.log | tail -10 | grep -o "zone.*at position [^,]*,[^,]*" | sort | uniq -c
```

---

## 🛠️ Fix Verification Scripts

### Verify Fire-and-Forget Fix
```bash
#!/bin/bash
# Verify RPC timeout fix is working

echo "Checking fire-and-forget pattern implementation..."

# 1. Check method signature
if grep -q "public void SendPlayerInputEx" Shooter.Client.Common/GranvilleRpcGameClientService.cs; then
    echo "✅ SendPlayerInputEx is void (non-blocking)"
else
    echo "🚨 SendPlayerInputEx is still async - fix broken!"
fi

# 2. Check bot service call site
if grep -q "await.*SendPlayerInputEx" Shooter.Bot/Services/BotService.cs; then
    echo "🚨 Bot still using await - fix incomplete!"
else
    echo "✅ Bot using non-blocking call"
fi

# 3. Check for background timeout handling
if grep -q "Task.Run.*inputTask\.WaitAsync" Shooter.Client.Common/GranvilleRpcGameClientService.cs; then
    echo "✅ Background timeout handling present"
else
    echo "🚨 Background timeout handling missing!"
fi

# 4. Verify no recent RPC timeout crashes
RPC_CRASHES=$(grep "timed out after 30000ms" logs/bot-0-console.log | wc -l)
echo "RPC timeout crashes: $RPC_CRASHES (should be 0)"
```

### Verify Debouncing Fix
```bash
#!/bin/bash
# Verify chronic mismatch debouncing is working

echo "Checking debouncing implementation..."

# 1. Check for debouncing field
if grep -q "_lastConsecutiveMismatchIncrement" Shooter.Client.Common/ZoneTransitionHealthMonitor.cs; then
    echo "✅ Debouncing field present"
else
    echo "🚨 Debouncing field missing!"
fi

# 2. Check for 1-second debounce logic
if grep -q "timeSinceLastIncrement\.TotalMilliseconds >= 1000" Shooter.Client.Common/ZoneTransitionHealthMonitor.cs; then
    echo "✅ 1-second debounce logic present"
else
    echo "🚨 Debounce logic missing or incorrect!"
fi

# 3. Check mismatch growth rate
echo "Analyzing mismatch growth rate..."
RECENT_MISMATCHES=$(grep "CHRONIC_MISMATCH.*detected [0-9]\+ times" logs/bot-0.log | tail -30)
if [ -n "$RECENT_MISMATCHES" ]; then
    echo "$RECENT_MISMATCHES" | while read line; do
        timestamp=$(echo "$line" | cut -d' ' -f1-2)
        count=$(echo "$line" | grep -o "detected [0-9]\+ times" | grep -o "[0-9]\+")
        echo "  $timestamp: $count mismatches"
    done
else
    echo "✅ No recent chronic mismatches"
fi
```

### Verify Zone Change Detection Fix
```bash
#!/bin/bash
# Verify zone change detection retry logic

echo "Checking zone change detection logic..."

# 1. Check for retry logic in else clause
if grep -A 3 -B 3 "Still in zone mismatch.*CheckForServerTransition" Shooter.Client.Common/GranvilleRpcGameClientService.cs; then
    echo "✅ Retry logic present in zone change detection"
else
    echo "🚨 Retry logic missing - zone changes may not retry!"
fi

# 2. Check for duplicate zone change handling
if grep -q "isNewZoneChange" Shooter.Client.Common/GranvilleRpcGameClientService.cs; then
    echo "✅ New zone change detection logic present"
else
    echo "⚠️  Zone change detection may have issues"
fi

# 3. Monitor zone change patterns
echo "Recent zone change activity:"
grep -E "(Ship moved to zone|Still in zone mismatch)" logs/bot-0.log | tail -10
```

---

## 📊 Performance Analysis Scripts

### Transition Timing Analysis
```bash
#!/bin/bash
# Analyze zone transition timing patterns

echo "=== ZONE TRANSITION TIMING ANALYSIS ==="

# Extract all successful transition times
echo "Transition time distribution:"
grep "Successfully connected to zone.*in.*s" logs/bot-0.log | \
    grep -o "in [0-9]\+\.[0-9]\+s" | sed 's/in //; s/s//' | \
    awk '
    BEGIN { 
        printf "%-12s %s\n", "Range", "Count"
        printf "%-12s %s\n", "------------", "-----"
    }
    {
        if($1 < 0.5) fast++;
        else if($1 < 1.0) good++;  
        else if($1 < 2.0) slow++;
        else if($1 < 5.0) veryslow++;
        else stuck++;
        total++;
    }
    END {
        printf "%-12s %d\n", "Fast (<0.5s)", fast;
        printf "%-12s %d\n", "Good (0.5-1s)", good;
        printf "%-12s %d\n", "Slow (1-2s)", slow;
        printf "%-12s %d\n", "Very Slow (2-5s)", veryslow;
        printf "%-12s %d\n", "Stuck (>5s)", stuck;
        printf "%-12s %d\n", "Total", total;
    }'

echo ""
echo "Average timing by zone:"
grep "Successfully connected to zone" logs/bot-0.log | \
    grep -o "zone ([0-9],[0-9]).*in [0-9]\+\.[0-9]\+s" | \
    sed 's/zone (\([0-9],[0-9]\)).*in \([0-9]\+\.[0-9]\+\)s/\1 \2/' | \
    awk '{
        zone=$1; time=$2;
        sum[zone]+=time; count[zone]++;
    }
    END {
        printf "%-8s %s\n", "Zone", "Avg Time"
        printf "%-8s %s\n", "--------", "--------"
        for(z in sum) printf "%-8s %.2fs (%d)\n", z, sum[z]/count[z], count[z]
    }' | sort
```

### Error Frequency Analysis
```bash
#!/bin/bash
# Analyze error frequency and patterns

echo "=== ERROR FREQUENCY ANALYSIS ==="

# Count different types of errors/warnings
echo "Error type frequency (last 1000 log entries):"
tail -1000 logs/bot-0.log | grep -E "(Error|Warning)" | \
    grep -o "\[[A-Z_]*\]" | sort | uniq -c | sort -rn | \
    awk '{printf "%-30s %d\n", $2, $1}'

echo ""
echo "Hourly error distribution:"
grep -E "(Error|Warning)" logs/bot-0.log | \
    grep -o "^[0-9-]* [0-9]*:" | sort | uniq -c | \
    awk '{printf "%s %d errors\n", $2, $1}' | tail -24
```

---

## 🚀 Quick Fix Application Scripts

### Apply Emergency Fixes
```bash
#!/bin/bash
# File: apply-emergency-fixes.sh
# Apply known fixes for critical issues

echo "Applying emergency fixes for zone transition issues..."

# 1. Check if fire-and-forget pattern needs restoration
if grep -q "public async Task SendPlayerInputEx" Shooter.Client.Common/GranvilleRpcGameClientService.cs; then
    echo "🔧 Restoring fire-and-forget pattern..."
    # Would need to apply the specific fix here
    echo "   Manual fix required - see fixes-applied.md"
fi

# 2. Check if debouncing needs restoration  
if ! grep -q "_lastConsecutiveMismatchIncrement" Shooter.Client.Common/ZoneTransitionHealthMonitor.cs; then
    echo "🔧 Restoring debouncing logic..."
    echo "   Manual fix required - see fixes-applied.md"
fi

# 3. Restart services with clean state
echo "🔄 Restarting services..."
scripts/kill-shooter-processes.sh
sleep 5
cd Shooter.AppHost && dotnet run --no-build &

echo "Emergency fixes applied. Monitor logs for 5 minutes to verify effectiveness."
```

### Performance Optimization Script
```bash
#!/bin/bash
# File: optimize-performance.sh
# Apply performance optimizations based on current metrics

echo "Analyzing current performance for optimization opportunities..."

# Check success rate
RECENT_SUCCESS=$(grep "Successfully connected to zone" logs/bot-0.log | tail -50 | wc -l)
RECENT_FORCED=$(grep "FORCING transition after" logs/bot-0.log | tail -50 | wc -l)
TOTAL_RECENT=$((RECENT_SUCCESS + RECENT_FORCED))

if [ $TOTAL_RECENT -gt 10 ]; then
    SUCCESS_RATE=$(echo "scale=1; $RECENT_SUCCESS * 100 / $TOTAL_RECENT" | bc)
    
    if [ $(echo "$SUCCESS_RATE < 80" | bc) -eq 1 ]; then
        echo "⚠️ Success rate low ($SUCCESS_RATE%) - consider:"
        echo "   - Increasing hysteresis distance (reduce oscillation)"
        echo "   - Checking ActionServer health"
        echo "   - Analyzing network conditions"
    elif [ $(echo "$SUCCESS_RATE > 95" | bc) -eq 1 ]; then
        echo "✅ Success rate excellent ($SUCCESS_RATE%) - consider:"
        echo "   - Reducing hysteresis distance (improve responsiveness)"
        echo "   - Reducing debounce timing (faster transitions)"
    else
        echo "👍 Success rate acceptable ($SUCCESS_RATE%)"
    fi
fi

# Check average timing
AVG_TIME=$(grep "Successfully connected to zone.*in.*s" logs/bot-0.log | tail -30 | \
    grep -o "in [0-9]\+\.[0-9]\+s" | sed 's/in //; s/s//' | \
    awk '{sum+=$1; count++} END {printf "%.2f", sum/count}')

if [ $(echo "$AVG_TIME > 2.0" | bc) -eq 1 ]; then
    echo "⚠️ Average timing slow (${AVG_TIME}s) - consider:"
    echo "   - Checking pre-established connection hit rate"
    echo "   - Analyzing server response times"
    echo "   - Reviewing network latency"
elif [ $(echo "$AVG_TIME < 0.8" | bc) -eq 1 ]; then
    echo "✅ Average timing excellent (${AVG_TIME}s)"
else
    echo "👍 Average timing acceptable (${AVG_TIME}s)"
fi

echo ""
echo "Current system appears to be functioning normally."
echo "No automatic optimizations applied - use manual tuning if needed."
```

This collection of quick reference scripts provides immediate diagnostic capabilities and helps maintain system health without requiring deep technical knowledge of the underlying implementation.