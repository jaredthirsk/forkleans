# Zone Transition Configuration Reference

This document provides a complete reference of all configuration values, thresholds, and timing parameters that affect zone transition behavior.

## 🎛️ Zone Transition Debouncer Configuration

**File**: `Shooter.Client.Common/ZoneTransitionDebouncer.cs`

### Core Timing Parameters
```csharp
private const int MIN_TIME_BETWEEN_TRANSITIONS_MS = 200;  // Minimum 200ms between transitions
private const int DEBOUNCE_DELAY_MS = 150;               // Wait 150ms to confirm zone change  
private const int MAX_RAPID_TRANSITIONS = 8;             // Max transitions before cooldown
private const int COOLDOWN_PERIOD_MS = 1000;             // 1 second cooldown after rapid transitions
private const float ZONE_HYSTERESIS_DISTANCE = 2f;       // Must move 2 units into new zone
```

### Parameter Analysis

#### `MIN_TIME_BETWEEN_TRANSITIONS_MS` (200ms)
- **Purpose**: Prevents rapid-fire transitions
- **History**: Reduced from 500ms to improve responsiveness  
- **Impact**: 
  - Too low: Risk of oscillation at boundaries
  - Too high: Sluggish response to legitimate zone changes
- **Tested Range**: 100-500ms (200ms optimal)

#### `DEBOUNCE_DELAY_MS` (150ms)  
- **Purpose**: Confirms zone change is stable before committing
- **History**: Reduced from 300ms for better responsiveness
- **Impact**:
  - Too low: False triggers on temporary boundary crossings
  - Too high: Delayed response to legitimate moves
- **Tested Range**: 50-300ms (150ms optimal)

#### `ZONE_HYSTERESIS_DISTANCE` (2f units)
- **Purpose**: Prevents oscillation by requiring minimum distance into zone
- **History**: Reduced from 5f units (was preventing legitimate transitions)
- **Impact**:
  - Too low: Boundary oscillation issues return
  - Too high: Players get stuck at zone boundaries
- **Critical Range**: 1-3f units (2f optimal)
- **Testing Notes**: Values >3f caused stuck players, <1.5f caused oscillation

---

## 🏥 Health Monitor Configuration

**File**: `Shooter.Client.Common/ZoneTransitionHealthMonitor.cs`

### Anomaly Detection Thresholds
```csharp
private const int MAX_MISMATCH_DURATION_MS = 5000;        // Prolonged mismatch threshold (5s)
private const int MAX_TRANSITION_DURATION_MS = 10000;     // Stuck transition threshold (10s)  
private const int STALE_WORLD_STATE_THRESHOLD_MS = 3000;  // Stale world state (3s)
private const int MAX_CONSECUTIVE_MISMATCHES = 10;        // Chronic mismatch threshold
private const int MAX_TRANSITION_ATTEMPTS = 5;           // Excessive transitions
private const float POSITION_JUMP_THRESHOLD = 100f;       // Position jump detection
```

### Threshold Analysis

#### `MAX_MISMATCH_DURATION_MS` (5000ms)
- **Purpose**: Detects when player is in wrong zone too long
- **Baseline**: Normal transitions complete in 0.5-2 seconds  
- **Trigger Level**: 5 seconds indicates problem
- **Action**: Logs PROLONGED_MISMATCH warning

#### `MAX_CONSECUTIVE_MISMATCHES` (10)
- **Purpose**: Detects systematic zone sync issues
- **With Debouncing**: Takes 10+ seconds to reach (1 increment/second)
- **Without Debouncing**: Would reach in 1 second (10 increments/second)
- **Critical**: This threshold assumes debouncing is working

#### `POSITION_JUMP_THRESHOLD` (100f)
- **Purpose**: Detects teleportation/respawn events
- **Context**: Normal movement is <10 units between updates
- **Large Jumps**: Usually zone transitions, respawns, or sync corrections
- **False Positives**: Rare with 100f threshold

---

## ⏱️ RPC and Connection Timeouts

**File**: `Shooter.Client.Common/GranvilleRpcGameClientService.cs`

### Input Handling Timeouts
```csharp
// Player input timeout (fire-and-forget pattern)
TimeSpan.FromSeconds(5)  // SendPlayerInputEx background timeout

// RPC handshake timeout  
private const int RPC_HANDSHAKE_TIMEOUT_SECONDS = 10;

// Grain acquisition timeout
private const int GRAIN_ACQUISITION_TIMEOUT_SECONDS = 15;
```

### Connection Management
```csharp
// Pre-established connection staleness
private const int STALE_CONNECTION_THRESHOLD_MS = 30000;  // 30 seconds

// Connection retry attempts
private const int MAX_RECONNECT_ATTEMPTS = 5;

// Forced transition timeout
private const double FORCE_TRANSITION_AFTER_SECONDS = 5.0;
```

### Timeout Rationale

#### 5-Second Player Input Timeout
- **Context**: Called 10x per second during active gameplay
- **Requirement**: Must not block bot loop
- **Solution**: Fire-and-forget with background timeout
- **Alternative**: Blocking would cause 30-second freezes on network issues

