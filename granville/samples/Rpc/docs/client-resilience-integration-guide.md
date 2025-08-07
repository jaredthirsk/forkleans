# Client Resilience Integration Guide

## Overview

This guide documents the integration of three protection components into the Shooter client to prevent freezes and improve stability during zone transitions and connection issues.

## Components

### 1. ZoneTransitionDebouncer
**Purpose**: Prevents rapid zone transitions that cause client freezes  
**Location**: `Shooter.Client.Common/ZoneTransitionDebouncer.cs`  
**Key Features**:
- Spatial hysteresis (20-unit buffer)
- Temporal debouncing (300ms delay)
- Rate limiting (5 transitions max)
- Cooldown period (2 seconds)

### 2. RobustTimerManager
**Purpose**: Protects timers during transitions instead of disposing them  
**Location**: `Shooter.Client.Common/RobustTimerManager.cs`  
**Key Features**:
- Transition scopes for atomic protection
- Automatic timer restart
- Health monitoring
- Centralized timer management

### 3. ConnectionResilienceManager
**Purpose**: Manages connection recovery with intelligent retry logic  
**Location**: `Shooter.Client.Common/ConnectionResilienceManager.cs`  
**Key Features**:
- Exponential backoff (1s to 30s)
- 10 retry attempts maximum
- Connection health tracking
- Diagnostic information

## Integration Steps

### Step 1: Add Components to Service

```csharp
public class GranvilleRpcGameClientService : IDisposable
{
    // Add component fields
    private readonly ZoneTransitionDebouncer _zoneDebouncer;
    private readonly RobustTimerManager _timerManager;
    private readonly ConnectionResilienceManager _connectionManager;
    
    // Update constructor
    public GranvilleRpcGameClientService(
        ILogger<GranvilleRpcGameClientService> logger,
        HttpClient httpClient,
        IConfiguration configuration,
        ILoggerFactory loggerFactory)  // Added parameter
    {
        _logger = logger;
        _httpClient = httpClient;
        _configuration = configuration;
        
        // Initialize components
        _zoneDebouncer = new ZoneTransitionDebouncer(
            loggerFactory.CreateLogger<ZoneTransitionDebouncer>());
        _timerManager = new RobustTimerManager(
            loggerFactory.CreateLogger<RobustTimerManager>());
        _connectionManager = new ConnectionResilienceManager(
            loggerFactory.CreateLogger<ConnectionResilienceManager>());
    }
}
```

### Step 2: Integrate Zone Transition Debouncing

```csharp
private async Task CheckForServerTransitionInternal()
{
    // Get player position and zone
    var playerEntity = _lastWorldState?.Entities?
        .FirstOrDefault(e => e.EntityId == PlayerId);
    if (playerEntity == null) return;
    
    var playerZone = GridSquare.FromPosition(playerEntity.Position);
    
    // Check if already in correct zone
    if (_currentZone != null && 
        playerZone.X == _currentZone.X && 
        playerZone.Y == _currentZone.Y)
    {
        return;
    }
    
    // Use debouncer to prevent rapid transitions
    var shouldTransition = await _zoneDebouncer.ShouldTransitionAsync(
        playerZone,
        playerEntity.Position,
        async () => await PerformZoneTransitionDebounced(
            playerZone, playerEntity.Position)
    );
    
    if (!shouldTransition)
    {
        _logger.LogDebug(
            "[ZONE_TRANSITION] Transition to zone ({X},{Y}) prevented by debouncer", 
            playerZone.X, playerZone.Y);
    }
}

private async Task PerformZoneTransitionDebounced(
    GridSquare playerZone, 
    Vector2 playerPosition)
{
    _isTransitioning = true;
    
    try
    {
        // Query for correct server
        var response = await _httpClient.GetFromJsonAsync<ActionServerInfo>(
            $"api/world/players/{PlayerId}/server");
            
        if (response != null && response.ServerId != CurrentServerId)
        {
            // Use timer manager to protect timers
            using (var scope = _timerManager.BeginTransition(
                $"zone change to {response.AssignedSquare.X},{response.AssignedSquare.Y}"))
            {
                // Timers are paused here, not disposed
                
                // Disconnect from old server
                if (_gameGrain != null && !string.IsNullOrEmpty(PlayerId))
                {
                    await _gameGrain.DisconnectPlayer(PlayerId);
                }
                
                Cleanup(); // Clean up old connection
                
                // Connect to new server
                await ConnectToActionServer(response);
                
                // Timers automatically resume when scope disposes
            }
        }
    }
    finally
    {
        _isTransitioning = false;
    }
}
```

