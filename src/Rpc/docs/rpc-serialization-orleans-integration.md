# RPC Serialization - Orleans Integration

## Overview

The Granville RPC framework has been updated to exclusively use Orleans serialization for all types. This ensures that any custom types marked with `[GenerateSerializer]` will work automatically without requiring manual serialization code.

## Previous Approach (Removed)

The RPC framework previously used a hybrid approach:
- "Secure binary" serialization for primitive types (string, int, bool, etc.)
- Custom hardcoded serialization for specific types like Vector2
- Orleans binary serialization as a fallback

This approach had several limitations:
- Required manual updates to support new custom types
- Duplicated serialization logic already handled by Orleans
- Increased maintenance burden

## Current Approach

The RPC framework now:
1. **Always uses Orleans binary serialization** for all argument types
2. **Leverages Orleans-generated serializers** for custom types marked with `[GenerateSerializer]`
3. **Maintains isolated serialization sessions** to ensure cross-runtime compatibility

### Key Changes

#### Client-side (`Orleans.Rpc.Client/RpcSerializationSessionFactory.cs`)
```csharp
public byte[] SerializeArgumentsWithIsolatedSession(Serializer serializer, object[] args)
{
    // Always use Orleans binary serialization with isolated session
    using var session = CreateClientSession();
    
    var writer = new System.Buffers.ArrayBufferWriter<byte>();
    serializer.Serialize(args, writer, session);
    var result = writer.WrittenMemory.ToArray();
    
    // Wrap with Orleans binary marker
    var finalResult = new byte[result.Length + 1];
    finalResult[0] = 0x00; // Orleans binary marker
    Array.Copy(result, 0, finalResult, 1, result.Length);
    
    return finalResult;
}
```

#### Server-side (`Orleans.Rpc.Server/RpcSerializationSessionFactory.cs`)
```csharp
public T DeserializeWithIsolatedSession<T>(Serializer serializer, ReadOnlyMemory<byte> data)
{
    // Check for Orleans binary marker and deserialize
    if (marker == 0x00) // Orleans binary marker
    {
        using var session = CreateServerSession();
        var result = serializer.Deserialize<T>(data, session);
        return result;
    }
}
```

## Benefits

1. **Automatic support for custom types**: Any type with `[GenerateSerializer]` works without RPC framework changes
2. **Consistent serialization**: Uses the same serialization mechanism as Orleans grains
3. **Better performance**: Orleans-generated serializers are optimized for each type
4. **Reduced maintenance**: No need to update RPC framework for new types
5. **Type safety**: Orleans handles versioning and type compatibility

## Example Usage

To make a custom type work with RPC:

```csharp
[Orleans.GenerateSerializer]
public record struct Vector2(
    [property: Id(0)] float X, 
    [property: Id(1)] float Y);

[Orleans.GenerateSerializer]
public class PlayerState
{
    [Id(0)] public string PlayerId { get; set; }
    [Id(1)] public Vector2 Position { get; set; }
    [Id(2)] public float Health { get; set; }
}

// These types will automatically work in RPC methods:
[RpcMethod]
Task UpdatePlayerPosition(string playerId, Vector2 position);

[RpcMethod]
Task<PlayerState> GetPlayerState(string playerId);
```

## Migration Guide

If you have existing RPC code:
1. Ensure all custom types have `[GenerateSerializer]` attribute
2. Add `[Id(n)]` attributes to properties/fields for versioning
3. Remove any custom serialization code for RPC
4. The RPC framework will handle everything else automatically

## Technical Details

- Orleans binary format includes type information and versioning
- Isolated serialization sessions prevent cross-runtime reference conflicts
- The 0x00 marker byte identifies Orleans binary format in the RPC protocol
- All Orleans serialization features (polymorphism, collections, etc.) are supported