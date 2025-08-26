# Code Patterns and Anti-Patterns for Zone Transitions

This document identifies important code patterns discovered during debugging and anti-patterns to avoid.

## ✅ Successful Patterns to Follow

### 1. Fire-and-Forget with Background Error Handling

**Pattern**: For operations that shouldn't block the caller but need error monitoring.

**Good Example** (from `SendPlayerInputEx`):
```csharp
public void SendPlayerInputEx(Vector2? moveDirection, Vector2? shootDirection)
{
    // Immediate validation - fail fast
    if (_gameGrain == null || !IsConnected || string.IsNullOrEmpty(PlayerId))
    {
        return;
    }
    
    // Start operation but don't wait for it
    var inputTask = _gameGrain.UpdatePlayerInputEx(PlayerId, moveDirection, shootDirection);
    
    // Handle completion/errors in background
    _ = Task.Run(async () =>
    {
        try
        {
            await inputTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            // Handle timeout gracefully
            IsConnected = false;
            ServerChanged?.Invoke("Connection lost");
        }
        // ... other exception handling
    });
}
```

**Why This Works**:
- Caller is never blocked by network operations
- Timeouts are handled gracefully without crashing
- Connection state is updated appropriately
- Task disposal is handled properly

**When to Use**: High-frequency operations (like player input) that can't afford to block.

---

### 2. Debounced Counter Pattern  

**Pattern**: Prevent counter spam while still detecting legitimate issues.

**Good Example** (from `ZoneTransitionHealthMonitor`):
```csharp
// Track when we last incremented to prevent spam
private DateTime _lastConsecutiveMismatchIncrement = DateTime.MinValue;

private void CheckForAnomalies()
{
    if (isMismatched)
    {
        // Only increment once per time period
        var timeSinceLastIncrement = _lastConsecutiveMismatchIncrement == DateTime.MinValue ? 
            TimeSpan.MaxValue : 
            (now - _lastConsecutiveMismatchIncrement);
        
        if (timeSinceLastIncrement.TotalMilliseconds >= 1000) // 1 second debounce
        {
            _consecutiveMismatchCount++;
            _lastConsecutiveMismatchIncrement = now;
        }
    }
    else
    {
        // Reset both counter and timing when condition clears
        _consecutiveMismatchCount = 0;
        _lastConsecutiveMismatchIncrement = DateTime.MinValue;
    }
}
```

**Why This Works**:
- Prevents explosive counter growth from high-frequency checks
- Still detects legitimate sustained issues
- Resets cleanly when condition resolves

**When to Use**: Any counter that's checked at high frequency but should only increment for sustained conditions.

---

### 3. Structured Logging with Context

**Pattern**: Include all relevant context in single log message for easy analysis.

**Good Example**:
```csharp
_logger.LogInformation("[ZONE_TRANSITION] Successfully connected to zone ({X},{Y}) in {Duration:F2}s - Test GetWorldState got {EntityCount} entities",
    zone.X, zone.Y, duration, entityCount);
```

**Key Elements**:
- **Prefix tag** (`[ZONE_TRANSITION]`) for filtering
- **Structured data** (zone coordinates, timing, entity count)
- **Success/failure indication** clearly stated
- **Performance metrics** (duration) included

**Why This Works**:
- Single grep command can extract all transition information
- Performance analysis is possible from logs alone  
- Context helps diagnose issues without additional investigation

---

### 4. State Validation with Early Return

**Pattern**: Validate all required state before attempting operations.

**Good Example**:
```csharp
public async Task PerformZoneTransition(GridSquare targetZone)
{
    // Validate all prerequisites upfront
    if (_isTransitioning)
    {
        _logger.LogDebug("Zone transition already in progress, skipping");
        return;
    }
    
    if (_currentZone != null && _currentZone.Equals(targetZone))  
    {
        _logger.LogDebug("Already in target zone {Zone}", targetZone);
        return;
    }
    
    if (_gameGrain == null || string.IsNullOrEmpty(PlayerId))
    {
        _logger.LogWarning("Cannot transition - missing game grain or player ID");
        return;
    }
    
    // Now proceed with actual transition logic
    _isTransitioning = true;
    try
    {
        // ... transition logic
    }
    finally
    {
        _isTransitioning = false;
    }
}
```

**Why This Works**:
- Prevents duplicate/unnecessary work
- Fails fast with clear reasons
- Reduces nesting and makes happy path obvious
- Ensures state consistency

