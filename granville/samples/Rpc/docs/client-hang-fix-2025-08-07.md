# Client Hang Fix - RPC Disconnection Detection

## Issue
Client browser hung and required refresh to recover. Investigation revealed the RPC client lost connection but continued trying to poll world state, causing errors.

## Root Cause
When the RPC client disconnects (e.g., due to network issues or server restart), the `IsConnected` property doesn't immediately reflect the disconnection. This causes:
1. World state polling to throw `InvalidOperationException: RPC client is not connected`
2. Client to keep trying to poll without reconnecting
3. Browser appears frozen as no updates are received

## Timeline of Events
1. 18:06:50.874 - SignalR connection lost
2. 18:06:51.110 - RPC client throws "not connected" error
3. 18:06:51.140 - Application shutdown initiated
4. Client hung until user refreshed browser

## Fixes Implemented

### 1. RPC Disconnection Detection (GranvilleRpcGameClientService.cs)

#### Immediate Detection
```csharp
// Check if this is a disconnection error
if (ex is InvalidOperationException && ex.Message.Contains("RPC client is not connected"))
{
    _logger.LogWarning("[RPC_DISCONNECT] RPC client disconnected, marking connection as lost");
    IsConnected = false;
    
    // Immediately attempt reconnection for RPC disconnection
    if (_worldStatePollFailures == 1)
    {
        _logger.LogInformation("[RPC_RECONNECT] Attempting immediate reconnection after RPC disconnect");
        _ = Task.Run(async () => await AttemptReconnection());
    }
}
```

#### Proactive Check
```csharp
// Double-check RPC client connection status before polling
if (_rpcClient != null && _rpcClient.GetType().GetProperty("IsConnected")?.GetValue(_rpcClient) is bool rpcConnected && !rpcConnected)
{
    _logger.LogWarning("[RPC_CHECK] RPC client reports disconnected, skipping poll and marking connection as lost");
    IsConnected = false;
    _ = Task.Run(async () => await AttemptReconnection());
    return;
}
```

### 2. Client-Side Connection Monitoring (Game.razor)

#### Connection Monitor Timer
```csharp
private void StartConnectionMonitoring()
{
    _connectionMonitorTimer = new System.Timers.Timer(2000); // Check every 2 seconds
    _connectionMonitorTimer.Elapsed += async (sender, e) =>
    {
        var timeSinceLastUpdate = DateTime.UtcNow - _lastWorldStateUpdate;
        if (timeSinceLastUpdate.TotalSeconds > 5 && _isConnected)
        {
            // Check if RPC client is still connected
            if (!RpcGameClient.IsConnected)
            {
                // Attempt reconnection
                var reconnected = await RpcGameClient.ConnectAsync(_playerName);
                if (reconnected)
                {
                    _errorMessage = "";
                    _lastWorldStateUpdate = DateTime.UtcNow;
                }
                else
                {
                    _errorMessage = "Failed to reconnect. Please refresh the page.";
                    _isConnected = false;
                    StopConnectionMonitoring();
                }
            }
        }
    };
}
```

#### World State Update Tracking
```csharp
private void OnWorldStateUpdated(WorldState worldState)
{
    _currentWorldState = worldState;
    _lastWorldStateUpdate = DateTime.UtcNow;
    
    // Reset consecutive failure count on successful update
    _consecutiveUpdateFailures = 0;
    // ...
}
```

## Benefits

1. **Faster Detection**: RPC disconnection detected immediately instead of after 10 failures
2. **Automatic Recovery**: Attempts reconnection without user intervention
3. **User Feedback**: Shows "Connection lost. Attempting to reconnect..." message
4. **Graceful Degradation**: If reconnection fails, prompts user to refresh
5. **No More Hangs**: Client won't freeze when connection is lost

## Testing

1. Start the application normally
2. Kill the ActionServer or Silo process
3. Client should detect disconnection within 5 seconds
4. Client should attempt reconnection automatically
5. If server is restarted, client should reconnect successfully
6. If server stays down, client should show error message

## Monitoring

Look for these log patterns:
- `[RPC_DISCONNECT]` - RPC client disconnection detected
- `[RPC_RECONNECT]` - Reconnection attempt initiated
- `[RPC_CHECK]` - Proactive disconnection check
- `[CONNECTION_MONITOR]` - Client-side connection monitoring

## Future Improvements

1. Exponential backoff for reconnection attempts
2. Maximum retry limit before giving up
3. Connection quality indicator in UI
4. Automatic server migration if current server fails