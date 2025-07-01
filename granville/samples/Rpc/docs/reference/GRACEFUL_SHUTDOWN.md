# Graceful Shutdown Reference

This document explains how graceful shutdown works in the Shooter RPC application and provides guidance for proper service termination.

## Overview

Graceful shutdown ensures that applications terminate cleanly by completing current operations, releasing resources properly, and avoiding data corruption or connection errors.

## Normal Process Termination vs Graceful Shutdown

### Without Graceful Shutdown (Ctrl+C)

When you press Ctrl+C without proper handling:
- OS sends `SIGINT` (interrupt signal) to process
- .NET runtime immediately starts terminating
- Services may be killed mid-operation
- Database transactions could be lost
- Network connections abruptly closed
- Temporary files/resources not cleaned up
- Results in errors like "Connection attempt failed" in logs

### With Graceful Shutdown

With proper graceful shutdown:
- Application receives shutdown signal
- Controlled shutdown sequence begins
- Services complete current operations
- Resources are properly released
- Clean exit with completion messages

## .NET's Built-in Graceful Shutdown

.NET has built-in graceful shutdown through `IHostApplicationLifetime`:

```csharp
// Default behavior - .NET automatically handles Ctrl+C
app.Run(); // This already supports graceful shutdown!
```

When you press **Ctrl+C**, the default .NET behavior:
1. .NET catches the `SIGINT` signal
2. Triggers `IHostApplicationLifetime.ApplicationStopping`
3. Calls `StopAsync()` on all `IHostedService` instances
4. Waits for them to complete (with timeout)
5. Disposes services
6. Exits cleanly

## Enhanced Graceful Shutdown Implementation

The Shooter AppHost includes enhanced graceful shutdown with additional control and feedback:

```csharp
Console.CancelKeyPress += (sender, e) =>
{
    Console.WriteLine("\n🔄 Graceful shutdown initiated... Press Ctrl+C again to force quit.");
    e.Cancel = true; // Prevent immediate termination
    
    // Trigger graceful shutdown
    var cts = new CancellationTokenSource();
    cts.CancelAfter(TimeSpan.FromSeconds(30)); // 30 second timeout
    
    _ = Task.Run(async () =>
    {
        try
        {
            await app.StopAsync(cts.Token);
            Console.WriteLine("✅ Graceful shutdown completed.");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("⚠️ Graceful shutdown timed out. Forcing exit.");
        }
        Environment.Exit(0);
    });
};
```

### What This Enhanced Version Does

1. **Intercepts Ctrl+C** before .NET's default handler
2. **Provides user feedback** ("Graceful shutdown initiated...")
3. **Sets `e.Cancel = true`** to prevent immediate exit
4. **Manually triggers `StopAsync()`** with a timeout
5. **Allows force quit** with second Ctrl+C
6. **Shows completion status**

## Shutdown Sequence in Shooter Application

When graceful shutdown occurs, this sequence happens:

```
Ctrl+C Pressed
    ↓
Console.CancelKeyPress Event
    ↓
e.Cancel = true
    ↓
app.StopAsync() Called
    ↓
IHostedService.StopAsync() on All Services
    ↓
┌─────────────────┬─────────────────┬─────────────────┐
│  Silo:          │  ActionServer:  │  Bot:           │
│  Stop Orleans   │  Stop RPC       │  Stop Bot       │
│                 │  Server         │  Service        │
├─────────────────┼─────────────────┼─────────────────┤
│  Close Orleans  │  Close RPC      │  Stop Bot       │
│  Connections    │  Connections    │  Activities     │
└─────────────────┴─────────────────┴─────────────────┘
    ↓
Dispose Resources
    ↓
Exit Process
```

### Specific Service Shutdowns

#### 1. Silo (`Shooter.Silo`)
- Orleans silo stops accepting new requests
- Completes in-flight grain activations
- Closes cluster connections
- Saves any persistent state
- Logs: `"Orleans Silo stopped."`

#### 2. ActionServer (`Shooter.ActionServer`)
- RPC server stops accepting new connections
- Completes current RPC calls
- Unregisters from Silo
- Stops world simulation
- Closes UDP sockets
- Logs: `"RPC server stopped successfully"`

#### 3. Bot (`Shooter.Bot`)
- Stops bot AI loops
- Closes connections to ActionServers
- Cleans up timers/tasks
- Logs: `"Bot service stopped"`

## Benefits of Enhanced Implementation

