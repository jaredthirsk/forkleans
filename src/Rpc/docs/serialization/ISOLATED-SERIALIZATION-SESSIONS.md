# Isolated Serialization Sessions for RPC

## Overview

Granville RPC uses **isolated serialization sessions** to ensure proper value-based serialization across independent Orleans runtimes. This approach solves a fundamental issue when using Orleans serialization between separate client and server processes that don't share the same Orleans runtime.

## The Problem

### Reference-Based Serialization Issue

Orleans' default binary serialization is optimized for communication within a single cluster where all participants share:
- The same serializer instance
- The same session context
- The same type manifest
- The same reference tracking system

When serializing data, Orleans may use **reference-based serialization** for efficiency:
```
// Example: String "player123" might be serialized as:
// First occurrence: Full value (e.g., "player123")
// Subsequent occurrences: Reference ID (e.g., 0x03)
```

This works perfectly within a single Orleans cluster but fails across independent runtimes because:
1. The server doesn't have the client's serialization session
2. Reference IDs are meaningless across process boundaries
3. String interning and object references aren't shared

### Symptoms of the Problem

When this issue occurs, you'll see:
- Null values where strings were expected
- Missing or corrupted data after deserialization
- Errors like "RPC: ConnectPlayer called with null or empty playerId"
- Hexadecimal reference values (e.g., `200003C101E0`) instead of actual data

## The Solution: Isolated Sessions

### What Are Isolated Sessions?

Isolated sessions ensure that each serialization operation:
1. Uses a fresh, independent session context
2. Forces value-based serialization (no references)
3. Produces self-contained byte arrays
4. Can be deserialized without external context

### Implementation

The solution is implemented in `RpcSerializationSessionFactory`:

```csharp
public class RpcSerializationSessionFactory
{
    public byte[] SerializeArgumentsWithIsolatedSession<T>(Serializer serializer, T value)
    {
        // Create a new session pool for this operation
        var sessionPool = new SerializerSessionPool(serializer);
        
        using (var session = sessionPool.GetSession())
        {
            var writer = BufferWriter.CreatePooled(session);
            try
            {
                serializer.Serialize(value, ref writer);
                return writer.ToArray();
            }
            finally
            {
                writer.Dispose();
            }
        }
    }
    
    public T DeserializeWithIsolatedSession<T>(Serializer serializer, byte[] data)
    {
        // Create a new session pool for this operation
        var sessionPool = new SerializerSessionPool(serializer);
        
        using (var session = sessionPool.GetSession())
        {
            var reader = BufferReader.Create(data, session);
            return serializer.Deserialize<T>(ref reader);
        }
    }
}
```

### Key Principles

1. **Session Isolation**: Each RPC call gets its own serialization session
2. **No Shared State**: Sessions are not reused between calls
3. **Value Semantics**: All data is serialized by value, not by reference
4. **Self-Contained Messages**: Each message contains all necessary data

## Architecture Impact

### Client Side (OutsideRpcRuntimeClient)

```csharp
private byte[] SerializeArguments(object[] args)
{
    // Use isolated session for value-based serialization
    var result = _sessionFactory.SerializeArgumentsWithIsolatedSession(_serializer, args);
    
    if (_logger.IsEnabled(LogLevel.Debug))
    {
        _logger.LogDebug("[RPC_CLIENT] Serialized {Count} arguments into {Size} bytes", 
            args.Length, result.Length);
    }
    
    return result;
}
```

### Server Side (RpcConnection)

```csharp
private object[] DeserializeArguments(byte[] serializedArguments)
{
    try
    {
        // Use isolated session for value-based deserialization
        var result = _sessionFactory.DeserializeWithIsolatedSession<object[]>(
            _serializer, serializedArguments);
        
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("[RPC_SERVER] Deserialized {Count} arguments", 
                result.Length);
        }
        
        return result;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "[RPC_SERVER] Failed to deserialize arguments");
        throw;
    }
}
```

## Benefits

1. **Reliability**: Guarantees data integrity across process boundaries
2. **Independence**: No coupling between client and server Orleans runtimes
3. **Predictability**: Consistent behavior regardless of serialization history
4. **Debugging**: Easier to inspect and debug serialized data

## Performance Considerations

While isolated sessions have a slight performance overhead compared to reference-based serialization:

1. **Trade-off**: Reliability over micro-optimization
2. **Acceptable Cost**: The overhead is minimal for RPC scenarios
3. **Network Bound**: RPC is typically network-bound, not serialization-bound
4. **Caching**: Consider caching serialized data for repeated calls

## Testing

To verify isolated sessions are working correctly:

```csharp
[Fact]
public void TestIsolatedSessionSerialization()
{
    var factory = new RpcSerializationSessionFactory();
    var serializer = serviceProvider.GetRequiredService<Serializer>();
    
    // Test with repeated values
    var args = new object[] { "player123", "player123", "player123" };
    
    // Serialize
    var bytes = factory.SerializeArgumentsWithIsolatedSession(serializer, args);
    
    // Deserialize in a completely different context
    var result = factory.DeserializeWithIsolatedSession<object[]>(serializer, bytes);
    
    // All values should be properly deserialized
    Assert.Equal(3, result.Length);
    Assert.Equal("player123", result[0]);
    Assert.Equal("player123", result[1]);
    Assert.Equal("player123", result[2]);
}
```

## Migration Guide

If you're experiencing serialization issues in your RPC implementation:

1. **Identify**: Look for null values or corrupted data after RPC calls
2. **Update**: Ensure you're using `RpcSerializationSessionFactory`
3. **Inject**: Register the factory in your DI container
4. **Replace**: Update serialization calls to use the factory methods
5. **Test**: Verify data integrity across process boundaries

## Related Documentation

- [RPC Serialization Session Analysis](sessions/RPC-SERIALIZATION-SESSION-ANALYSIS.md)
- [String Serialization Issue](STRING-SERIALIZATION-ISSUE.md)
- [Orleans Binary Serialization Fix](../performance/ORLEANS-BINARY-SERIALIZATION-FIX.md)
- [Serialization Fixes Summary](SERIALIZATION-FIXES-SUMMARY.md)