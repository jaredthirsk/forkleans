# Granville RPC Memory Optimization Guide for Game Developers

## Quick Start - Immediate Impact

### 1. Use Direct Transport for High-Frequency Updates

Replace high-frequency RPC calls with direct transport access:

```csharp
// ❌ High allocation - avoid for 60Hz updates
await rpcClient.InvokeAsync<IGameService>(s => s.UpdatePosition(playerId, position));

// ✅ Low allocation - use for position updates
var transport = rpcClient.GetDirectTransport();
var positionBytes = SerializePosition(position);
transport.SendUnreliable(positionBytes);
```

### 2. Pre-allocate and Reuse Buffers

```csharp
public class GameClient
{
    private readonly byte[] _positionBuffer = new byte[32];
    private readonly byte[] _tempBuffer = new byte[1024];
    
    public void SendPosition(Vector3 position)
    {
        // Reuse buffer instead of allocating
        SerializePositionIntoBuffer(position, _positionBuffer);
        _transport.SendUnreliable(_positionBuffer);
    }
}
```

### 3. Batch Multiple Updates

```csharp
public class BatchedGameClient
{
    private readonly List<EntityUpdate> _pendingUpdates = new();
    private readonly byte[] _batchBuffer = new byte[4096];
    
    public void QueueUpdate(EntityUpdate update)
    {
        _pendingUpdates.Add(update);
        if (_pendingUpdates.Count >= 10)
        {
            FlushBatch();
        }
    }
    
    private void FlushBatch()
    {
        var bytesWritten = SerializeBatchIntoBuffer(_pendingUpdates, _batchBuffer);
        _transport.SendUnreliable(_batchBuffer.AsSpan(0, bytesWritten));
        _pendingUpdates.Clear();
    }
}
```

## Memory Allocation Patterns to Avoid

### ❌ High-Allocation Patterns

1. **Creating new byte arrays in hot paths**:
```csharp
// BAD - allocates new array every time
public void SendData(int value)
{
    var buffer = new byte[4];
    BitConverter.GetBytes(value).CopyTo(buffer, 0);
    _transport.Send(buffer);
}
```

2. **String concatenation in loops**:
```csharp
// BAD - allocates strings on every iteration
for (int i = 0; i < players.Count; i++)
{
    var connectionId = "player-" + i;
    // ...
}
```

3. **Boxing value types**:
```csharp
// BAD - boxes the int
var args = new object[] { playerId, position };
await rpcClient.InvokeAsync<IGameService>(s => s.UpdatePlayer(args));
```

### ✅ Low-Allocation Patterns

1. **Reuse pre-allocated buffers**:
```csharp
// GOOD - reuses buffer
private readonly byte[] _buffer = new byte[1024];

public void SendData(int value)
{
    BitConverter.TryWriteBytes(_buffer, value);
    _transport.Send(_buffer.AsSpan(0, 4));
}
```

2. **Use ArrayPool for temporary allocations**:
```csharp
// GOOD - uses pooled arrays
private static readonly ArrayPool<byte> Pool = ArrayPool<byte>.Shared;

public void ProcessLargeData(ReadOnlySpan<byte> data)
{
    var buffer = Pool.Rent(data.Length);
    try
    {
        data.CopyTo(buffer);
        // Process buffer...
    }
    finally
    {
        Pool.Return(buffer);
    }
}
```

3. **Use span-based APIs**:
```csharp
// GOOD - no allocation with spans
public void SerializePosition(Vector3 pos, Span<byte> buffer)
{
    BitConverter.TryWriteBytes(buffer.Slice(0, 4), pos.X);
    BitConverter.TryWriteBytes(buffer.Slice(4, 4), pos.Y);
    BitConverter.TryWriteBytes(buffer.Slice(8, 4), pos.Z);
}
```

## Specific Game Development Scenarios

### 1. Real-Time Player Movement (60Hz)

```csharp
public class PlayerMovementSystem
{
    private readonly IDirectTransport _transport;
    private readonly byte[] _positionBuffer = new byte[28]; // 4 bytes player ID + 3 * 4 bytes position + 3 * 4 bytes velocity
    
    public void SendMovementUpdate(int playerId, Vector3 position, Vector3 velocity)
    {
        var span = _positionBuffer.AsSpan();
        
        // Write directly to buffer - no allocations
        BitConverter.TryWriteBytes(span.Slice(0, 4), playerId);
        BitConverter.TryWriteBytes(span.Slice(4, 4), position.X);
        BitConverter.TryWriteBytes(span.Slice(8, 4), position.Y);
        BitConverter.TryWriteBytes(span.Slice(12, 4), position.Z);
        BitConverter.TryWriteBytes(span.Slice(16, 4), velocity.X);
        BitConverter.TryWriteBytes(span.Slice(20, 4), velocity.Y);
        BitConverter.TryWriteBytes(span.Slice(24, 4), velocity.Z);
        
        _transport.SendUnreliable(_positionBuffer);
    }
}
```

