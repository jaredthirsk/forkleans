# Vector2 Serialization Fix for RPC Framework

## Problem
The Granville RPC framework was unable to serialize custom structs like `Vector2` when using the secure binary serialization path. This caused the following error:
```
Failed to deserialize arguments: Failed to deserialize RPC arguments: A WireType value of TagDelimited is expected by this codec.
```

## Root Cause
The RPC framework's secure binary serialization only supported primitive types (string, int, bool, double, etc.) and would fall back to Orleans binary serialization for complex types. However, Orleans binary serialization expects specific wire format markers that weren't compatible with the RPC protocol.

## Solution
Extended the RPC framework's secure binary serialization to support `Vector2` types by:

1. **Client-side (Orleans.Rpc.Client/RpcSerializationSessionFactory.cs)**:
   - Modified `IsSimpleType()` to recognize `Vector2` and `Vector2?` types
   - Added serialization logic with type markers 8 (Vector2) and 9 (Nullable<Vector2>)
   - Used reflection to extract X and Y properties from Vector2 instances

2. **Server-side (Orleans.Rpc.Server/RpcSerializationSessionFactory.cs)**:
   - Added deserialization cases for type markers 8 and 9
   - Implemented `DeserializeVector2()` and `DeserializeNullableVector2()` methods
   - Used reflection to construct Vector2 instances with the deserialized X and Y values

## Implementation Details

### Type Markers
- `0x00`: Orleans binary marker
- `0xFE`: Secure binary marker
- `0xFF`: Legacy JSON marker (deprecated)
- Within secure binary:
  - `0`: null
  - `1`: string
  - `2`: Guid
  - `3`: int
  - `4`: bool
  - `5`: double
  - `6`: DateTime
  - `7`: decimal
  - `8`: Vector2 (new)
  - `9`: Nullable<Vector2> (new)

### Binary Format for Vector2
```
[byte: 8]     // Type marker
[float: X]    // 4 bytes - X coordinate
[float: Y]    // 4 bytes - Y coordinate
```

### Binary Format for Nullable<Vector2>
```
[byte: 9]           // Type marker
[bool: hasValue]    // 1 byte - whether the nullable has a value
[float: X]          // 4 bytes - X coordinate (only if hasValue is true)
[float: Y]          // 4 bytes - Y coordinate (only if hasValue is true)
```

## Testing
1. Rebuilt RPC client and server libraries
2. Rebuilt Shooter sample projects
3. Created test script: `test-vector2-serialization.ps1`
4. Verified that player movement and shooting work correctly with Vector2 parameters

## Benefits
- Maintains secure, efficient binary serialization for Vector2 types
- Avoids Orleans wire format compatibility issues
- Supports both nullable and non-nullable Vector2 types
- Uses reflection to work with any assembly containing the Vector2 type

## Future Improvements
- Consider using Orleans-generated codecs instead of reflection for better performance
- Add support for other common game types (Vector3, Quaternion, etc.)
- Cache reflection results to improve performance