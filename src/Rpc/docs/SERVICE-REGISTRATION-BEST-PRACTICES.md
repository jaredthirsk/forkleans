# RPC Client Service Registration Best Practices

## Overview

This document outlines best practices for registering and using RPC client services to avoid common timing and dependency issues.

## Service Registration Guidelines

### 1. Use Keyed Services for RPC Components

Always register RPC-specific services as keyed services to avoid conflicts with Orleans client:

```csharp
// Good - keyed service registration
services.AddKeyedSingleton<IClusterManifestProvider, MultiServerManifestProvider>("rpc");
services.AddKeyedSingleton<IGrainFactory>("rpc", (sp, key) => sp.GetRequiredService<RpcGrainFactory>());

// Bad - unkeyed registration can conflict
services.AddSingleton<IClusterManifestProvider, MultiServerManifestProvider>();
```

### 2. Avoid Circular Dependencies

Be careful when injecting services that depend on each other:

```csharp
// Bad - circular dependency
public class ServiceA
{
    public ServiceA(ServiceB b) { }
}

public class ServiceB
{
    public ServiceB(ServiceA a) { }
}

// Good - lazy resolution
public class ServiceA
{
    private readonly IServiceProvider _serviceProvider;
    
    public ServiceA(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }
    
    private ServiceB GetServiceB() => _serviceProvider.GetRequiredService<ServiceB>();
}
```

### 3. Handle Async Initialization Properly

Always wait for async operations to complete before using dependent services:

```csharp
// Bad - race condition
await host.StartAsync();
var grain = rpcClient.GetGrain<T>(); // Manifest may not be ready!

// Good - explicit wait
await host.StartAsync();
await rpcClient.WaitForManifestAsync();
var grain = rpcClient.GetGrain<T>();
```

## Connection Management Best Practices

### 1. Always Check Connection State

Before making RPC calls, ensure the client is connected:

```csharp
public async Task<bool> MakeRpcCall()
{
    try
    {
        // Wait for manifest to ensure connection is ready
        await _rpcClient.WaitForManifestAsync(TimeSpan.FromSeconds(5));
        
        var grain = _rpcClient.GetGrain<IMyGrain>("key");
        return await grain.DoSomething();
    }
    catch (TimeoutException)
    {
        _logger.LogError("RPC server not available");
        return false;
    }
}
```

### 2. Implement Retry Logic

Add retry logic for transient failures:

```csharp
public async Task<T> CallWithRetry<T>(Func<Task<T>> operation, int maxRetries = 3)
{
    for (int i = 0; i < maxRetries; i++)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex) when (i < maxRetries - 1)
        {
            _logger.LogWarning("Attempt {Attempt} failed: {Error}", i + 1, ex.Message);
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i))); // Exponential backoff
        }
    }
    throw new InvalidOperationException($"Operation failed after {maxRetries} attempts");
}
```

### 3. Handle Disconnections Gracefully

Subscribe to connection events and handle disconnections:

```csharp
public class RpcClientWrapper
{
    private readonly IRpcClient _rpcClient;
    private bool _isConnected;
    
    public async Task EnsureConnected()
    {
        if (!_isConnected)
        {
            await _rpcClient.WaitForManifestAsync();
            _isConnected = true;
        }
    }
    
    public async Task<T> GetGrain<T>(string key) where T : IGrainWithStringKey
    {
        await EnsureConnected();
        return _rpcClient.GetGrain<T>(key);
    }
}
```

## Common Pitfalls to Avoid

### 1. Not Waiting for Manifest

**Problem**: Calling GetGrain immediately after StartAsync
**Solution**: Always use WaitForManifestAsync

### 2. Ignoring Timeout Exceptions

**Problem**: Not handling TimeoutException from WaitForManifestAsync
**Solution**: Catch and handle appropriately

### 3. Mixing Orleans and RPC Services

**Problem**: Using unkeyed services that conflict with Orleans
**Solution**: Use keyed services with "rpc" key

### 4. Blocking Async Operations

**Problem**: Using .Result or .Wait() on async operations
**Solution**: Use async/await throughout

## Testing Recommendations

### 1. Unit Test Service Registration

```csharp
[Fact]
public void Services_Should_Be_Registered_As_Keyed()
{
    var services = new ServiceCollection();
    services.AddRpcClient(/* config */);
    
    var provider = services.BuildServiceProvider();
    
    // Verify keyed services exist
    var manifestProvider = provider.GetKeyedService<IClusterManifestProvider>("rpc");
    Assert.NotNull(manifestProvider);
}
```

### 2. Integration Test Connection Flow

```csharp
[Fact]
public async Task Client_Should_Wait_For_Manifest()
{
    // Arrange
    var host = CreateTestHost();
    await host.StartAsync();
    
    var rpcClient = host.Services.GetRequiredService<IRpcClient>();
    
    // Act & Assert
    await Assert.ThrowsAsync<TimeoutException>(async () =>
    {
        // Should timeout if no server running
        await rpcClient.WaitForManifestAsync(TimeSpan.FromSeconds(1));
    });
}
```

### 3. Test Retry Logic

```csharp
[Fact]
public async Task Should_Retry_On_Transient_Failure()
{
    var attempts = 0;
    var result = await CallWithRetry(async () =>
    {
        attempts++;
        if (attempts < 3)
            throw new TimeoutException();
        return "success";
    });
    
    Assert.Equal(3, attempts);
    Assert.Equal("success", result);
}
```

## Debugging Tips

### 1. Enable Debug Logging

```csharp
logging.AddFilter("Granville.Rpc", LogLevel.Debug);
logging.AddFilter("Orleans", LogLevel.Debug);
```

### 2. Check Service Registration

```csharp
// In your startup code
var grainFactory = services.GetKeyedService<IGrainFactory>("rpc");
_logger.LogInformation("RPC GrainFactory type: {Type}", 
    grainFactory?.GetType().FullName ?? "NULL");
```

### 3. Monitor Manifest State

```csharp
var manifestProvider = services.GetKeyedService<IClusterManifestProvider>("rpc");
if (manifestProvider is MultiServerManifestProvider multiServer)
{
    var manifest = multiServer.Current;
    _logger.LogInformation("Manifest has {GrainCount} grains", 
        manifest?.AllGrainManifests.Sum(m => m.Grains.Count) ?? 0);
}
```

## Summary

Following these best practices will help avoid common timing and dependency issues:

1. Always use `WaitForManifestAsync` before calling `GetGrain`
2. Register RPC services as keyed services
3. Handle timeouts and connection failures gracefully
4. Implement retry logic for transient failures
5. Enable debug logging for troubleshooting