### 2. Game State Synchronization (10Hz)

```csharp
public class GameStateSystem
{
    private readonly IGranvilleRpcClient _rpcClient;
    private readonly MemoryStream _stateStream = new();
    private readonly BinaryWriter _writer;
    
    public GameStateSystem(IGranvilleRpcClient rpcClient)
    {
        _rpcClient = rpcClient;
        _writer = new BinaryWriter(_stateStream);
    }
    
    public async Task SyncGameState(GameState state)
    {
        // Reuse stream - no allocation
        _stateStream.Position = 0;
        _stateStream.SetLength(0);
        
        SerializeGameState(state, _writer);
        var stateBytes = _stateStream.ToArray(); // One allocation unavoidable here
        
        await _rpcClient.Bypass.SendReliableOrdered(stateBytes);
    }
}
```

### 3. Chat Messages (Event-Driven)

```csharp
public class ChatSystem
{
    private readonly IGranvilleRpcClient _rpcClient;
    private readonly StringBuilder _messageBuilder = new();
    
    public async Task SendChatMessage(string playerName, string message)
    {
        // Use full RPC for infrequent events - allocation acceptable
        await _rpcClient.InvokeAsync<IChatService>(
            service => service.BroadcastMessage(playerName, message));
    }
    
    public async Task SendFormattedMessage(string format, params object[] args)
    {
        // Reuse StringBuilder to minimize string allocations
        _messageBuilder.Clear();
        _messageBuilder.AppendFormat(format, args);
        var formattedMessage = _messageBuilder.ToString();
        
        await _rpcClient.InvokeAsync<IChatService>(
            service => service.BroadcastMessage("System", formattedMessage));
    }
}
```

## Optimization Techniques by Frequency

### Ultra-High Frequency (60Hz+) - Zero Allocation Goal

- Use direct transport access only
- Pre-allocate all buffers
- Use struct-based data types
- Avoid any dynamic allocation

```csharp
public unsafe struct PlayerUpdate
{
    public int PlayerId;
    public Vector3 Position;
    public Vector3 Velocity;
    public float Health;
    
    public static void Serialize(PlayerUpdate update, Span<byte> buffer)
    {
        fixed (byte* ptr = buffer)
        {
            *(PlayerUpdate*)ptr = update;
        }
    }
}
```

### High Frequency (10-30Hz) - Minimal Allocation

- Use RPC bypass API
- Pool temporary objects
- Batch when possible

```csharp
public class HighFrequencyUpdater
{
    private readonly IGranvilleRpcClient _client;
    private readonly ArrayPool<byte> _pool = ArrayPool<byte>.Shared;
    
    public async Task SendUpdate(GameUpdate update)
    {
        var buffer = _pool.Rent(1024);
        try
        {
            var bytesWritten = SerializeUpdate(update, buffer);
            await _client.Bypass.SendReliableOrdered(buffer.AsMemory(0, bytesWritten));
        }
        finally
        {
            _pool.Return(buffer);
        }
    }
}
```

### Medium Frequency (1-10Hz) - Controlled Allocation

- Use full RPC with optimized serialization
- Pool complex objects
- Monitor allocation patterns

```csharp
public class MediumFrequencyUpdater
{
    private readonly IGranvilleRpcClient _client;
    private readonly ObjectPool<ComplexData> _objectPool;
    
    public async Task ProcessComplexUpdate(ComplexData data)
    {
        // Use object pooling for complex types
        var pooledData = _objectPool.Get();
        try
        {
            CopyDataIntoPooledObject(data, pooledData);
            await _client.InvokeAsync<IGameService>(
                service => service.ProcessComplexUpdate(pooledData));
        }
        finally
        {
            _objectPool.Return(pooledData);
        }
    }
}
```

### Low Frequency (Event-Driven) - Allocation Acceptable

- Use full RPC with convenience APIs
- Focus on code clarity over allocation
- Use async/await patterns