---

### 5. Retry Logic with Context

**Pattern**: Always retry operations that can fail due to transient issues.

**Good Example**:
```csharp
else
{
    _logger.LogDebug("[CLIENT_ZONE_CHANGE] Still in zone mismatch to ({X},{Y}) - last change was {Seconds}s ago", 
        playerZone.X, playerZone.Y, timeSinceLastChange);
    
    // CRITICAL: Still call CheckForServerTransition in case previous transition failed
    _ = CheckForServerTransition();
}
```

**Why This Works**:
- Doesn't give up after first failure
- Provides context about retry attempts
- Distinguishes between new issues and ongoing retries

**When to Use**: Network operations, resource acquisition, any operation with transient failure modes.

---

## ❌ Anti-Patterns to Avoid

### 1. Blocking Operations in High-Frequency Loops

**Anti-Pattern**: Using `await` or blocking calls in methods called frequently.

**Bad Example**:
```csharp
// BAD: This blocks the bot loop for up to 30 seconds on timeout
public async Task SendPlayerInput(Vector2 direction)
{
    await _gameGrain.UpdatePlayerInput(PlayerId, direction); // Can timeout!
}
```

**Why This Fails**:
- Bot loop stops responding during RPC timeouts
- Cascades into connection issues and state problems
- User experience degrades severely

**Fix**: Use fire-and-forget pattern shown above.

---

### 2. Counter Increment Without Debouncing

**Anti-Pattern**: Incrementing counters on every check without rate limiting.

**Bad Example**:
```csharp  
// BAD: This runs 10x per second, counter grows exponentially
private void CheckForMismatch()
{
    if (playerZone != serverZone)
    {
        _mismatchCount++; // Increments 10x/second!
        if (_mismatchCount > 10)
        {
            _logger.LogError("Chronic mismatch: {Count}", _mismatchCount);
        }
    }
}
```

**Why This Fails**:
- Counter grows faster than actual problem duration
- Logs become unusable due to spam
- Real issues are hidden by noise

**Fix**: Add time-based debouncing as shown in successful patterns.

---

### 3. Silent Failure in Retry Logic

**Anti-Pattern**: Ignoring failed operations without retry mechanism.

**Bad Example**:
```csharp
// BAD: If zone change detected but this path is skipped, no retry happens
if (_lastDetectedZone?.Equals(newZone) == true)
{
    // Ignore "duplicate" zone change - but what if first transition failed?
    return; // No retry mechanism!
}
```

**Why This Fails**:
- Failed transitions are never retried
- Players get permanently stuck in wrong zones
- System can't recover from transient failures

**Fix**: Always provide retry mechanism for failed operations.

---

### 4. Deep Nesting Without Early Returns  

**Anti-Pattern**: Nesting conditions instead of using guard clauses.

**Bad Example**:
```csharp
// BAD: Logic buried deep, else cases easy to miss
public void HandleZoneChange(GridSquare newZone)
{
    if (newZone != null)
    {
        if (_currentZone != null)
        {
            if (!_currentZone.Equals(newZone))
            {
                if (!_isTransitioning)
                {
                    // Actual logic buried 4 levels deep
                    StartZoneTransition(newZone);
                }
                // Easy to miss: what if _isTransitioning is true?
            }
            // Easy to miss: what if zones are equal?
        }
        // Easy to miss: what if _currentZone is null?
    }
    // Easy to miss: what if newZone is null?
}
```

**Why This Fails**:
- Hard to see all code paths  
- Easy to miss edge cases in else conditions
- Difficult to test all branches

**Fix**: Use early returns and guard clauses.

---

### 5. Logging Without Context

**Anti-Pattern**: Generic error messages without diagnostic information.

**Bad Example**:
```csharp
// BAD: No context, can't diagnose from logs alone
_logger.LogError("Zone transition failed");
_logger.LogWarning("Player input timeout");  
_logger.LogError("Connection lost");
```

**Why This Fails**:
- Can't diagnose issues from logs
- No performance data available
- Difficult to correlate related events

**Fix**: Always include relevant context (timing, state, parameters).

---

## 🎯 Specific Zone Transition Patterns

### Zone Boundary Handling