#### 10-Second RPC Handshake Timeout  
- **Context**: Establishing new connections to ActionServers
- **Tolerance**: Network latency, server startup time
- **Failure Mode**: Connection attempt abandoned, retry triggered

#### 5-Second Forced Transition
- **Context**: Player detected in wrong zone but transition not completing
- **Purpose**: Recovery mechanism for stuck transitions
- **Trade-off**: May interrupt valid slow transitions

---

## 🔧 Bot-Specific Configuration

**File**: `Shooter.Bot/Services/BotService.cs`

### Bot Behavior Parameters
```csharp
// Bot reconnection attempts
private const int MAX_RECONNECT_ATTEMPTS = 5;

// Missing entity tolerance (before respawn attempt)
private const int CONSECUTIVE_MISSING_ENTITY_THRESHOLD = 5;  // ~5 seconds at 1Hz

// Bot status logging frequency  
private const int STATUS_LOG_FREQUENCY = 50;  // Every 5 seconds at 100ms loop

// Position jump detection for bots
private readonly float _positionJumpThreshold = 100f;
```

### Bot Loop Timing
```csharp
// Main bot loop delay
await Task.Delay(100, stoppingToken);  // 10 Hz update rate

// Reconnection delay
await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);  // 5s between reconnect attempts
```

---

## 📊 Performance and Monitoring Configuration

### Health Report Frequency
```csharp
// Health reports generated every 30 seconds
private readonly Timer _healthReportTimer = new(TimeSpan.FromSeconds(30));
```

### Log Level Guidelines
```csharp
// Recommended log levels by component
LogLevel.Debug:   Zone change detection, connection state changes
LogLevel.Info:    Successful transitions, health reports  
LogLevel.Warning: Timeouts, retries, debouncer blocks
LogLevel.Error:   Chronic mismatches, failed transitions
```

---

## 🎯 Tuning Guidelines

### When to Adjust Zone Hysteresis

**Increase ZONE_HYSTERESIS_DISTANCE (2f → 3f) if**:
- Seeing boundary oscillation in logs
- Players ping-ponging between adjacent zones
- ZONE_DEBOUNCE blocking legitimate transitions

**Decrease ZONE_HYSTERESIS_DISTANCE (2f → 1.5f) if**:
- Players getting stuck at zone boundaries  
- Legitimate transitions being blocked
- Debouncer shows large distance requirements not being met

**⚠️ Warning**: Values outside 1.5f-3f range likely to cause issues

### When to Adjust Debounce Timing  

**Increase DEBOUNCE_DELAY_MS (150ms → 250ms) if**:
- False zone changes due to temporary position fluctuations
- Network lag causing position updates to arrive out of order

**Decrease DEBOUNCE_DELAY_MS (150ms → 100ms) if**:  
- Transitions feel sluggish to users
- Legitimate zone changes take too long to trigger

### When to Adjust Health Monitor Thresholds

**Increase MAX_CONSECUTIVE_MISMATCHES (10 → 20) if**:
- Getting false alarms during normal operation
- Debouncing is working but threshold too sensitive

**Decrease MAX_CONSECUTIVE_MISMATCHES (10 → 5) if**:
- Want earlier warning of sync issues
- System is very stable and higher sensitivity desired

---

## 🧪 Testing Configuration Changes

### Safe Testing Process
1. **Backup current values** in comments
2. **Change one parameter at a time**  
3. **Test for minimum 30 minutes** (allow multiple health report cycles)
4. **Monitor key metrics**:
   - Chronic mismatch count trends
   - Transition success rate from health reports  
   - Average transition timing
   - ZONE_DEBOUNCE blocking frequency

### Validation Commands
```bash
# Monitor mismatch growth rate
grep "CHRONIC_MISMATCH" logs/bot-0.log | tail -20

# Check transition success trends
grep "Success Rate:" logs/bot-0.log | tail -10

# Verify debouncer effectiveness  
grep "ZONE_DEBOUNCE.*BLOCKED" logs/bot-0.log | tail -20

# Monitor transition timing
grep "Successfully connected to zone.*in.*s" logs/bot-0.log | tail -20
```

### Rollback Criteria
Revert changes immediately if any of these occur:
- Chronic mismatch count exceeds 100 consecutive
- Success rate drops below 70% in health reports
- RPC timeout crashes return
- Transition timing degrades significantly (>5 seconds average)

---

## 📝 Change Log Template

When modifying configuration values, document changes:

```markdown
## Configuration Change - [Date]

**Parameter**: ZONE_HYSTERESIS_DISTANCE  
**Old Value**: 2f  
**New Value**: 2.5f  
**Reason**: Players getting stuck at boundaries during high-latency conditions  
**Testing Duration**: 2 hours  
**Results**: Boundary sticking reduced, no increase in oscillation  
**Metrics**:
- Success Rate: 85% → 92%  
- Average Transition Time: 0.8s → 0.6s
- Chronic Mismatches: Stable at <20 consecutive
**Decision**: Keep new value
```

This format helps track what changes were made, why, and their effectiveness.