### Step 3: Replace Timer Creation

```csharp
// OLD: Direct timer creation
_worldStateTimer = new Timer(async _ => await PollWorldState(), 
    null, TimeSpan.Zero, TimeSpan.FromMilliseconds(33));

// NEW: Use RobustTimerManager
_timerManager.CreateTimer("worldState", 
    async _ => await PollWorldState(), 33);

// Create all timers
private void StartTimers()
{
    _timerManager.CreateTimer("worldState", 
        async _ => await PollWorldState(), 33);
    _timerManager.CreateTimer("heartbeat", 
        async _ => await SendHeartbeat(), 5000);
    _timerManager.CreateTimer("availableZones", 
        async _ => await PollAvailableZones(), 10000);
    _timerManager.CreateTimer("networkStats", 
        async _ => await PollNetworkStats(), 1000);
    _timerManager.CreateTimer("watchdog", 
        _ => { CheckPollingHealth(); }, 5000);
}
```

### Step 4: Add Connection Resilience

```csharp
private async Task AttemptReconnection()
{
    if (_isTransitioning)
    {
        _logger.LogInformation(
            "Already transitioning, skipping reconnection attempt");
        return;
    }
    
    try
    {
        // Use ConnectionResilienceManager for robust reconnection
        var result = await _connectionManager.ExecuteWithReconnect(
            async () => await TestAndRestoreConnection(),
            "reconnection",
            _cancellationTokenSource?.Token ?? CancellationToken.None
        );
        
        if (result != null)
        {
            _logger.LogInformation("Reconnection successful");
        }
        else
        {
            _logger.LogWarning("Reconnection failed after all attempts");
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error during reconnection attempt");
    }
}

private async Task<object> TestAndRestoreConnection()
{
    if (_gameGrain != null)
    {
        try
        {
            // Test current connection
            var testState = await _gameGrain.GetWorldState();
            if (testState != null)
            {
                // Connection is good
                _worldStatePollFailures = 0;
                IsConnected = true;
                _connectionManager.MarkConnectionSuccess();
                
                // Restart timers
                _timerManager.RestartAllTimers();
                
                return new object(); // Success
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Connection test failed");
            _connectionManager.MarkConnectionFailure("Test failed");
        }
    }
    
    // Try to reconnect via zone check
    await CheckForServerTransition();
    
    if (IsConnected)
    {
        return new object(); // Success
    }
    
    throw new InvalidOperationException("Failed to restore connection");
}
```

### Step 5: Update Dependency Injection

```csharp
// In Program.cs
builder.Services.AddScoped<GranvilleRpcGameClientService>(
    serviceProvider =>
{
    var logger = serviceProvider
        .GetRequiredService<ILogger<GranvilleRpcGameClientService>>();
    var loggerFactory = serviceProvider
        .GetRequiredService<ILoggerFactory>(); // Added
    var httpClientFactory = serviceProvider
        .GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient();
    httpClient.BaseAddress = new Uri(siloUrl);
    var configuration = serviceProvider
        .GetRequiredService<IConfiguration>();
        
    return new GranvilleRpcGameClientService(
        logger, httpClient, configuration, loggerFactory);
});
```

### Step 6: Clean Up in Dispose

```csharp
public void Dispose()
{
    Cleanup();
    
    // Clean up pre-established connections
    var allKeys = _preEstablishedConnections.Keys.ToList();
    foreach (var key in allKeys)
    {
        _ = Task.Run(async () => 
            await CleanupPreEstablishedConnection(key));
    }
    _preEstablishedConnections.Clear();
    
    // Dispose protection components
    _timerManager?.Dispose();
    _zoneDebouncer?.Reset();
    _connectionManager?.Reset();
    
    PlayerId = null;
}
```

## Configuration Tuning

### Debouncer Settings

