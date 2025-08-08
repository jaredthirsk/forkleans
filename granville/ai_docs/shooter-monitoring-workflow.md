# Shooter Game Monitoring and Fix Workflow

## Overview
This document describes the continuous development loop for monitoring and fixing issues in the Shooter game, particularly focusing on bot hangs and zone transition problems.

## The Development Loop

### 1. START - Run the Application
```bash
# From the AppHost directory
cd /mnt/c/forks/orleans/granville/samples/Rpc/Shooter.AppHost
./rl.sh
```

### 2. MONITOR - Real-Time Log Monitoring

#### Key Log Files to Watch
```bash
# In separate terminals, tail these logs:
tail -f ../logs/bot*.log          # Bot logs (watch for hangs/errors)
tail -f ../logs/shooter-silo.log  # Silo logs (zone transitions)
tail -f ../logs/action-server.log # Action server logs
```

#### Critical Patterns to Watch For

**Bot Hangs:**
- No activity for >5 seconds from a bot
- "Failed to connect" messages
- "Connection timeout" errors
- SSL certificate errors
- Repeated "Attempting to reconnect" without success

**Zone Transition Issues:**
- "Zone transition" followed by long gaps
- "Timer disposed" errors
- "Connection lost during transition"
- Rapid zone changes (>5 in 10 seconds)
- Client freeze indicators (no updates for >2 seconds)

**Memory/Performance:**
- Growing memory usage in logs
- Increasing response times
- Thread pool starvation warnings

### 3. DETECT - Issue Detection Checklist

#### Bot Health Check
- [ ] All bots reporting position updates regularly (every 1-2 seconds)
- [ ] No SSL/TLS errors in bot logs
- [ ] Bot movement patterns look natural (not stuck at boundaries)
- [ ] Connection state stable (no rapid connect/disconnect)

#### Zone Transition Health
- [ ] Transitions complete within 2 seconds
- [ ] No rapid back-and-forth transitions
- [ ] Timers survive transitions (no disposal errors)
- [ ] Client maintains connection through transition

#### System Health
- [ ] CPU usage reasonable (<80%)
- [ ] Memory stable (no continuous growth)
- [ ] Network latency acceptable (<100ms for local)
- [ ] No unhandled exceptions in any component

### 4. SHUTDOWN - Graceful Shutdown Procedure

```bash
# Step 1: Stop the AppHost gracefully
# In the terminal running ./rl.sh, press Ctrl+C
# Wait for "Application stopped" message

# Step 2: Verify all processes stopped
ps aux | grep -E "Shooter" | grep -v grep

# Step 3: If processes remain, kill them
./scripts/kill-shooter-processes.sh

# Step 4: Save relevant logs
cp ../logs/*.log ../logs/archive/$(date +%Y%m%d_%H%M%S)/
```

### 5. DIAGNOSE - Root Cause Analysis

#### For Bot Hangs
1. Check last successful operation in bot log
2. Look for connection state at hang time
3. Verify server was responsive (check silo logs)
4. Check for zone transition correlation
5. Review network transport logs

#### For Zone Transitions
1. Identify the zones involved
2. Check player position at transition
3. Look for rapid transitions (bounce pattern)
4. Verify timer state before/after
5. Check connection stability during transition

### 6. FIX - Apply Fixes

#### Common Fixes

**Bot Connection Issues:**
- Update SSL settings in Program.cs
- Adjust reconnection delays
- Implement connection pooling
- Add retry logic with backoff

**Zone Transition Problems:**
- Adjust hysteresis distance
- Increase debounce delay
- Implement transition queuing
- Protect timers during transition

**Performance Issues:**
- Reduce logging verbosity
- Implement caching
- Optimize hot paths
- Add connection pooling

### 7. VERIFY - Test the Fix

```bash
# Quick verification (5 minutes)
./rl.sh
# Watch logs for 5 minutes
# Check that the specific issue is resolved

# Extended verification (30 minutes)
# Run with multiple bots
# Monitor all metrics
# Stress test the fixed component
```

### 8. DOCUMENT - Update Documentation

