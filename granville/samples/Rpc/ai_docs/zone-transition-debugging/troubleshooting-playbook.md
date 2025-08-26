# Zone Transition Troubleshooting Playbook

This document provides step-by-step troubleshooting procedures for the most common zone transition issues.

## 🚨 Emergency Response Procedures

### Critical Issue: System Completely Broken

**Symptoms**: Continuous crashes, logs completely unusable, no successful transitions

**Immediate Actions**:
1. **Stop all processes**: `scripts/kill-shooter-processes.sh`
2. **Archive current logs**: `cp logs/bot-0.log logs/emergency-$(date +%Y%m%d_%H%M%S).log`
3. **Check for regression**: Compare with known-good configuration
4. **Restart with clean state**: Wait 30 seconds, then restart AppHost

**Emergency Rollback**:
```bash
# If recent code changes caused the issue
git log --oneline -10  # Check recent commits
git revert HEAD        # Revert last commit if necessary
```

---

## 🔍 Diagnostic Decision Tree

### Step 1: Determine Issue Category

Run this quick diagnostic to categorize the problem:

```bash
# Check if system is fundamentally broken
echo "=== SYSTEM HEALTH CHECK ==="
echo "RPC Crashes in last hour:"
grep "timed out after 30000ms" logs/bot-0-console.log | wc -l

echo "Bot still running:"  
grep "🤖 Bot.*status" logs/bot-0.log | tail -1

echo "Recent transitions:"
grep "Successfully connected to zone" logs/bot-0.log | tail -5 | wc -l
```