**Good Pattern**:
```csharp
private bool IsWellInsideZone(Vector2 position, GridSquare zone)
{
    var (min, max) = zone.GetBounds();
    
    // Calculate distance from each border
    var distFromLeft = position.X - min.X;
    var distFromRight = max.X - position.X;
    var distFromBottom = position.Y - min.Y;
    var distFromTop = max.Y - position.Y;
    
    // Require minimum distance from ALL borders (hysteresis)
    return distFromLeft >= ZONE_HYSTERESIS_DISTANCE &&
           distFromRight >= ZONE_HYSTERESIS_DISTANCE &&
           distFromBottom >= ZONE_HYSTERESIS_DISTANCE &&
           distFromTop >= ZONE_HYSTERESIS_DISTANCE;
}
```

**Why This Works**: Prevents oscillation at boundaries by requiring player to be well inside zone.

### Connection Cleanup Pattern  

**Good Pattern**:
```csharp
public async Task DisconnectFromCurrentServer()
{
    try
    {
        if (_gameGrain != null && !string.IsNullOrEmpty(PlayerId))
        {
            await _gameGrain.DisconnectPlayer(PlayerId);
        }
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Failed to disconnect player {PlayerId} cleanly", PlayerId);
        // Don't rethrow - cleanup should continue
    }
    finally
    {
        // Always reset state regardless of disconnect success  
        _gameGrain = null;
        IsConnected = false;
        _lastInputDirection = Vector2.Zero;
        _lastInputShooting = false;
    }
}
```

**Why This Works**: 
- Attempts clean disconnect but doesn't fail if server is unavailable
- Always resets local state to prevent inconsistencies
- Provides diagnostic information about cleanup failures

---

## 🧪 Testing Patterns

### State Machine Testing

**Pattern**: Test all state transitions explicitly.

**Example**:
```csharp
[Test]
public async Task ZoneTransition_WhenAlreadyTransitioning_ShouldSkip()
{
    // Arrange
    _client._isTransitioning = true;
    var targetZone = new GridSquare(1, 0);
    
    // Act
    await _client.PerformZoneTransition(targetZone);
    
    // Assert
    _mockLogger.Verify(x => x.LogDebug("Zone transition already in progress, skipping"));
    // Verify no actual transition work was done
}
```

### Error Recovery Testing

**Pattern**: Test recovery from failure states.

**Example**:
```csharp  
[Test]
public async Task SendPlayerInput_WhenRpcTimesOut_ShouldMarkDisconnected()
{
    // Arrange
    _mockGrain.Setup(x => x.UpdatePlayerInput(It.IsAny<string>(), It.IsAny<Vector2>()))
             .ThrowsAsync(new TimeoutException());
    
    // Act
    _client.SendPlayerInputEx(Vector2.Zero, null);
    await Task.Delay(100); // Allow background task to complete
    
    // Assert  
    Assert.False(_client.IsConnected);
    _mockServerChanged.Verify(x => x("Connection lost"));
}
```

---

## 📚 Documentation Patterns

### Method Documentation

**Pattern**: Document timing expectations and failure modes.

**Example**:
```csharp
/// <summary>
/// Performs zone transition to target zone with retry logic.
/// </summary>
/// <param name="targetZone">The zone to transition to</param>
/// <returns>Task that completes when transition finishes (typically 0.5-2s)</returns>
/// <remarks>
/// This method:
/// - Uses pre-established connections when available for faster transitions
/// - Retries failed connections up to 3 times
/// - Falls back to forced transition after 5 seconds
/// - Updates connection state and health monitor upon completion
/// </remarks>
public async Task PerformZoneTransition(GridSquare targetZone)
```

### Configuration Documentation

**Pattern**: Document why values were chosen.

**Example**:
```csharp
// Zone transition timing configuration
private const int MIN_TIME_BETWEEN_TRANSITIONS_MS = 200; // Prevent rapid oscillation
private const int DEBOUNCE_DELAY_MS = 150; // Confirm zone change is stable  
private const int MAX_RAPID_TRANSITIONS = 8; // Allow burst then cooldown
private const float ZONE_HYSTERESIS_DISTANCE = 2f; // Balance responsiveness vs stability
//   ^^ Reduced from 5f after testing - was preventing legitimate transitions
//   ^^ Must be >1f to prevent boundary oscillation
//   ^^ Values 3-5f caused players to get stuck at boundaries
```

This documentation style helps future developers understand not just what the values are, but why they were chosen and what happens if you change them.