# Zone Transition Performance Analysis Guide

This document provides methods for analyzing and optimizing zone transition performance in the Granville RPC Shooter sample.

## 📊 Performance Metrics Overview

### Key Performance Indicators (KPIs)

#### Primary Metrics
1. **Transition Success Rate** - Percentage of zone transitions that complete successfully
2. **Average Transition Time** - Time from zone change detection to completion  
3. **Connection Uptime** - How long connections stay stable without issues
4. **Chronic Mismatch Rate** - Frequency of sustained zone mismatches

#### Secondary Metrics  
1. **Pre-connection Hit Rate** - How often pre-established connections are used
2. **Forced Transition Rate** - How often transitions require the 5-second timeout
3. **Position Jump Frequency** - Rate of large position corrections
4. **Reconnection Frequency** - How often bots need to reconnect

---

## 📈 Baseline Performance Expectations

### Optimal Performance Profile
```
Transition Success Rate:     >95%
Average Transition Time:     0.3-0.8 seconds  
Connection Uptime:          >30 minutes
Chronic Mismatch Count:     <10 consecutive
Pre-connection Hit Rate:    >80%
Forced Transition Rate:     <5% of transitions
Position Jump Frequency:    <1 per minute
Reconnection Frequency:     <1 per 10 minutes
```

### Acceptable Performance Profile
```
Transition Success Rate:     80-95%
Average Transition Time:     0.5-2.0 seconds
Connection Uptime:          >10 minutes  
Chronic Mismatch Count:     <50 consecutive
Pre-connection Hit Rate:    >60%
Forced Transition Rate:     <15% of transitions
Position Jump Frequency:    <3 per minute
Reconnection Frequency:     <1 per 5 minutes
```

### Performance Alert Thresholds
```
Transition Success Rate:     <70% (immediate attention)
Average Transition Time:     >5.0 seconds (investigate)
Connection Uptime:          <5 minutes (critical issue)
Chronic Mismatch Count:     >100 consecutive (broken)
Forced Transition Rate:     >30% (major issues)
```

---

## 🔍 Performance Data Collection

### Automated Metrics Collection Script

Create this script for ongoing performance monitoring:

```bash
#!/bin/bash
# File: collect-zone-performance.sh

LOG_FILE="logs/bot-0.log"
TIMEFRAME="$(date '+%Y-%m-%d %H:')"  # Current hour
OUTPUT_FILE="performance-report-$(date +%Y%m%d_%H%M%S).txt"

echo "=== Zone Transition Performance Report ===" > $OUTPUT_FILE
echo "Generated: $(date)" >> $OUTPUT_FILE
echo "Timeframe: Last hour ($TIMEFRAME)" >> $OUTPUT_FILE
echo "" >> $OUTPUT_FILE

# Success Rate Analysis
echo "--- TRANSITION SUCCESS RATE ---" >> $OUTPUT_FILE
RECENT_SUCCESS=$(grep "Successfully connected to zone" $LOG_FILE | grep "$TIMEFRAME" | wc -l)
FORCED_TRANSITIONS=$(grep "FORCING transition after" $LOG_FILE | grep "$TIMEFRAME" | wc -l)
TOTAL_ATTEMPTS=$((RECENT_SUCCESS + FORCED_TRANSITIONS))

if [ $TOTAL_ATTEMPTS -gt 0 ]; then
    SUCCESS_RATE=$(echo "scale=1; $RECENT_SUCCESS * 100 / $TOTAL_ATTEMPTS" | bc)
    echo "Successful Transitions: $RECENT_SUCCESS" >> $OUTPUT_FILE
    echo "Forced Transitions: $FORCED_TRANSITIONS" >> $OUTPUT_FILE  
    echo "Total Attempts: $TOTAL_ATTEMPTS" >> $OUTPUT_FILE
    echo "Success Rate: $SUCCESS_RATE%" >> $OUTPUT_FILE
else
    echo "No transition attempts detected in timeframe" >> $OUTPUT_FILE
fi

# Timing Analysis
echo "" >> $OUTPUT_FILE
echo "--- TRANSITION TIMING ---" >> $OUTPUT_FILE
grep "Successfully connected to zone.*in.*s" $LOG_FILE | grep "$TIMEFRAME" | \
    grep -o "in [0-9]\+\.[0-9]\+s" | \
    sed 's/in //; s/s//' | \
    awk '{sum+=$1; count++} END {
        if(count>0) {
            printf "Average Time: %.2f seconds\n", sum/count;
            printf "Total Measurements: %d\n", count;
        } else {
            print "No timing data available";
        }
    }' >> $OUTPUT_FILE

# Chronic Mismatch Analysis  
echo "" >> $OUTPUT_FILE
echo "--- CHRONIC MISMATCH ANALYSIS ---" >> $OUTPUT_FILE
LATEST_MISMATCH=$(grep "CHRONIC_MISMATCH.*detected [0-9]\+ times" $LOG_FILE | grep "$TIMEFRAME" | tail -1)
if [ -n "$LATEST_MISMATCH" ]; then
    echo "Latest: $LATEST_MISMATCH" >> $OUTPUT_FILE
    MISMATCH_COUNT=$(echo "$LATEST_MISMATCH" | grep -o "detected [0-9]\+ times" | grep -o "[0-9]\+")
    echo "Current Mismatch Count: $MISMATCH_COUNT" >> $OUTPUT_FILE
else
    echo "No chronic mismatches detected" >> $OUTPUT_FILE
fi

# Connection Health
echo "" >> $OUTPUT_FILE  
echo "--- CONNECTION HEALTH ---" >> $OUTPUT_FILE
RECONNECTIONS=$(grep "lost connection.*attempting to reconnect" $LOG_FILE | grep "$TIMEFRAME" | wc -l)
echo "Reconnection Events: $RECONNECTIONS" >> $OUTPUT_FILE

# Health Report Summary
LATEST_HEALTH=$(grep -A 5 "Zone Transition Health Report" $LOG_FILE | tail -5)
if [ -n "$LATEST_HEALTH" ]; then
    echo "" >> $OUTPUT_FILE
    echo "--- LATEST HEALTH REPORT ---" >> $OUTPUT_FILE  
    echo "$LATEST_HEALTH" >> $OUTPUT_FILE
fi

echo "" >> $OUTPUT_FILE
echo "Report saved to: $OUTPUT_FILE"
cat $OUTPUT_FILE
```