**Decision Matrix**:
- **RPC Crashes > 0**: → Go to [RPC Timeout Crisis](#rpc-timeout-crisis) 
- **No bot status updates in 5+ minutes**: → Go to [Bot Loop Frozen](#bot-loop-frozen)
- **No successful transitions**: → Go to [Zone Transition Complete Failure](#zone-transition-complete-failure)
- **Some successful transitions**: → Go to [Partial Zone Transition Issues](#partial-zone-transition-issues)

---

## 🚨 RPC Timeout Crisis

**Symptoms**: `RPC request timed out after 30000ms`, bot freezing, client crashes

### Root Cause Analysis
```bash
# Check if fire-and-forget pattern is broken
grep "Player input timed out after 5 seconds" logs/bot-0.log | tail -10

# Check for blocking operations in bot loop
grep -A 5 -B 5 "Bot.*status" logs/bot-0.log | tail -20
```

### Fix Verification Checklist
1. **Confirm fire-and-forget pattern**:
   ```csharp
   // In SendPlayerInputEx - should be void, not async Task
   public void SendPlayerInputEx(Vector2? moveDirection, Vector2? shootDirection)
   ```

2. **Verify background timeout handling**:
   ```csharp
   // Should use Task.Run for background processing
   _ = Task.Run(async () => {
       await inputTask.WaitAsync(TimeSpan.FromSeconds(5));
   });
   ```

3. **Check bot service call site**:
   ```csharp
   // Should NOT use await
   _gameClient.SendPlayerInputEx(moveDirection, shootDirection);
   ```

### Emergency Fix
If fire-and-forget pattern is broken:
1. **Immediate**: Comment out problematic input sending temporarily
2. **Quick fix**: Revert to last known working version of `SendPlayerInputEx`
3. **Proper fix**: Re-implement fire-and-forget pattern as documented in `fixes-applied.md`

---

## 🤖 Bot Loop Frozen  

**Symptoms**: Bot status updates stop appearing, bot appears unresponsive

### Diagnostic Commands
```bash
# Check if bot process is still running
scripts/show-shooter-processes.sh | grep Bot

# Check last bot activity
grep "🤖 Bot.*status" logs/bot-0.log | tail -5

# Look for exceptions in bot loop
grep -A 10 "Error in bot loop" logs/bot-0.log | tail -20
```

### Common Causes & Fixes

#### Cause 1: Blocking RPC Call
**Evidence**: Last activity shows input being sent, then silence
**Fix**: Verify fire-and-forget pattern in `SendPlayerInputEx`

#### Cause 2: Reconnection Loop  
**Evidence**: Repeated "attempting to reconnect" messages
```bash
grep "attempting to reconnect" logs/bot-0.log | tail -20
```
**Fix**: Check if max reconnect attempts exceeded, restart bot service

#### Cause 3: Exception in Bot Loop
**Evidence**: Exception logged, then no more status updates
**Fix**: Address root cause of exception, add defensive programming

### Recovery Procedure
1. **Restart bot only**: Kill bot process, leave other services running
2. **Clean restart**: If restart doesn't work, restart entire AppHost
3. **Investigate**: Use logs to identify what caused the freeze

---

## 🌍 Zone Transition Complete Failure

**Symptoms**: No successful transitions logged, players stuck in zones permanently

### Diagnostic Commands
```bash
# Check if transitions are being attempted
grep "FORCING transition after" logs/bot-0.log | tail -10

# Check if debouncer is blocking everything
grep "ZONE_DEBOUNCE.*BLOCKED" logs/bot-0.log | tail -20

# Check if zone detection is working
grep "Ship moved to zone" logs/bot-0.log | tail -20
```

### Investigation Tree

#### No Zone Change Detection
**Evidence**: No "Ship moved to zone" messages
**Likely Cause**: Zone calculation logic broken
**Check**: `GridSquare.FromPosition()` method, player position updates

#### Zone Changes Detected but Transitions Not Triggered
**Evidence**: "Ship moved to zone" but no "FORCING transition"
**Likely Cause**: Transition logic broken, debouncer misconfigured
**Check**: `CheckForServerTransition()` calls, debouncer thresholds

#### Transitions Attempted but All Failing
**Evidence**: "FORCING transition" but no "Successfully connected"  
**Likely Cause**: RPC connection failures, server unavailable
**Check**: ActionServer health, network connectivity

### Resolution Steps
1. **Check ActionServer availability**:
   ```bash
   # Verify all ActionServers are running
   scripts/show-shooter-processes.sh | grep ActionServer
   ```

2. **Test RPC connectivity**:
   ```bash
   # Check if pre-established connections are working
   grep "Pre-connection status" logs/bot-0.log | tail -5
   ```

3. **Verify zone calculation**:
   ```bash
   # Check if zone detection makes sense
   grep -E "Ship moved to zone.*at position" logs/bot-0.log | tail -10
   ```

---

## 🔄 Partial Zone Transition Issues

**Symptoms**: Some transitions work, others fail; chronic mismatch counts growing slowly

### Health Assessment
```bash
# Get current health metrics
grep -A 5 "Zone Transition Health Report" logs/bot-0.log | tail -10

# Check success rate trends
grep "Success Rate:" logs/bot-0.log | tail -20

# Monitor transition timing
grep "Successfully connected to zone.*in.*s" logs/bot-0.log | tail -20
```

### Performance Categories

#### Success Rate 70-89%: Moderate Issues
**Possible Causes**:
- Network instability  
- Occasional server overload
- Boundary oscillation at specific locations

**Investigation**:
```bash
# Check for patterns in failures
grep "Failed to connect" logs/bot-0.log | tail -20

# Look for specific zones with problems  
grep "Successfully connected to zone" logs/bot-0.log | grep -o "zone ([0-9],[0-9])" | sort | uniq -c
```

#### Success Rate 50-69%: Significant Problems
**Possible Causes**:
- Pre-established connection staleness
- Server routing problems
- Zone boundary calculation issues

**Investigation**:
```bash
# Check pre-established connection health
grep "Pre-established connection.*failed" logs/bot-0.log | tail -20

# Look for connection refresh activity
grep "Pre-establishing connection to zone" logs/bot-0.log | tail -20
```

#### Success Rate <50%: System Degradation
**Possible Causes**:
- Fundamental configuration problem
- Resource exhaustion  
- Network connectivity issues

**Action**: Escalate to full investigation, consider rollback

---

## 🎯 Specific Issue Patterns

### Pattern: Boundary Oscillation

**Symptoms**: Rapid zone changes between adjacent zones
```bash
# Detect oscillation pattern
grep "Ship moved to zone" logs/bot-0.log | tail -20 | grep -o "zone ([0-9],[0-9])"
```

**Fix Options**:
1. **Increase hysteresis**: Modify `ZONE_HYSTERESIS_DISTANCE` from 2f to 2.5f
2. **Increase debounce time**: Modify `DEBOUNCE_DELAY_MS` from 150ms to 200ms
3. **Check player movement**: Verify bot isn't moving in tight loops near boundaries

### Pattern: Chronic Mismatch Growth

**Symptoms**: Mismatch counts steadily increasing even with debouncing
```bash
# Check mismatch growth rate
grep -E "CHRONIC_MISMATCH.*detected [0-9]+" logs/bot-0.log | tail -30
```

**Diagnosis**:
- **Growth rate ~1 per second**: Debouncing working, but transitions failing
- **Growth rate >5 per second**: Debouncing may be broken
- **Growth rate irregular**: Network instability or resource issues

### Pattern: Stuck Transitions

**Symptoms**: Long delays before forced transitions
```bash  
# Check forced transition timing
grep "FORCING transition after" logs/bot-0.log | tail -20
```

**Analysis**:
- **Times >30s**: Transitions completely stuck, likely connection issues
- **Times 10-30s**: Slow but progressing, may be server overload
- **Times 5-10s**: Normal for degraded conditions

---

## 🛠️ Quick Fixes for Common Issues

### Fix 1: Reset Pre-Established Connections
```bash
# Restart just the bot to refresh connections
scripts/kill-shooter-processes.sh | grep Bot
# Then restart AppHost
```

### Fix 2: Clear Stale State
```bash  
# Clean logs and restart fresh
rm logs/*.log
cd Shooter.AppHost && dotnet run --no-build &
```

### Fix 3: Reduce Hysteresis Temporarily  
```csharp
// In ZoneTransitionDebouncer.cs, temporarily reduce for testing
private const float ZONE_HYSTERESIS_DISTANCE = 1.5f; // Reduced from 2f
```

### Fix 4: Force Zone Alignment
```bash
# If chronic mismatch is severe, restart all services to force realignment
scripts/kill-shooter-processes.sh
sleep 5
cd Shooter.AppHost && dotnet run --no-build &
```

---

## 📊 Monitoring During Troubleshooting

### Real-Time Monitoring Setup
```bash
# Monitor multiple aspects simultaneously
tail -f logs/bot-0.log | grep -E "(CHRONIC_MISMATCH|Successfully connected|FORCING transition)" &
tail -f logs/bot-0-console.log | grep "timed out" &  
tail -f logs/bot-0.log | grep "🤖 Bot.*status" &
```

### Success Criteria for Resolution
Issue is resolved when all of these are true for 30+ minutes:
- ✅ No RPC timeout crashes: `grep "timed out after 30000ms" logs/bot-0-console.log | wc -l` returns 0
- ✅ Bot status updates regular: New status messages every ~5 seconds  
- ✅ Success rate >80%: Latest health report shows good performance
- ✅ Chronic mismatch stable: Count stays below 50 consecutive

### Escalation Criteria
Escalate to deeper investigation if:
- Issue persists after following all relevant procedures
- Success rate remains <50% after fixes
- New error patterns emerge not covered in documentation
- System behavior doesn't match any documented patterns

---

## 🎓 Learning from Incidents

### Post-Incident Review Template
After resolving any significant issue:

1. **Timeline**: When did issue start/end?
2. **Root Cause**: What actually caused the problem?
3. **Detection**: How was the issue discovered?
4. **Resolution**: What steps fixed it?
5. **Prevention**: How to prevent similar issues?
6. **Documentation**: What needs to be updated?

### Documentation Updates
- Add new patterns to this playbook
- Update configuration reference with lessons learned
- Enhance monitoring commands based on what was useful
- Update debugging techniques with new effective methods