### User Experience
- **Visual feedback** during shutdown process
- **Escape hatch** (second Ctrl+C for force quit)
- **Timeout protection** (won't hang forever)
- **Clear status messages**

### System Reliability
- **Proper cleanup** of resources
- **No orphaned processes**
- **Better log file integrity**
- **Reduced connection errors** in logs
- **Prevents data corruption**

## Alternative Shutdown Methods

Instead of Ctrl+C, you can use:

### 1. SIGTERM (More Graceful)
```bash
# Send SIGTERM (more graceful than SIGINT)
kill -TERM <process_id>
```

### 2. Admin Endpoint
```bash
# Use the admin endpoint available on ActionServers
curl -X POST http://localhost:7072/api/admin/shutdown
```

### 3. Service Managers (Production)
```bash
# In production, use service managers
systemctl stop shooter-service
```

### 4. Docker/Container Environments
```bash
# Docker sends SIGTERM by default
docker stop <container_id>

# Kubernetes graceful shutdown
kubectl delete pod <pod_name> --grace-period=30
```

## Testing Graceful Shutdown

You can verify graceful shutdown works by:

### 1. Start the Application
```bash
cd Shooter.AppHost
dotnet run
```

### 2. Monitor Logs During Shutdown
Watch for orderly shutdown messages in the console and log files.

### 3. Look for Completion Messages

**Good Graceful Shutdown Logs:**
```
🔄 Graceful shutdown initiated... Press Ctrl+C again to force quit.
[Information] Orleans Silo stopped.
[Information] RPC server stopped successfully
[Information] Client shutdown completed
✅ Graceful shutdown completed.
```

**Bad Abrupt Shutdown Logs:**
```
[Warning] Connection attempt to endpoint S127.0.0.1:30000:0 failed
[Error] Failed to register ActionServer with Silo
[Process terminated]
```

### 4. Verification Checklist

- [ ] No connection error messages during shutdown
- [ ] All services log completion messages
- [ ] No orphaned processes remain
- [ ] Log files are properly closed
- [ ] Temporary resources cleaned up

## Troubleshooting Graceful Shutdown

### Shutdown Hangs/Times Out

**Symptoms:**
- Shutdown takes longer than 30 seconds
- "⚠️ Graceful shutdown timed out" message appears

**Common Causes:**
- Services not responding to cancellation tokens
- Infinite loops in service shutdown code
- Deadlocks in cleanup operations
- External dependencies not responding

**Solutions:**
- Check service `StopAsync()` implementations
- Ensure proper cancellation token usage
- Add timeout handling to external calls
- Review service dependencies

### Services Don't Stop Cleanly

**Symptoms:**
- Connection errors in logs during shutdown
- Services appear to restart after shutdown
- Resources not properly released

**Common Causes:**
- Missing `IHostedService` interface implementation
- Services not registered with DI container
- Incorrect service lifetime registration

**Solutions:**
- Ensure services implement `IHostedService`
- Register services with `AddHostedService()`
- Check service registration order

### Force Quit Required

**Symptoms:**
- Second Ctrl+C needed to exit
- Application doesn't respond to first shutdown signal

**Common Causes:**
- Blocking operations in shutdown code
- Services not honoring cancellation tokens
- Thread-unsafe shutdown operations

**Solutions:**
- Use `async`/`await` in shutdown code
- Respect `CancellationToken` parameters
- Avoid blocking calls in service cleanup

## Best Practices

### 1. Service Implementation
```csharp
public class MyService : IHostedService
{
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Use the cancellation token
        await CleanupAsync(cancellationToken);
        
        // Don't block indefinitely
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await ExternalCleanupAsync(timeout.Token);
    }
}
```

### 2. Resource Cleanup
```csharp
public async Task StopAsync(CancellationToken cancellationToken)
{
    try
    {
        // Close connections gracefully
        await _connection?.CloseAsync(cancellationToken);
    }
    finally
    {
        // Always dispose resources
        _connection?.Dispose();
    }
}
```

### 3. Timeout Handling
```csharp
public async Task StopAsync(CancellationToken cancellationToken)
{
    using var combined = CancellationTokenSource.CreateLinkedTokenSource(
        cancellationToken,
        new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
    
    await DoCleanupAsync(combined.Token);
}
```

## Production Considerations

### 1. Health Check Integration
Implement health checks that can indicate when services are ready for shutdown:

```csharp
public class ReadinessHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        // Return Unhealthy when shutting down
        return Task.FromResult(_isShuttingDown 
            ? HealthCheckResult.Unhealthy("Service shutting down")
            : HealthCheckResult.Healthy());
    }
}
```

### 2. Load Balancer Integration
- Update load balancer health checks during shutdown
- Drain connections before stopping services
- Coordinate with orchestration platforms (Kubernetes, etc.)

### 3. Monitoring and Alerting
- Monitor shutdown duration metrics
- Alert on timeout occurrences
- Track resource cleanup completion

### 4. Database Transactions
- Complete or rollback active transactions
- Ensure connection pools are properly closed
- Verify data consistency after shutdown

## Related Documentation

- [.NET Generic Host Shutdown](https://docs.microsoft.com/en-us/dotnet/core/extensions/generic-host#host-shutdown)
- [IHostedService Interface](https://docs.microsoft.com/en-us/dotnet/api/microsoft.extensions.hosting.ihostedservice)
- [Orleans Shutdown Documentation](https://docs.microsoft.com/en-us/dotnet/orleans/host/configuration#shutdown)
- [Container Graceful Shutdown](../deployment/CONTAINER_DEPLOYMENT.md)