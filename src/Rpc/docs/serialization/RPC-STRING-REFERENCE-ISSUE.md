# RPC String Reference Issue Analysis

## Problem
RPC client sends 7-byte reference (`00200003C101E0`) instead of full string value for playerId argument.

## Root Cause Investigation

### Test Results
Created test program to understand Orleans serialization behavior:

1. **Fresh session serializes full value**: String "c35a081a-b977-4bc6-8e7d-94a57f15f962" serializes to 38 bytes
2. **Reused session creates references**: Same string serialized twice in same session produces 2-byte reference on second serialization
3. **Object arrays work correctly**: `object[] { "string" }` serializes to 43 bytes with full value using fresh session

### The 7-Byte Pattern
The pattern `00200003C101E0` indicates:
- `00`: Orleans binary marker we add
- `20`: Array type marker
- `0003`: Array with single element  
- `C1`: Reference marker
- `01E0`: Reference ID

This confirms the string was already serialized earlier in the same session.

## Current Implementation

### RpcSerializationSessionFactory.SerializeArgumentsWithIsolatedSession
```csharp
public byte[] SerializeArgumentsWithIsolatedSession(Serializer serializer, object[] args)
{
    using var session = CreateClientSession();
    
    var writer = new System.Buffers.ArrayBufferWriter<byte>();
    serializer.Serialize(args, writer, session);
    var result = writer.WrittenMemory.ToArray();
    
    // Wrap with Orleans binary marker
    var finalResult = new byte[result.Length + 1];
    finalResult[0] = 0x00;
    Array.Copy(result, 0, finalResult, 1, result.Length);
    
    return finalResult;
}
```

### CreateClientSession
```csharp
public SerializerSession CreateClientSession()
{
    var session = new SerializerSession(_typeCodec, _wellKnownTypes, _codecProvider);
    return session;
}
```

## Hypothesis
The session is not truly isolated. Possible causes:
1. `_typeCodec`, `_wellKnownTypes`, or `_codecProvider` might contain shared state
2. The string might be registered as a well-known type
3. Orleans might be caching string references at a deeper level

## Next Steps
1. Check if the string is being serialized elsewhere before the arguments
2. Investigate if Orleans has a global string intern pool
3. Consider serializing arguments individually rather than as an array
4. Force value serialization by wrapping strings in a custom type