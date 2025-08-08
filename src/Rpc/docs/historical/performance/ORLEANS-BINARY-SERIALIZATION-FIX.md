# Orleans Binary Serialization Fix for Granville RPC

## Issue

Granville RPC message classes are already configured for Orleans binary serialization with `[GenerateSerializer]` and `[Id]` attributes, but the implementation uses JSON serialization instead. This causes massive allocation overhead (5-40x multiplier).

## Current State

```csharp
// RpcMessage.cs - Already has Orleans attributes!
[GenerateSerializer]
public abstract class RpcMessage
{
    [Id(0)]
    public Guid MessageId { get; set; } = Guid.NewGuid();
    
    [Id(1)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

// But RpcMessageSerializer.cs uses JSON:
var json = JsonSerializer.Serialize(message, message.GetType(), _jsonOptions);
var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
```

## Proposed Fix

Replace JSON serialization with Orleans binary serialization in `RpcMessageSerializer.cs`:

```csharp
using System;
using System.Buffers;
using Microsoft.Extensions.Logging;
using Orleans.Serialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Session;

namespace Granville.Rpc.Protocol
{
    /// <summary>
    /// Handles serialization and deserialization of RPC messages using Orleans binary serialization.
    /// </summary>
    public class RpcMessageSerializer
    {
        private readonly Serializer _serializer;
        private readonly ILogger<RpcMessageSerializer> _logger;
        private readonly SerializerSessionPool _sessionPool;

        public RpcMessageSerializer(Serializer serializer, ILogger<RpcMessageSerializer> logger)
        {
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sessionPool = new SerializerSessionPool();
        }

        /// <summary>
        /// Serializes an RPC message to bytes using Orleans binary serialization.
        /// </summary>
        public byte[] SerializeMessage(RpcMessage message)
        {
            try
            {
                var writer = new ArrayBufferWriter<byte>();
                
                // Write message type byte
                writer.Write(new[] { GetMessageTypeByte(message) });
                
                // Use Orleans binary serialization
                using var session = _sessionPool.GetSession();
                var writerBuffer = new Writer<ArrayBufferWriter<byte>>(writer, session);
                _serializer.Serialize(message, ref writerBuffer);
                
                return writer.WrittenMemory.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to serialize RPC message of type {Type}", message.GetType().Name);
                throw;
            }
        }

        /// <summary>
        /// Deserializes an RPC message from bytes using Orleans binary serialization.
        /// </summary>
        public RpcMessage DeserializeMessage(ReadOnlyMemory<byte> data)
        {
            try
            {
                if (data.Length < 1)
                {
                    throw new ArgumentException("Message data is too short");
                }

                var messageType = data.Span[0];
                var messageData = data.Slice(1);
                
                // Use Orleans binary deserialization
                using var session = _sessionPool.GetSession();
                var reader = Reader.Create(messageData, session);
                
                return messageType switch
                {
                    1 => _serializer.Deserialize<RpcRequest>(ref reader),
                    2 => _serializer.Deserialize<RpcResponse>(ref reader),
                    3 => _serializer.Deserialize<RpcHeartbeat>(ref reader),
                    4 => _serializer.Deserialize<RpcHandshake>(ref reader),
                    5 => _serializer.Deserialize<RpcHandshakeAck>(ref reader),
                    6 => _serializer.Deserialize<RpcAsyncEnumerableRequest>(ref reader),
                    7 => _serializer.Deserialize<RpcAsyncEnumerableItem>(ref reader),
                    8 => _serializer.Deserialize<RpcAsyncEnumerableCancel>(ref reader),
                    _ => throw new NotSupportedException($"Unknown message type: {messageType}")
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize RPC message");
                throw;
            }
        }

        private byte GetMessageTypeByte(RpcMessage message)
        {
            return message switch
            {
                RpcRequest => 1,
                RpcResponse => 2,
                RpcHeartbeat => 3,
                RpcHandshake => 4,
                RpcHandshakeAck => 5,
                RpcAsyncEnumerableRequest => 6,
                RpcAsyncEnumerableItem => 7,
                RpcAsyncEnumerableCancel => 8,
                _ => throw new NotSupportedException($"Unknown message type: {message.GetType()}")
            };
        }
    }
}
```

## Additional Optimizations

### 1. Pool ArrayBufferWriter Instances

```csharp
private readonly ObjectPool<ArrayBufferWriter<byte>> _writerPool = 
    new DefaultObjectPool<ArrayBufferWriter<byte>>(
        new DefaultPooledObjectPolicy<ArrayBufferWriter<byte>>());

public byte[] SerializeMessage(RpcMessage message)
{
    var writer = _writerPool.Get();
    try
    {
        writer.Clear();
        // ... serialization code ...
        return writer.WrittenMemory.ToArray();
    }
    finally
    {
        _writerPool.Return(writer);
    }
}
```

### 2. Use ArrayPool for Final Buffer

```csharp
public void SerializeMessage(RpcMessage message, IBufferWriter<byte> output)
{
    // Write message type
    output.Write(new[] { GetMessageTypeByte(message) });
    
    // Serialize directly to output buffer
    using var session = _sessionPool.GetSession();
    var writer = new Writer<IBufferWriter<byte>>(output, session);
    _serializer.Serialize(message, ref writer);
}
```

### 3. Fix Argument Serialization

Also need to fix the argument serialization in other files:

**RpcServer.cs** (around line 400):
```csharp
// Instead of:
var serializedArgs = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(args);

// Use:
using var session = _sessionPool.GetSession();
var writer = new ArrayBufferWriter<byte>();
var writerBuffer = new Writer<ArrayBufferWriter<byte>>(writer, session);
_serializer.Serialize(args, ref writerBuffer);
var serializedArgs = writer.WrittenMemory.ToArray();
```

**OutsideRpcRuntimeClient.cs** (around line 256):
```csharp
// Instead of:
var serializedArgs = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(args);

// Use Orleans serialization
```

## Expected Results

Based on Orleans' efficient binary serialization:

1. **Reduced Allocations**: 
   - Eliminate string intermediate (saves ~2x payload size)
   - No UTF8 encoding step (saves array allocation)
   - More compact binary format (saves 30-70% in size)

2. **Performance Improvements**:
   - Faster serialization/deserialization
   - Lower GC pressure
   - Better cache utilization

3. **Memory Savings**:
   - Small payload: From 1,250 KB to ~150 KB (90%+ reduction)
   - Medium payload: From 5,117 KB to ~500 KB (90%+ reduction)
   - Large payload: From 10,023 KB to ~10,100 KB (minimal overhead)

## Implementation Notes

1. **Backward Compatibility**: This change breaks wire format compatibility. Need versioning strategy.

2. **Testing**: Ensure all message types serialize/deserialize correctly with Orleans serializer.

3. **Session Pooling**: Orleans provides `SerializerSessionPool` for efficient session reuse.

4. **Further Optimization**: Consider using `IBufferWriter<byte>` throughout to avoid final `ToArray()` call.

## Migration Strategy

1. **Phase 1**: Add configuration option to choose serializer
   ```csharp
   public class RpcTransportOptions
   {
       public SerializationFormat SerializationFormat { get; set; } = SerializationFormat.Json;
   }
   ```

2. **Phase 2**: Default new deployments to binary, keep JSON for compatibility

3. **Phase 3**: Deprecate JSON support after migration period

This single change should provide the 90% allocation reduction mentioned in the performance analysis, without needing to implement a custom binary serializer!