# Shooter Testing and Troubleshooting Workflow

## Overview
This document provides comprehensive instructions for testing the Shooter sample, monitoring for issues, and implementing fixes in an iterative development loop.

## Available Testing Scripts

### 1. Interactive Development Menu (`run-dev.sh`)
**Purpose**: Provides an interactive menu-driven interface for common development tasks.

```bash
./scripts/run-dev.sh
```

**Menu Options**:
- **Quick Actions (1-5)**:
  - Start with/without build
  - Graceful or force stop
  - Restart components
- **Testing (6-8)**:
  - Automated robustness testing
  - Test loop with dev manager
  - Real-time log monitoring
- **Diagnostics (9-12)**:
  - Show running processes
  - Check for errors in logs
  - Clear logs
  - Trigger garbage collection
- **Advanced (13-15)**:
  - Start with bot
  - Minimal setup (1 ActionServer)
  - Tail all logs simultaneously

### 2. Robustness Testing (`test-robustness.sh`)
**Purpose**: Runs automated test iterations to detect stability issues.

```bash
./scripts/test-robustness.sh
```

**What it does**:
- Runs 5 iterations by default
- Each iteration:
  - Starts all components
  - Monitors for 60 seconds
  - Checks for timeouts, exceptions, zone mismatches
  - Collects diagnostics on failure
  - Stops all components

**Output**:
- Success rate percentage
- Detailed issue log in `logs/robustness-test.log`
- List of all detected issues by iteration

### 3. PowerShell Development Manager (`dev-manager.ps1`)
**Purpose**: Advanced testing and management with PowerShell.

```bash
pwsh scripts/dev-manager.ps1 -Action <action>
```

**Actions**:
- `start` - Start all components
- `stop` - Stop all components  
- `graceful-stop` - Graceful shutdown via HTTP endpoint
- `restart` - Stop and restart
- `monitor` - Monitor logs for issues
- `test-loop` - Run automated test iterations

**Parameters**:
- `-SkipBuild` - Skip building before start
- `-WithBot` - Include bot in startup
- `-MonitorSeconds` - Duration to monitor (default: 60)

## Troubleshooting Loop Workflow

### Step 1: Initial Setup
```bash
# Clear old logs for fresh start
rm -f logs/*.log

# Ensure all processes are stopped
./scripts/kill-shooter-processes.sh
```

### Step 2: Start Testing Loop
```bash
# Option A: Interactive menu
./scripts/run-dev.sh
# Choose option 6 for robustness test

# Option B: Direct robustness test
./scripts/test-robustness.sh

# Option C: PowerShell test loop
pwsh scripts/dev-manager.ps1 -Action test-loop
```

### Step 3: Monitor for Issues

#### Common Issues to Watch For:

**1. Timeout Errors**
```bash
grep -n "timed out after" logs/*.log | tail -20
```
- Look for: "RPC request XXX timed out after 30000ms"
- Indicates: Connection or zone transition issues

**2. Zone Mismatches**
```bash
grep "ZONE_MISMATCH" logs/client*.log | tail -10
```
- Look for: "Player at position (X,Y) is in zone (A,B) but server thinks they should be in zone (C,D)"
- Indicates: Zone boundary calculation issues

**3. Stuck Transitions**
```bash
grep -E "Stuck in transitioning|ZONE_TRANSITION.*stuck" logs/*.log
```
- Look for: "Stuck in transitioning state for X seconds"
- Indicates: Zone transition logic failure

**4. Connection Loss**
```bash
grep -E "connection lost|disconnected|reconnect" logs/*.log -i | tail -20
```
- Look for: "Marking connection as lost", "Attempting reconnection"
- Indicates: Network or RPC issues

**5. Freeze Detection**
```bash
grep "World state polling.*stopped" logs/client*.log
```
- Look for: "World state polling appears to have stopped. Last poll was X seconds ago"
- Indicates: Client freeze/hang

### Step 4: Analyze Specific Time Periods

When you notice a freeze or issue at a specific time:

```bash
# Example: Client froze at 9:41:25
TIME="09:41:2[0-9]"
grep -E "$TIME" logs/client.log | head -50

# Check what happened in next 30 seconds
TIME_RANGE="09:41:[2-5][0-9]"
grep -E "$TIME_RANGE" logs/client.log > freeze_analysis.txt
```

### Step 5: Stop and Fix

#### Graceful Stop (Preferred)
```bash
# Via HTTP endpoint
curl -X POST http://localhost:7071/api/admin/shutdown?delaySeconds=2

# Or via script
pwsh scripts/dev-manager.ps1 -Action graceful-stop
```

#### Force Stop (If Graceful Fails)
```bash
./scripts/kill-shooter-processes.sh
```

