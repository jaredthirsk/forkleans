# Orleans Marker Byte Deserialization Fix

## Problem

After rebuild, the client couldn't join the game with the following error:
```
Failed to deserialize arguments: Failed to deserialize RPC arguments: A WireType value of TagDelimited is expected by this codec. [VarInt, IdDelta:0, SchemaType:Expected]
```

The server logs showed it was trying to deserialize only 7 bytes when more data was expected.

## Root Cause

The client-side `SerializeArgumentsWithIsolatedSession` was adding an Orleans binary marker byte (0x00) and then appending the Orleans-serialized data:

```csharp
// Client serialization
var result = writer.WrittenMemory.ToArray();  // Orleans serialized data
var finalResult = new byte[result.Length + 1];
finalResult[0] = 0x00; // Orleans binary marker
Array.Copy(result, 0, finalResult, 1, result.Length);
```

However, the server-side `DeserializeWithIsolatedSession` was passing the entire data (including the marker) to the Orleans deserializer:

```csharp
// Server deserialization (before fix)
if (marker == 0x00) // Orleans binary marker
{
    var result = serializer.Deserialize<T>(data, session); // WRONG: includes marker byte
}
```

Orleans doesn't expect the marker byte - it expects only the serialized payload.

## Solution

Modified the server-side deserialization to skip the marker byte before passing data to Orleans:

```csharp
// Server deserialization (after fix)
if (marker == 0x00) // Orleans binary marker
{
    // Skip the marker byte - Orleans deserializer expects the raw serialized data without the marker
    var orleansData = data.Slice(1);
    var result = serializer.Deserialize<T>(orleansData, session); // CORRECT: marker skipped
}
```

## Impact

This fix ensures that:
1. The Orleans deserializer receives the correct data format it expects
2. RPC calls with Orleans binary serialization work correctly
3. Client can successfully connect to the game server

## Technical Details

The marker byte (0x00) is used to identify the serialization format:
- `0x00` - Orleans binary serialization
- `0xFE` - Secure binary serialization (for simple types)
- `0xFF` - JSON serialization (deprecated)

The marker must be stripped before passing data to the appropriate deserializer.