### Manual Performance Sampling

For quick performance checks:

```bash
# Quick success rate check (last 20 transitions)
echo "Recent Transition Analysis:"
RECENT_SUCCESS=$(grep "Successfully connected to zone" logs/bot-0.log | tail -20 | wc -l)
RECENT_FORCED=$(grep "FORCING transition after" logs/bot-0.log | tail -20 | wc -l)  
echo "Successful: $RECENT_SUCCESS, Forced: $RECENT_FORCED"

# Average timing for recent transitions
echo "Average Timing:"
grep "Successfully connected to zone.*in.*s" logs/bot-0.log | tail -20 | \
    grep -o "in [0-9]\+\.[0-9]\+s" | sed 's/in //; s/s//' | \
    awk '{sum+=$1; count++} END {printf "%.2f seconds average\n", sum/count}'

# Current mismatch status
echo "Current Mismatch Status:"
grep "CHRONIC_MISMATCH.*detected [0-9]\+ times" logs/bot-0.log | tail -1
```

---

## 📉 Performance Analysis Techniques

### Trend Analysis

#### Success Rate Trending
```bash
# Extract hourly success rates for trending
for hour in {00..23}; do
    TIMEFRAME="$(date '+%Y-%m-%d') $hour:"
    SUCCESS=$(grep "Successfully connected to zone" logs/bot-0.log | grep "$TIMEFRAME" | wc -l)
    FORCED=$(grep "FORCING transition after" logs/bot-0.log | grep "$TIMEFRAME" | wc -l)
    TOTAL=$((SUCCESS + FORCED))
    if [ $TOTAL -gt 0 ]; then
        RATE=$(echo "scale=1; $SUCCESS * 100 / $TOTAL" | bc)
        echo "$hour:00 - Success Rate: $RATE% ($SUCCESS/$TOTAL)"
    fi
done
```

#### Timing Distribution Analysis
```bash
# Analyze transition time distribution
echo "Transition Time Distribution:"
grep "Successfully connected to zone.*in.*s" logs/bot-0.log | \
    grep -o "in [0-9]\+\.[0-9]\+s" | sed 's/in //; s/s//' | \
    awk '
    {
        if($1 < 0.5) fast++;
        else if($1 < 1.0) good++;  
        else if($1 < 2.0) slow++;
        else if($1 < 5.0) veryslow++;
        else stuck++;
        total++;
    }
    END {
        printf "Fast (<0.5s): %d (%.1f%%)\n", fast, fast*100/total;
        printf "Good (0.5-1.0s): %d (%.1f%%)\n", good, good*100/total;
        printf "Slow (1.0-2.0s): %d (%.1f%%)\n", slow, slow*100/total;
        printf "Very Slow (2.0-5.0s): %d (%.1f%%)\n", veryslow, veryslow*100/total;
        printf "Stuck (>5.0s): %d (%.1f%%)\n", stuck, stuck*100/total;
    }'
```

### Correlation Analysis