### Step 6: Apply Fix
1. Edit the problematic file(s)
2. Build the solution:
   ```bash
   dotnet build GranvilleSamples.sln --configuration Debug
   ```
3. If build fails, fix compilation errors and retry

### Step 7: Verify Fix

#### Quick Test
```bash
# Start minimal setup for quick verification
./scripts/run-dev.sh
# Choose option 14 (minimal setup)

# Monitor specific issue
tail -f logs/*.log | grep -E "ERROR|timeout|ZONE"
```

#### Full Test
```bash
# Run full robustness test
./scripts/test-robustness.sh

# Check success rate
grep "Success rate:" logs/robustness-test.log
```

## Automated Troubleshooting Loop

### Continuous Testing Script
Create this script as `scripts/continuous-test.sh`:

```bash
#!/bin/bash
ITERATIONS=0
FAILURES=0

while true; do
    ITERATIONS=$((ITERATIONS + 1))
    echo "=== Iteration $ITERATIONS ==="
    
    # Run test
    ./scripts/test-robustness.sh
    
    if [ $? -ne 0 ]; then
        FAILURES=$((FAILURES + 1))
        echo "Test failed! Failures: $FAILURES/$ITERATIONS"
        
        # Analyze failure
        echo "Recent errors:"
        grep -E "ERROR|Exception|timeout" logs/*.log | tail -10
        
        # Wait for manual fix
        read -p "Fix applied? Press Enter to continue..."
    else
        echo "Test passed! Success rate: $((ITERATIONS - FAILURES))/$ITERATIONS"
    fi
    
    # Optional delay between iterations
    sleep 5
done
```

## Log Analysis Commands

### Quick Health Check
```bash
# Show error counts by component
for log in logs/*.log; do
    echo "$(basename $log): $(grep -c ERROR $log) errors, $(grep -c WARNING $log) warnings"
done
```

### Timeline Analysis
```bash
# Create timeline of events
grep -h "09:41:" logs/*.log | sort | head -100 > timeline.txt
```

### Performance Metrics
```bash
# Check zone transition times
grep "ZONE_TRANSITION.*took" logs/client*.log | \
    sed 's/.*took \([0-9.]*\)ms.*/\1/' | \
    awk '{sum+=$1; count++} END {print "Avg transition time:", sum/count, "ms"}'
```

## Common Fixes

### Issue: Rapid Zone Transitions
**Symptom**: Player bouncing between zones rapidly
**Fix**: Increase hysteresis distance in `ZoneTransitionDebouncer.cs`

### Issue: Timer Freeze
**Symptom**: World state polling stops for 10+ seconds
**Fix**: Use `RobustTimerManager` to protect timers during transitions

### Issue: Connection Timeout
**Symptom**: RPC requests timing out after 30 seconds
**Fix**: Check `ConnectionResilienceManager` backoff settings

### Issue: Zone Mismatch
**Symptom**: Client and server disagree on player zone
**Fix**: Verify `GridSquare.FromPosition()` calculation consistency

## Best Practices

1. **Always Start Fresh**: Clear logs before testing sessions
2. **Use Graceful Shutdown**: Prevents corrupt state
3. **Monitor Memory**: Use GC trigger if memory grows
4. **Test Incrementally**: Start with minimal setup, then scale up
5. **Document Issues**: Keep notes on error patterns and fixes
6. **Version Control**: Commit working fixes before trying new ones

## Troubleshooting Tips

1. **If nothing works**: 
   ```bash
   # Nuclear option - clean everything
   ./scripts/kill-shooter-processes.sh
   rm -rf logs/*.log
   dotnet clean
   dotnet build
   ```

2. **If logs are too verbose**:
   - Edit `appsettings.json` files
   - Set problematic namespaces to "Warning" level

3. **If you can't find the issue**:
   - Enable trace logging temporarily
   - Use `multitail` for simultaneous log viewing
   - Check ActionServer logs, not just client

4. **If tests are flaky**:
   - Increase timeouts in test scripts
   - Add more stabilization time between operations
   - Check system resource usage

## Success Criteria

A successful test run should have:
- ✅ No timeout errors in 5 minutes
- ✅ No stuck zone transitions
- ✅ Smooth player movement across zones
- ✅ Consistent 30+ FPS polling rate
- ✅ Memory usage stable (no leaks)
- ✅ All components stay connected
- ✅ Graceful shutdown works cleanly

## Emergency Recovery

If the system becomes completely unresponsive:

```bash
# Force kill everything
pkill -f "Shooter\."
pkill -f dotnet

# Clean build artifacts
rm -rf **/bin **/obj
rm -rf logs/*.log

# Clear NuGet cache if needed
dotnet nuget locals all --clear

# Full rebuild
dotnet restore
dotnet build --no-incremental

# Start fresh
./scripts/run-dev.sh
```