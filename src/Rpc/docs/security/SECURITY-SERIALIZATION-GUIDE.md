# Security Serialization Guide for Granville RPC

## Overview

This document provides security-focused guidance for RPC serialization in Granville, covering threat models, mitigation strategies, and secure development practices. It complements the technical implementation details in [Secure Binary Serialization](../serialization/SECURE-BINARY-SERIALIZATION.md).

## Threat Model

### Attack Vectors in Serialization

#### 1. Arbitrary Type Deserialization
**Threat**: Attacker controls serialized data to deserialize malicious types

**Example Attack**:
```json
{
  "$type": "System.Diagnostics.Process, System",
  "StartInfo": {
    "FileName": "cmd.exe", 
    "Arguments": "/c calc.exe"
  }
}
```

**Impact**: Remote code execution, privilege escalation, data exfiltration

#### 2. Gadget Chain Exploitation
**Threat**: Attacker chains together legitimate .NET types to achieve code execution

**Common Gadgets**:
- `ObjectDataProvider` + reflection
- `WindowsIdentity` + impersonation
- `ActivitySurrogateSelector` + serialization callbacks

**Impact**: Bypass security controls, execute arbitrary code

#### 3. Resource Exhaustion
**Threat**: Malicious payloads consume excessive memory/CPU

**Examples**:
- Extremely deep object graphs
- Large collection allocations
- Recursive data structures

**Impact**: Denial of service, system instability

#### 4. Data Injection
**Threat**: Malformed data exploits parsing vulnerabilities

**Examples**:
- Buffer overflows in native parsing
- Integer overflows in length fields
- Format string vulnerabilities

**Impact**: Memory corruption, code execution

## Granville RPC Security Architecture

### Defense in Depth Strategy

```
┌─────────────────────────────────────────┐
│           Application Layer             │
├─────────────────────────────────────────┤
│         Input Validation               │
├─────────────────────────────────────────┤
│      Type Safety Enforcement          │  ← Secure Binary Serialization
├─────────────────────────────────────────┤
│        Format Detection               │  ← Marker-based routing
├─────────────────────────────────────────┤
│      Transport Encryption             │
└─────────────────────────────────────────┘
```

### Security Properties

1. **Type Whitelisting**: Only explicitly allowed types can be deserialized
2. **Fail-Safe Design**: Unknown data throws exceptions rather than succeeding
3. **Format Isolation**: Different serialization formats are strictly separated
4. **Memory Safety**: Uses .NET's safe binary reading APIs
5. **Bounded Operations**: All operations have predictable resource consumption

## Secure Binary Serialization Design

### Security by Design Principles

#### 1. Explicit Type Control
```csharp
// SECURE: Explicit type handling
result[i] = typeMarker switch
{
    1 => reader.ReadString(),     // Only strings allowed
    2 => new Guid(bytes),         // Only GUIDs allowed
    3 => reader.ReadInt32(),      // Only ints allowed
    _ => throw new InvalidOperationException() // Fail on unknown
};
```

#### 2. No Polymorphism
```csharp
// INSECURE: Polymorphic deserialization
object obj = DeserializePolymorphic(data); // Could be anything!

// SECURE: Monomorphic deserialization  
string str = DeserializeString(data);      // Only strings
```

#### 3. Bounded Resource Usage
```csharp
// SECURE: Length validation before allocation
var length = reader.ReadInt32();
if (length > MAX_ARRAY_LENGTH || length < 0)
    throw new InvalidOperationException("Invalid array length");

var result = new object[length]; // Safe allocation
```

### Type Safety Enforcement

#### Supported Type Categories

**✅ Safe Types** (Supported):
- **Value Types**: `int`, `bool`, `double`, `decimal`
- **Immutable Types**: `string`, `DateTime`, `Guid`
- **Null Values**: Explicit null handling

**❌ Dangerous Types** (Prohibited):
- **Reference Types**: Custom classes, interfaces
- **Collections**: Arrays, lists, dictionaries
- **Delegates**: Function pointers, lambda expressions
- **System Types**: `Process`, `FileStream`, reflection types
- **Serialization Types**: `ISerializable` implementations

#### Type Marker Security

```csharp
// Each type has a unique, non-overlapping marker
private const byte NULL_MARKER = 0;
private const byte STRING_MARKER = 1;
private const byte GUID_MARKER = 2;
// ... explicit markers for each type

// No wildcards or ranges - every marker is explicit
```

## Format Detection Security

### Marker-Based Routing

```csharp
public T DeserializeWithIsolatedSession<T>(Serializer serializer, ReadOnlyMemory<byte> data)
{
    var marker = data.Span[0];
    
    return marker switch
    {
        0xFE => DeserializeSecureBinary<T>(data.Slice(1)),    // Most secure
        0xFF => DeserializeDeprecatedJson<T>(data.Slice(1)), // Legacy, warn
        0x00 => DeserializeOrleansBinary<T>(data.Slice(1)),  // Complex types only
        _ => DeserializeLegacyOrleans<T>(data)               // Backward compatibility
    };
}
```

### Security Properties of Markers

1. **Format Isolation**: Each format uses completely separate code paths
2. **Explicit Routing**: No ambiguity about which deserializer to use  
3. **Deprecation Support**: Can warn about insecure legacy formats
4. **Future Evolution**: Can add new secure formats without breaking existing code

## Secure Development Practices

### Code Review Checklist

When reviewing serialization code, verify:

#### ✅ Type Safety
- [ ] Only whitelisted types are deserialized
- [ ] No polymorphic deserialization
- [ ] All type markers are explicit and bounded
- [ ] Unknown types throw exceptions

