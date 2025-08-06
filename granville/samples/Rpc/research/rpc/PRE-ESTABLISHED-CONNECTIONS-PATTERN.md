# Pre-Established Connections Pattern in Shooter RPC Client

## Overview

The Shooter game client uses a sophisticated pre-established connection pattern to ensure smooth zone transitions. This document explains how these connections work and their different initialization patterns.

## Connection Types

The Shooter client manages two types of RPC connections:

1. **Main Connection** - The primary connection to the current zone's server
2. **Pre-Established Connections** - Background connections to neighboring zones for quick transitions

## Connection Initialization Patterns

### Pattern 1: Initial Connection (ConnectAsync)

When the client first connects to the game, it follows this pattern:

```csharp
// Create RPC client using helper method
var host = BuildRpcHost(resolvedHost, rpcPort, PlayerId);
_rpcHost = host;

// Start the host in the background to avoid blocking on console lifetime
_ = host.RunAsync();

// Give the host time to start services
await Task.Delay(500);

// Defer getting the RPC client service to avoid potential blocking
await Task.Delay(100);
_rpcClient = host.Services.GetRequiredService<Granville.Rpc.IRpcClient>();
```

**Important Note**: Using `await host.StartAsync()` with `Host.CreateDefaultBuilder()` can cause issues because the default builder includes console lifetime management that might block. Using `RunAsync()` in a fire-and-forget pattern avoids this issue.

### Pattern 2: Pre-Established Connections (EstablishConnection)

For neighboring zones, connections are established in the background:

```csharp
// Create RPC client
var hostBuilder = Host.CreateDefaultBuilder()
    .UseOrleansRpcClient(rpcBuilder => { ... })
    .Build();

// Start the host without awaiting - it runs in the background
_ = hostBuilder.RunAsync();

// Wait a bit for the host to start up
await Task.Delay(100);

connection.RpcHost = hostBuilder;

// Defer getting the RPC client service to avoid potential blocking
connection.RpcClient = hostBuilder.Services.GetRequiredService<Granville.Rpc.IRpcClient>();
```

### Pattern 3: Zone Transition with Pre-Established Connection

When transitioning to a zone with a pre-established connection:

```csharp
// If using a pre-established connection, create an independent host
var hostBuilder = Host.CreateDefaultBuilder()
    .UseOrleansRpcClient(rpcBuilder => { ... })
    .Build();

// Start the host without awaiting
_ = hostBuilder.RunAsync();

await Task.Delay(100);
_rpcHost = hostBuilder;

// Defer service resolution
_rpcClient = hostBuilder.Services.GetRequiredService<Granville.Rpc.IRpcClient>();
```

## Key Differences Between Patterns

### 1. Host Startup Method
- **Initial Connection**: Uses `host.StartAsync()` with explicit await and timeout
- **Pre-Established**: Uses `hostBuilder.RunAsync()` without awaiting (fire-and-forget)

### 2. Service Resolution Timing
- All patterns now defer `GetRequiredService<IRpcClient>()` to avoid blocking during startup
- A 100ms delay is added after host startup to ensure services are initialized
- Retry logic is implemented to handle cases where services aren't immediately available

### 3. Connection Lifecycle
- **Main Connection**: Managed as part of the game client lifecycle
- **Pre-Established**: Run independently in the background, cleaned up based on distance/time

## Why Different Patterns?

### Initial Connection
- Needs synchronous startup confirmation before proceeding
- Uses timeout to prevent hanging if server is unavailable
- Critical for game initialization

### Pre-Established Connections
- Run asynchronously in the background
- Don't block the main game flow
- Can fail without affecting current gameplay
- Optimized for quick zone transitions

### Zone Transitions
- Leverages pre-established connections when available
- Falls back to creating new connections if needed
- Creates independent hosts to prevent cleanup conflicts

## Common Issues and Solutions

### Issue: Console Lifetime Blocking (Critical Bug)
**Problem**: When using `Host.CreateDefaultBuilder()`, the host includes console lifetime management by default. This can cause `StartAsync()` to block or behave unexpectedly in a client application that creates multiple hosts.

**Root Cause**: `Host.CreateDefaultBuilder()` sets up:
- Console lifetime (waits for Ctrl+C or SIGTERM)
- Default logging providers
- Default configuration sources

**Solution 1**: Use `RunAsync()` in a fire-and-forget pattern:
```csharp
_ = host.RunAsync(); // Starts services and runs in background
await Task.Delay(500); // Give services time to start
```

**Solution 2**: Use `HostBuilder` instead of `Host.CreateDefaultBuilder()`:
```csharp
var host = new HostBuilder()
    .ConfigureLogging(...)
    .UseOrleansRpcClient(...)
    .Build();
```

### Issue: Awaiting Host Lifetime Task
**Problem**: The original code attempted to await the result of `StartAsync()` multiple times, misunderstanding what the task represents.

**Root Cause**: Confusion between:
- `StartAsync()` - Starts services and returns immediately
- `RunAsync()` - Starts services and waits for shutdown
- The host's lifetime task - Only completes when host shuts down

### Issue: Host Startup Blocking
**Problem**: Calling `GetRequiredService<IRpcClient>()` immediately after host startup can block if services aren't fully initialized.

**Solution**: Defer service resolution with a delay and retry mechanism:
```csharp
await Task.Delay(100);
try
{
    _rpcClient = host.Services.GetRequiredService<Granville.Rpc.IRpcClient>();
}
catch (Exception serviceEx)
{
    _logger.LogWarning(serviceEx, "Failed to get RPC client service immediately, retrying after delay");
    await Task.Delay(500);
    _rpcClient = host.Services.GetRequiredService<Granville.Rpc.IRpcClient>();
}
```

### Issue: Circular Dependencies
**Problem**: The RPC client has complex circular dependencies that can cause deadlocks during startup.

**Solution**: The deferred service resolution pattern helps break these circular dependencies by allowing the DI container to fully initialize before resolving services.

## Best Practices

1. **Always defer service resolution** after host startup
2. **Use appropriate timeouts** for different connection types
3. **Implement retry logic** for service resolution
4. **Log extensively** during connection establishment for debugging
5. **Test with multiple concurrent connections** to ensure no race conditions

## Connection Management Details

### Distance-Based Pre-Establishment
- Connections are created when player is within 200 units of a zone boundary
- Connections are disposed when player is beyond 400 units (hysteresis)
- Recently visited zones are kept alive longer

### Connection Health Monitoring
- Pre-established connections are periodically health-checked
- Dead connections are cleaned up automatically
- Connection state is tracked for UI visualization

### Resource Management
- Each connection maintains its own IHost instance
- Connections are fully independent to prevent interference
- Proper cleanup ensures no resource leaks