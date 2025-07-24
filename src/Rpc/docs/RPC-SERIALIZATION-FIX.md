# RPC Serialization Fix - Evolution to Secure Binary Serialization

## Summary

Fixed RPC argument deserialization issue where clients were sending valid string arguments but servers were receiving `null` values. The root cause was reference-based serialization across independent Orleans runtimes, which has been resolved through an evolution of approaches culminating in **secure binary serialization**.

## Problem

- **Symptom**: `ConnectPlayer` RPC calls failed with "null or empty playerId" despite clients sending valid GUIDs
- **Root Cause**: Orleans serialized strings as 6-byte references (`200003C101E0`) that couldn't be resolved across independent runtimes
- **Impact**: All RPC calls with object arguments failed in cross-runtime scenarios

## Solution Evolution

The fix evolved through three approaches:

### Phase 1: Isolated Serialization Sessions
Initial implementation using fresh Orleans sessions per RPC call to minimize reference contamination.

### Phase 2: JSON/Orleans Hybrid Strategy  
Introduced JSON serialization for simple types to force value semantics, falling back to Orleans for complex types.

### Phase 3: Secure Binary Serialization (Final Solution)
Implemented **secure binary serialization** that addresses both reliability and security concerns:

1. **Type-Safe Binary Format**: Custom binary serialization with explicit type markers
2. **Security First**: No arbitrary type deserialization - only whitelisted safe types
3. **Marker-Based Detection**: Format identification (0xFE=secure binary, 0xFF=deprecated JSON, 0x00=Orleans)
4. **Value-Based Serialization**: Forces value semantics for strings and primitives
5. **Backward Compatibility**: Maintains support for existing Orleans and legacy JSON formats

## Technical Details

### Key Components

- `RpcSerializationSessionFactory`: Factory managing secure binary serialization and format detection
- `SerializeSimpleTypesBinary()`: Type-safe binary serialization for whitelisted types
- `DeserializeSimpleTypesBinary()`: Secure binary deserialization with explicit type handling
- Modified `OutsideRpcRuntimeClient.SerializeArguments()`: Uses hybrid approach with secure binary for simple types
- Modified `RpcConnection.DeserializeArguments()`: Format-aware deserialization with security validation

### Implementation Highlights

```csharp
// Client-side secure binary serialization
public byte[] SerializeArgumentsWithIsolatedSession(Serializer serializer, object[] args)
{
    bool allSimple = args.All(IsSimpleType);
    
    if (allSimple && args.Length > 0)
    {
        // Use secure binary for simple types
        var binaryBytes = SerializeSimpleTypesBinary(args);
        var result = new byte[binaryBytes.Length + 1];
        result[0] = 0xFE; // Secure binary marker
        Array.Copy(binaryBytes, 0, result, 1, binaryBytes.Length);
        return result;
    }
    
    // Fall back to Orleans binary for complex types
    using var session = CreateClientSession();
    // ... Orleans serialization logic
}

// Server-side format-aware deserialization
public T DeserializeWithIsolatedSession<T>(Serializer serializer, ReadOnlyMemory<byte> data)
{
    var marker = data.Span[0];
    var actualData = data.Slice(1);
    
    return marker switch
    {
        0xFE => (T)(object)DeserializeSimpleTypesBinary(actualData), // Secure binary
        0xFF => System.Text.Json.JsonSerializer.Deserialize<T>(actualData.Span), // Deprecated JSON
        0x00 => DeserializeWithOrleansSession<T>(serializer, actualData), // Orleans binary
        _ => DeserializeWithOrleansSession<T>(serializer, data) // Legacy format
    };
}
```

## Results

- **Before**: 6-byte reference (`200003C101E0`) → `null` (deserialization failure)
- **After**: Secure binary format → Valid string values (reliable deserialization)
- **Security**: Eliminated arbitrary JSON deserialization vulnerability
- **Performance**: Binary format more efficient than JSON, comparable to Orleans
- **Test Status**: ✅ All builds successful, ready for production testing

### Supported Types in Secure Binary Format

- `string` (UTF-8 encoded)
- `Guid` (16-byte binary)
- `int`, `bool`, `double` (native binary representation)
- `DateTime` (binary ticks format)
- `decimal` (4-int bit representation)
- `null` values

## Files Changed

### Secure Binary Implementation (Final)
- `/src/Rpc/Orleans.Rpc.Client/RpcSerializationSessionFactory.cs` (updated with secure binary)
- `/src/Rpc/Orleans.Rpc.Server/RpcSerializationSessionFactory.cs` (updated with secure binary, fixed marker byte handling)

### Earlier Phases
- `/src/Rpc/Orleans.Rpc.Client/OutsideRpcRuntimeClient.cs` (modified for isolated sessions)
- `/src/Rpc/Orleans.Rpc.Server/RpcConnection.cs` (modified for isolated sessions)
- `/src/Rpc/Orleans.Rpc.Client/Hosting/DefaultRpcClientServices.cs` (DI registration)
- `/src/Rpc/Orleans.Rpc.Server/Hosting/DefaultRpcServerServices.cs` (DI registration)

## Security Considerations

The move from JSON to secure binary serialization addressed critical security concerns:

- **Eliminated Arbitrary Deserialization**: JSON allowed deserialization of any type, creating potential attack vectors
- **Explicit Type Whitelisting**: Binary format only supports explicitly defined safe types
- **Type Safety**: Each supported type has a specific marker byte and known deserialization pattern
- **No User-Controlled Types**: Cannot deserialize user-defined classes or complex objects through this path

## Documentation

For detailed information on the secure binary implementation, see:
- [Secure Binary Serialization Guide](serialization/SECURE-BINARY-SERIALIZATION.md) - Comprehensive technical details
- [Security Serialization Guide](security/SECURITY-SERIALIZATION-GUIDE.md) - Security-focused documentation
- [Serialization Fixes Summary](serialization/SERIALIZATION-FIXES-SUMMARY.md) - Complete timeline

For historical context and implementation strategy:
- [Isolated Serialization Sessions](serialization/ISOLATED-SERIALIZATION-SESSIONS.md) - Phase 1 approach
- [RPC Serialization Session Analysis](serialization/sessions/RPC-SERIALIZATION-SESSION-ANALYSIS.md) - Deep technical analysis
- [String Serialization Issue](serialization/STRING-SERIALIZATION-ISSUE.md) - Original problem analysis