#### ✅ Resource Safety  
- [ ] Array lengths are validated before allocation
- [ ] String lengths are bounded
- [ ] No recursive data structures
- [ ] Memory allocation is predictable

#### ✅ Input Validation
- [ ] All binary reads are bounds-checked
- [ ] Malformed data throws exceptions
- [ ] No assumption about data correctness
- [ ] Error handling is fail-safe

#### ✅ Format Security
- [ ] Secure binary format used for simple types
- [ ] JSON deserialization avoided or deprecated
- [ ] Orleans binary limited to complex trusted types
- [ ] Legacy formats handled securely

### Testing Security Properties

#### Unit Tests

```csharp
[Fact]
public void SecureBinary_RejectsArbitraryTypes()
{
    var factory = new RpcSerializationSessionFactory();
    
    // Attempt to serialize dangerous type
    var dangerousArgs = new object[] { new Process() };
    
    // Should throw, not serialize
    Assert.Throws<InvalidOperationException>(() =>
        factory.SerializeSimpleTypesBinary(dangerousArgs));
}

[Fact]
public void SecureBinary_RejectsUnknownMarkers()
{
    var factory = new RpcSerializationSessionFactory();
    
    // Craft payload with unknown marker
    var maliciousData = new byte[] { 0x99, 0x00, 0x00, 0x00, 0x01 };
    
    // Should throw, not deserialize
    Assert.Throws<InvalidOperationException>(() =>
        factory.DeserializeSimpleTypesBinary(maliciousData));
}
```

#### Fuzzing Tests

```csharp
[Fact]
public void SecureBinary_HandlesRandomInput()
{
    var factory = new RpcSerializationSessionFactory();
    var random = new Random(42);
    
    for (int i = 0; i < 1000; i++)
    {
        var randomBytes = new byte[random.Next(1, 1000)];
        random.NextBytes(randomBytes);
        
        // Should either deserialize successfully or throw
        // Should never crash or execute code
        try
        {
            factory.DeserializeSimpleTypesBinary(randomBytes);
        }
        catch (InvalidOperationException)
        {
            // Expected for malformed data
        }
        catch (EndOfStreamException) 
        {
            // Expected for truncated data
        }
        // Any other exception indicates a security issue
    }
}
```

### Secure Extension Guidelines

When adding new supported types:

#### 1. Security Analysis
- ✅ Is the type a value type or immutable?
- ✅ Does deserialization only set primitive fields?
- ✅ Can the type reference other dangerous objects?
- ✅ Are there known exploits for this type?

#### 2. Implementation Requirements
- ✅ Add explicit type marker
- ✅ Validate input before deserialization
- ✅ Handle malformed data gracefully
- ✅ Test with malicious inputs

#### 3. Documentation
- ✅ Document security rationale for inclusion
- ✅ Explain any limitations or restrictions
- ✅ Provide secure usage examples

## Monitoring and Detection

### Security Logging

Enable security-relevant logging:

```csharp
// Log format detection for monitoring
_logger.LogWarning("[SECURITY] Deprecated JSON serialization format detected from {Source}");

// Log rejected serialization attempts  
_logger.LogError("[SECURITY] Rejected serialization of dangerous type: {Type}");

// Log suspicious deserialization patterns
_logger.LogWarning("[SECURITY] Unusual deserialization pattern: {Pattern}");
```

### Metrics and Alerting

Track security metrics:

- **Format Usage**: Monitor JSON vs secure binary usage trends
- **Rejection Rates**: Track rejected type serialization attempts
- **Error Patterns**: Look for systematic deserialization failures
- **Performance Anomalies**: Detect potential DoS attacks

### Incident Response

If a serialization vulnerability is discovered:

1. **Immediate**: Block the attack vector (disable format if necessary)
2. **Short-term**: Deploy patched serialization logic
3. **Long-term**: Review and strengthen type whitelisting
4. **Communication**: Update security documentation

## Migration from Insecure Patterns

### Identifying Vulnerable Code

Look for these patterns in existing code:

```csharp
// VULNERABLE: Arbitrary JSON deserialization
var result = JsonSerializer.Deserialize<object>(json);

// VULNERABLE: Type-based deserialization
var type = Type.GetType(typeName);
var result = JsonSerializer.Deserialize(json, type);

// VULNERABLE: Custom ISerializable
public class CustomType : ISerializable { ... }
```

### Secure Alternatives

Replace vulnerable patterns:

```csharp
// SECURE: Use RpcSerializationSessionFactory
var result = _sessionFactory.DeserializeWithIsolatedSession<KnownType>(serializer, data);

// SECURE: Explicit type handling
if (typeof(T) == typeof(string))
    return (T)(object)DeserializeString(data);
else if (typeof(T) == typeof(Guid))
    return (T)(object)DeserializeGuid(data);
```

## Compliance and Standards

### Industry Standards

- **OWASP**: Follow deserialization cheat sheet recommendations
- **CWE-502**: Mitigate deserialization of untrusted data
- **NIST**: Implement secure coding practices for serialization

### Regulatory Requirements

For regulated environments:
- **SOX**: Maintain audit trails of serialization security changes
- **HIPAA**: Ensure patient data serialization is secure
- **PCI DSS**: Protect payment data during serialization

## Related Documentation

- [Secure Binary Serialization](../serialization/SECURE-BINARY-SERIALIZATION.md) - Technical implementation
- [RPC Serialization Fix](../RPC-SERIALIZATION-FIX.md) - Evolution and context  
- [Security Concerns](SECURITY-CONCERNS.md) - General RPC security
- [Threat Model](THREAT-MODEL.md) - Broader security analysis