#### Performance vs Time of Day
```bash
# Check if performance varies by time of day
echo "Performance by Hour of Day:"
for hour in {00..23}; do
    TIMEFRAME="$hour:"
    grep "Successfully connected to zone.*in.*s" logs/bot-0.log | grep "$TIMEFRAME" | \
        grep -o "in [0-9]\+\.[0-9]\+s" | sed 's/in //; s/s//' | \
        awk -v h="$hour" '{sum+=$1; count++} END {
            if(count>0) printf "%s:00 - Avg: %.2fs (%d samples)\n", h, sum/count, count
        }'
done
```

#### Zone-Specific Performance
```bash
# Analyze performance by target zone
echo "Performance by Target Zone:"
grep "Successfully connected to zone" logs/bot-0.log | \
    grep -o "zone ([0-9],[0-9]).*in [0-9]\+\.[0-9]\+s" | \
    sed 's/zone (\([0-9],[0-9]\)).*in \([0-9]\+\.[0-9]\+\)s/\1 \2/' | \
    awk '{
        zone=$1; time=$2;
        sum[zone]+=time; count[zone]++;
    }
    END {
        for(z in sum) printf "Zone %s: %.2fs average (%d transitions)\n", z, sum[z]/count[z], count[z]
    }' | sort
```

---

## 🎯 Performance Optimization Strategies

### Optimization Priority Matrix

#### High Impact, Low Risk
1. **Pre-connection Pool Optimization**
   - Monitor hit rates and adjust pool size
   - Implement connection health checking
   - Add connection refresh logic

2. **Debouncer Tuning**  
   - Adjust hysteresis distance based on boundary behavior
   - Fine-tune debounce timing for responsiveness vs stability

#### High Impact, Medium Risk  
1. **RPC Connection Pooling**
   - Implement connection reuse across transitions
   - Add connection lifetime management
   - Monitor for connection leaks

2. **Zone Calculation Optimization**
   - Cache zone calculations for repeated positions
   - Optimize floating-point boundary calculations
   - Add prediction for moving targets

#### Medium Impact, Low Risk
1. **Logging Optimization**
   - Reduce log verbosity in hot paths  
   - Use structured logging for better parsing
   - Implement log rotation

2. **Health Monitor Optimization**
   - Adjust monitoring frequency
   - Optimize anomaly detection algorithms
   - Add predictive health metrics

### Specific Optimization Techniques

#### Pre-Connection Pool Tuning
```csharp
// Monitor connection pool effectiveness
private void LogConnectionPoolStats()
{
    var hitRate = _connectionPoolHits * 100.0 / _connectionPoolRequests;
    _logger.LogInformation("[PERF] Connection pool hit rate: {HitRate:F1}% ({Hits}/{Requests})",
        hitRate, _connectionPoolHits, _connectionPoolRequests);
    
    // Adjust pool size based on hit rate
    if (hitRate < 70) {
        // Consider pre-establishing more connections
        // or refreshing stale connections more frequently
    }
}
```

#### Transition Time Optimization
```csharp
// Measure and optimize transition phases
using var activity = _activitySource.StartActivity("ZoneTransition");
activity?.SetTag("target.zone", $"{targetZone.X},{targetZone.Y}");

var connectionStart = DateTime.UtcNow;
await EstablishConnection(targetZone);
var connectionTime = (DateTime.UtcNow - connectionStart).TotalMilliseconds;
activity?.SetTag("connection.duration.ms", connectionTime);

var verificationStart = DateTime.UtcNow;  
await VerifyConnection();
var verificationTime = (DateTime.UtcNow - verificationStart).TotalMilliseconds;
activity?.SetTag("verification.duration.ms", verificationTime);
```

---

## 📊 Performance Testing Scenarios

### Load Testing  

#### Multiple Bot Scenario
```bash
# Run multiple bots to test concurrent transitions
for i in {1..5}; do
    echo "Starting bot $i"
    dotnet run --project Shooter.Bot -- --BotName "LoadTestBot$i" &
done

# Monitor aggregate performance
sleep 300  # Run for 5 minutes
echo "Aggregate Performance Results:"
grep "Successfully connected to zone" logs/*.log | wc -l
grep "CHRONIC_MISMATCH" logs/*.log | wc -l
```

#### Boundary Stress Test  
```csharp
// Configure bot to move specifically near zone boundaries
public class BoundaryStressTestController : AutoMoveController
{
    public override (Vector2? move, Vector2? shoot) Update(WorldState world, List<GridSquare> zones, Vector2 position)
    {
        // Move toward zone boundaries intentionally
        var targetBoundary = FindNearestZoneBoundary(position);
        return (Vector2.Normalize(targetBoundary - position), null);
    }
}
```