- Update this workflow if new patterns discovered
- Document the fix in relevant docs/
- Update ZONE_TRANSITION_HANGING_ISSUE.md if related
- Add to troubleshooting guide

## Automated Monitoring Script

```bash
#!/bin/bash
# monitor-shooter.sh

# Colors for output
RED='\033[0;31m'
YELLOW='\033[1;33m'
GREEN='\033[0;32m'
NC='\033[0m' # No Color

echo "Starting Shooter monitoring..."

# Start the application
./rl.sh &
APP_PID=$!

# Wait for startup
sleep 10

# Monitor logs
while kill -0 $APP_PID 2>/dev/null; do
    # Check for bot hangs (no updates in 10 seconds)
    LAST_BOT_UPDATE=$(tail -n 100 ../logs/bot*.log | grep "Position update" | tail -1 | cut -d' ' -f1-2)
    if [ -n "$LAST_BOT_UPDATE" ]; then
        LAST_TIMESTAMP=$(date -d "$LAST_BOT_UPDATE" +%s 2>/dev/null)
        CURRENT_TIMESTAMP=$(date +%s)
        DIFF=$((CURRENT_TIMESTAMP - LAST_TIMESTAMP))
        
        if [ $DIFF -gt 10 ]; then
            echo -e "${RED}WARNING: Bot hasn't updated in $DIFF seconds${NC}"
        fi
    fi
    
    # Check for zone transition issues
    RECENT_TRANSITIONS=$(tail -n 100 ../logs/shooter-silo.log | grep -c "Zone transition")
    if [ $RECENT_TRANSITIONS -gt 5 ]; then
        echo -e "${YELLOW}NOTICE: High zone transition rate: $RECENT_TRANSITIONS in last 100 lines${NC}"
    fi
    
    # Check for errors
    ERRORS=$(tail -n 50 ../logs/*.log | grep -iE "error|exception|failed" | wc -l)
    if [ $ERRORS -gt 0 ]; then
        echo -e "${RED}ERRORS DETECTED: $ERRORS errors in recent logs${NC}"
    fi
    
    sleep 5
done

echo "Application stopped. Monitoring ended."
```

## Quick Reference Commands

```bash
# Start monitoring
./monitor-shooter.sh

# Check specific bot
tail -f ../logs/bot-TestBot*.log

# Find zone transition issues
grep -A5 -B5 "Zone transition" ../logs/*.log

# Check for hangs
grep -E "(hang|freeze|stuck|timeout)" ../logs/*.log

# Performance metrics
grep -E "(memory|cpu|latency)" ../logs/*.log | tail -20

# Kill all processes
pkill -f Shooter
```

## Issue Priority Matrix

| Issue Type | Severity | Immediate Action | Long-term Fix |
|------------|----------|------------------|---------------|
| Bot Complete Hang | CRITICAL | Restart bot, check logs | Fix connection resilience |
| Zone Transition Freeze | HIGH | Monitor duration, restart if >30s | Implement debouncing |
| Rapid Zone Bouncing | MEDIUM | Log and monitor | Adjust hysteresis |
| High CPU Usage | MEDIUM | Check for loops | Profile and optimize |
| Memory Leak | HIGH | Monitor growth rate | Find and fix leak |
| Network Timeouts | HIGH | Check connectivity | Implement retry logic |

## Current Focus Areas

### Priority 1: Bot Stability
- SSL certificate issues (fixed by using http://)
- Connection resilience during network hiccups
- Graceful handling of server restarts

### Priority 2: Zone Transitions
- Prevent rapid transitions (debouncing implemented)
- Protect timers during transitions (pause/resume implemented)
- Ensure state consistency across transitions

### Priority 3: Performance
- Reduce verbose logging to trace level
- Optimize hot paths in update loops
- Implement connection pooling for HTTP clients

## Workflow Automation Goals

1. **Automated Detection**: Build scripts to detect issues automatically
2. **Self-Healing**: Implement automatic recovery for known issues
3. **Metrics Collection**: Gather performance metrics for analysis
4. **Alert System**: Set up alerts for critical issues
5. **Test Automation**: Create stress tests for problem areas