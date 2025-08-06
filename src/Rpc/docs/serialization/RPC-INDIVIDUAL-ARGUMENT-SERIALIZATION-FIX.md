# RPC Individual Argument Serialization Fix

## Problem Summary

When using Orleans-generated proxies with RPC transport, method arguments were being serialized as references instead of values, causing the server to receive null values for string arguments. This occurred because Orleans' `StringCodec` always attempts to use reference tracking if a string was previously seen in the serialization session.

## Root Cause

Orleans' `StringCodec.WriteField()` method always calls `ReferenceCodec.TryWriteReferenceFieldExpected()` first, which checks if the string exists in the session's `ReferencedObjects` collection. If found, it writes a reference (typically 2-7 bytes) instead of the full value. This is problematic for RPC scenarios where client and server have independent Orleans runtimes with no shared session context.

## Solution: Individual Argument Sessions

The fix serializes each method argument with its own isolated `SerializerSession` to prevent cross-argument reference sharing. This ensures each argument is self-contained with value-based serialization.

### Implementation Details

1. **Custom Wire Format** (marker 0xFF):
   ```
   [0xFF][count:4][length1:4][data1][length2:4][data2]...
   
   - marker: 0xFF (identifies custom RPC format, not 0x00 which is Orleans binary)
   - count: Number of arguments (4 bytes, big-endian)
   - lengthN: Length of argument N data (4 bytes, big-endian)
   - dataN: Orleans-serialized argument N
   ```

2. **Client-Side Serialization** (`RpcSerializationSessionFactory.SerializeArgumentsWithIsolatedSession`):
   - Creates a fresh session for each argument
   - Serializes each argument independently
   - Combines into custom format with 0xFF marker

3. **Server-Side Deserialization** (`RpcSerializationSessionFactory.DeserializeWithIsolatedSession`):
   - Detects 0xFF marker for custom format
   - Extracts each argument segment
   - Deserializes each with fresh session

### Code Changes

**File: `src/Rpc/Orleans.Rpc.Client/RpcSerializationSessionFactory.cs`**
- Added `SerializeArgumentsWithIsolatedSession()` method
- Modified `DeserializeWithIsolatedSession()` to handle custom format

**File: `src/Rpc/Orleans.Rpc.Server/RpcSerializationSessionFactory.cs`**
- Modified `DeserializeWithIsolatedSession()` to handle custom format

**File: `src/Rpc/Orleans.Rpc.Client/OutsideRpcClient.cs`**
- Updated `InvokeRpcMethodAsync()` to use `SerializeArgumentsWithIsolatedSession()`

## Testing Results

Before fix:
```
[Error] ConnectPlayer called with null or empty playerId
[Information] RPC ConnectPlayer returned: FAILED
```

After fix:
```
[Information] ConnectPlayer returning True for player e46784dc-a071-429c-be27-d4b75939a099
[Information] RPC ConnectPlayer returned: SUCCESS
```

## Performance Impact

- **Overhead**: Creating multiple sessions adds minimal overhead (microseconds)
- **Wire Size**: Slightly larger due to length prefixes (4 bytes per argument)
- **Trade-off**: Reliability and correctness outweigh small performance cost

## Backward Compatibility

The solution maintains backward compatibility:
- Orleans binary format (marker 0x00) still supported
- Custom format (marker 0xFF) used only for RPC argument arrays
- Non-RPC serialization unaffected

## Future Considerations

1. Could optimize for primitive types that don't use references
2. Could use varint encoding for lengths to save bytes
3. Could investigate Orleans configuration to disable reference tracking globally for RPC sessions