```csharp
public class EventDrivenUpdater
{
    private readonly IGranvilleRpcClient _client;
    
    public async Task HandlePlayerJoined(Player player)
    {
        // Full RPC with convenient serialization - allocation OK for rare events
        await _client.InvokeAsync<IGameService>(
            service => service.OnPlayerJoined(player));
    }
}
```

## Memory Profiling and Monitoring

### 1. Built-in Diagnostics

```csharp
public class RpcMemoryMonitor
{
    private readonly ILogger<RpcMemoryMonitor> _logger;
    private long _lastAllocatedBytes;
    
    public void LogMemoryUsage()
    {
        var currentBytes = GC.GetTotalMemory(false);
        var allocated = currentBytes - _lastAllocatedBytes;
        
        _logger.LogInformation("RPC Memory: {CurrentMB} MB, Allocated: {AllocatedKB} KB", 
            currentBytes / 1024 / 1024, allocated / 1024);
        
        _lastAllocatedBytes = currentBytes;
    }
}
```

### 2. Performance Counters

```csharp
public class RpcPerformanceCounters
{
    private readonly Counter<long> _allocationsCounter;
    private readonly Counter<long> _gcCounter;
    
    public RpcPerformanceCounters(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("Granville.Rpc.Memory");
        _allocationsCounter = meter.CreateCounter<long>("rpc_allocations");
        _gcCounter = meter.CreateCounter<long>("rpc_gc_collections");
    }
    
    public void RecordAllocation(long bytes)
    {
        _allocationsCounter.Add(bytes);
    }
    
    public void RecordGC()
    {
        _gcCounter.Add(1);
    }
}
```

### 3. Custom Memory Profiler

```csharp
public class RpcMemoryProfiler
{
    private readonly Dictionary<string, long> _allocationsByOperation = new();
    
    public IDisposable TrackOperation(string operationName)
    {
        var startBytes = GC.GetTotalMemory(false);
        return new AllocationTracker(operationName, startBytes, _allocationsByOperation);
    }
    
    public void LogAllocationReport()
    {
        foreach (var kvp in _allocationsByOperation.OrderByDescending(x => x.Value))
        {
            Console.WriteLine($"{kvp.Key}: {kvp.Value} bytes");
        }
    }
    
    private class AllocationTracker : IDisposable
    {
        private readonly string _operationName;
        private readonly long _startBytes;
        private readonly Dictionary<string, long> _tracking;
        
        public AllocationTracker(string operationName, long startBytes, Dictionary<string, long> tracking)
        {
            _operationName = operationName;
            _startBytes = startBytes;
            _tracking = tracking;
        }
        
        public void Dispose()
        {
            var endBytes = GC.GetTotalMemory(false);
            var allocated = endBytes - _startBytes;
            _tracking[_operationName] = _tracking.GetValueOrDefault(_operationName) + allocated;
        }
    }
}
```

## Best Practices Summary

### Do's
- ✅ Use direct transport for high-frequency updates
- ✅ Pre-allocate buffers and reuse them
- ✅ Use `ArrayPool<byte>` for temporary allocations
- ✅ Batch multiple operations when possible
- ✅ Use span-based APIs to avoid intermediate allocations
- ✅ Profile memory usage regularly
- ✅ Choose appropriate abstraction level based on frequency

### Don'ts
- ❌ Don't use full RPC for 60Hz updates
- ❌ Don't allocate new arrays in hot paths
- ❌ Don't use string concatenation in loops
- ❌ Don't box value types unnecessarily
- ❌ Don't ignore GC pressure warnings
- ❌ Don't optimize prematurely without profiling

### Frequency-Based Decision Matrix

| Update Frequency | Recommended Approach | Allocation Target | Example Use Case |
|------------------|---------------------|-------------------|------------------|
| 60Hz+ | Direct Transport | 0 bytes | Player movement |
| 10-30Hz | Bypass API | <100 bytes | Game state sync |
| 1-10Hz | Full RPC | <1KB | Inventory updates |
| Event-driven | Full RPC | Any | Chat messages |

## Migration Strategy

### Phase 1: Identify Hot Paths
1. Profile your application to identify high-frequency operations
2. Measure current allocation patterns
3. Prioritize optimization targets

### Phase 2: Optimize Critical Paths
1. Replace high-frequency RPC calls with direct transport
2. Implement buffer pooling for critical operations
3. Add batching for burst operations

### Phase 3: Monitor and Iterate
1. Set up continuous memory monitoring
2. Establish allocation budgets for different operation types
3. Regular performance reviews and optimization

---

*Last updated: July 16, 2025*  
*Compatible with: Granville RPC v1.0+*