#### Network Simulation
```bash
# Simulate network conditions with tc (Linux)
sudo tc qdisc add dev lo root handle 1: netem delay 100ms
sudo tc qdisc add dev lo parent 1:1 handle 10: netem loss 1%

# Run performance test
./collect-zone-performance.sh

# Remove network simulation
sudo tc qdisc del dev lo root
```

### Regression Testing

#### Performance Regression Detection
```bash
#!/bin/bash
# Compare performance before/after changes

BASELINE_FILE="performance-baseline.txt"
CURRENT_FILE="performance-current.txt"

# Collect current metrics
./collect-zone-performance.sh > $CURRENT_FILE

# Compare with baseline
echo "Performance Comparison:"
echo "Metric | Baseline | Current | Change"
echo "-------|----------|---------|-------"

# Extract and compare success rates
BASELINE_SUCCESS=$(grep "Success Rate:" $BASELINE_FILE | grep -o "[0-9]\+\.[0-9]\+%" | head -1)
CURRENT_SUCCESS=$(grep "Success Rate:" $CURRENT_FILE | grep -o "[0-9]\+\.[0-9]\+%" | head -1)
echo "Success Rate | $BASELINE_SUCCESS | $CURRENT_SUCCESS | $(echo "$CURRENT_SUCCESS - $BASELINE_SUCCESS" | bc)%"

# Similar comparisons for other metrics...
```

---

## 📈 Continuous Performance Monitoring

### Automated Alerting Setup

#### Performance Alert Script
```bash
#!/bin/bash
# File: performance-alerts.sh
# Run this periodically (e.g., every 5 minutes via cron)

LOG_FILE="logs/bot-0.log"
ALERT_FILE="performance-alerts.log"

# Check for critical performance degradation
RECENT_SUCCESS=$(grep "Successfully connected to zone" $LOG_FILE | tail -20 | wc -l)
RECENT_ATTEMPTS=$(grep -E "(Successfully connected|FORCING transition)" $LOG_FILE | tail -20 | wc -l)

if [ $RECENT_ATTEMPTS -gt 10 ]; then
    SUCCESS_RATE=$(echo "scale=1; $RECENT_SUCCESS * 100 / $RECENT_ATTEMPTS" | bc)
    
    if [ $(echo "$SUCCESS_RATE < 70" | bc) -eq 1 ]; then
        echo "$(date): ALERT - Success rate dropped to $SUCCESS_RATE%" >> $ALERT_FILE
        echo "Recent transitions: $RECENT_SUCCESS/$RECENT_ATTEMPTS" >> $ALERT_FILE
    fi
fi

# Check for chronic mismatch explosion
LATEST_MISMATCH_COUNT=$(grep "CHRONIC_MISMATCH.*detected [0-9]\+ times" $LOG_FILE | tail -1 | grep -o "detected [0-9]\+ times" | grep -o "[0-9]\+")
if [ -n "$LATEST_MISMATCH_COUNT" ] && [ $LATEST_MISMATCH_COUNT -gt 100 ]; then
    echo "$(date): ALERT - Chronic mismatch count: $LATEST_MISMATCH_COUNT" >> $ALERT_FILE
fi

# Check for RPC timeout crashes
RPC_TIMEOUTS=$(grep "timed out after 30000ms" logs/bot-0-console.log | tail -10 | wc -l)
if [ $RPC_TIMEOUTS -gt 0 ]; then
    echo "$(date): CRITICAL - RPC timeout crashes detected: $RPC_TIMEOUTS" >> $ALERT_FILE
fi
```

### Dashboard Metrics

#### Key Metrics for Real-Time Dashboard
1. **Success Rate Gauge** - Current success rate (0-100%)
2. **Average Response Time** - Current average transition time  
3. **Active Connections** - Number of healthy pre-established connections
4. **Error Rate** - Errors per minute (chronic mismatches, timeouts)
5. **Uptime** - Time since last critical failure

#### Prometheus Metrics Export
```csharp
// Example metrics that could be exported to Prometheus
public class ZoneTransitionMetrics
{
    private static readonly Counter TransitionAttempts = Metrics
        .CreateCounter("zone_transition_attempts_total", "Total zone transition attempts");
        
    private static readonly Counter TransitionSuccesses = Metrics
        .CreateCounter("zone_transition_successes_total", "Successful zone transitions");
        
    private static readonly Histogram TransitionDuration = Metrics
        .CreateHistogram("zone_transition_duration_seconds", "Zone transition duration");
        
    private static readonly Gauge ChronicMismatchCount = Metrics
        .CreateGauge("chronic_mismatch_count", "Current chronic mismatch count");
}
```

This performance analysis framework provides comprehensive monitoring and optimization capabilities for zone transition performance in the Granville RPC Shooter system.