```csharp
// In ZoneTransitionDebouncer.cs
private const int MIN_TIME_BETWEEN_TRANSITIONS_MS = 500;  // Minimum gap
private const int DEBOUNCE_DELAY_MS = 300;               // Confirmation delay
private const int MAX_RAPID_TRANSITIONS = 5;             // Before cooldown
private const int COOLDOWN_PERIOD_MS = 2000;             // Cooldown duration
private const float ZONE_HYSTERESIS_DISTANCE = 20f;      // Buffer zone
```

### Timer Manager Settings

```csharp
// Timer periods in milliseconds
"worldState": 33     // 30 FPS polling
"heartbeat": 5000    // Every 5 seconds
"availableZones": 10000  // Every 10 seconds
"networkStats": 1000     // Every second
"watchdog": 5000        // Every 5 seconds
```

### Connection Resilience Settings

```csharp
// In ConnectionResilienceManager.cs
private const int MAX_RECONNECT_ATTEMPTS = 10;
private const int INITIAL_BACKOFF_MS = 1000;
private const int MAX_BACKOFF_MS = 30000;
private const double BACKOFF_MULTIPLIER = 1.5;
```

## Monitoring

### Log Patterns to Watch

```bash
# Debouncer activity
grep "ZONE_DEBOUNCE" logs/client*.log

# Timer management
grep "TIMER_MANAGER" logs/client*.log

# Connection resilience
grep "CONNECTION_RESILIENCE" logs/client*.log

# Zone transitions
grep "ZONE_TRANSITION" logs/client*.log
```

### Health Metrics

```csharp
// Get diagnostics
string debounceStatus = _zoneDebouncer.GetDiagnostics();
string connectionStatus = _connectionManager.GetDiagnostics();

// Check timer health
_timerManager.CheckTimerHealth();

// Monitor rapid transitions
int rapidTransitionCount = _zoneDebouncer.RapidTransitionCount;
bool inCooldown = _zoneDebouncer.IsInCooldown;

// Connection health
bool isHealthy = _connectionManager.IsHealthy;
int reconnectAttempts = _connectionManager.ReconnectAttempts;
```

## Testing

### Manual Testing

1. **Zone Boundary Test**
   - Move player to zone boundary (x=500)
   - Rapidly move back and forth
   - Verify no freezes occur

2. **Connection Loss Test**
   - Kill an ActionServer while connected
   - Verify automatic reconnection
   - Check exponential backoff in logs

3. **Timer Recovery Test**
   - Trigger zone transition
   - Verify timers pause and resume
   - Check world state polling continues

### Automated Testing

```bash
# Run robustness test
./scripts/test-robustness.sh

# Check for issues
grep -E "timeout|freeze|stuck" logs/*.log

# Verify success rate
grep "Success rate:" logs/robustness-test.log
```

## Troubleshooting

### Issue: Still Getting Freezes

1. Check debouncer is active:
   ```bash
   grep "ZONE_DEBOUNCE" logs/client.log | tail -20
   ```

2. Verify timer manager is protecting:
   ```bash
   grep "Beginning transition" logs/client.log
   grep "Ending transition" logs/client.log
   ```

3. Check connection resilience:
   ```bash
   grep "CONNECTION_RESILIENCE" logs/client.log | tail -20
   ```

### Issue: Transitions Too Slow

- Reduce `DEBOUNCE_DELAY_MS` to 200ms
- Reduce `ZONE_HYSTERESIS_DISTANCE` to 15 units

### Issue: Too Many Cooldowns

- Increase `MAX_RAPID_TRANSITIONS` to 7-10
- Reduce `COOLDOWN_PERIOD_MS` to 1500ms

## Performance Impact

- **Memory**: ~5KB per component instance
- **CPU**: Negligible (timer callbacks only)
- **Latency**: 300ms added to first zone transition
- **Network**: No additional traffic

## Future Improvements

1. **Predictive Pre-connections**
   - Connect to adjacent zones before needed
   - Zero-latency transitions

2. **Adaptive Tuning**
   - Adjust parameters based on network quality
   - Learn player movement patterns

3. **Server Coordination**
   - Server hints for optimal transition timing
   - Coordinated handoffs between ActionServers

---

*Integration Guide Created: 2025-08-07*  
*Components Version: 1.0.0*