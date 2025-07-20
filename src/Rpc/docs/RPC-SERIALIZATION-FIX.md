# RPC Serialization Fix - Isolated Session Implementation

## Summary

Fixed RPC argument deserialization issue where clients were sending valid string arguments but servers were receiving `null` values. The root cause was reference-based serialization across independent Orleans runtimes, which has been resolved by implementing isolated serialization sessions.

## Problem

- **Symptom**: `ConnectPlayer` RPC calls failed with "null or empty playerId" despite clients sending valid GUIDs
- **Root Cause**: Orleans serialized strings as 6-byte references (`200003C101E0`) that couldn't be resolved across independent runtimes
- **Impact**: All RPC calls with object arguments failed in cross-runtime scenarios

## Solution

Implemented **isolated serialization sessions** that ensure value-based serialization:

1. Created `RpcSerializationSessionFactory` to manage isolated session lifecycle
2. Updated client-side serialization to use fresh sessions per RPC call
3. Updated server-side deserialization to use fresh sessions per RPC call
4. Integrated with Orleans DI for proper service resolution

## Technical Details

### Key Components

- `RpcSerializationSessionFactory`: Factory for creating isolated Orleans serialization sessions
- Modified `OutsideRpcRuntimeClient.SerializeArguments()`: Uses isolated sessions for serialization
- Modified `RpcConnection.DeserializeArguments()`: Uses isolated sessions for deserialization

### Implementation Highlights

```csharp
// Client-side serialization with isolated session
public byte[] SerializeArgumentsWithIsolatedSession(Serializer serializer, object[] args)
{
    using var session = CreateClientSession();
    var writer = new ArrayBufferWriter<byte>();
    serializer.Serialize(args, writer, session);
    return writer.WrittenMemory.ToArray();
}

// Server-side deserialization with isolated session
public T DeserializeWithIsolatedSession<T>(Serializer serializer, ReadOnlyMemory<byte> data)
{
    using var session = CreateServerSession();
    return serializer.Deserialize<T>(data, session);
}
```

## Results

- **Before**: 6-byte reference → `null` (deserialization failure)
- **After**: 43-byte value → Valid string (deserialization success)
- **Test Status**: ✅ All tests pass with isolated session implementation

## Files Changed

- `/src/Rpc/Orleans.Rpc.Client/RpcSerializationSessionFactory.cs` (new)
- `/src/Rpc/Orleans.Rpc.Server/RpcSerializationSessionFactory.cs` (new)
- `/src/Rpc/Orleans.Rpc.Client/OutsideRpcRuntimeClient.cs` (modified)
- `/src/Rpc/Orleans.Rpc.Server/RpcConnection.cs` (modified)
- `/src/Rpc/Orleans.Rpc.Client/Hosting/DefaultRpcClientServices.cs` (modified)
- `/src/Rpc/Orleans.Rpc.Server/Hosting/DefaultRpcServerServices.cs` (modified)

## Documentation

For detailed analysis and implementation strategy, see:
- `/src/Rpc/docs/serialization/sessions/RPC-SERIALIZATION-SESSION-ANALYSIS.md`
- `/src/Rpc/docs/serialization/sessions/MINIMAL-SESSION-CONTEXT-STRATEGY.md`