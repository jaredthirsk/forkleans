# Deserialization Safety Implementation Plan

**Document Version**: 1.0
**Created**: 2025-11-30
**Status**: Planning
**Priority**: HIGH (Security-Critical)

## Executive Summary

This document provides a comprehensive, implementation-ready plan for securing Granville RPC deserialization against arbitrary type instantiation attacks (CWE-502). The goal is to ensure that only explicitly authorized types can be deserialized, preventing Remote Code Execution (RCE) and other deserialization-based attacks.

### Current State

- **Risk Level**: HIGH - Orleans serialization can deserialize ANY type with a generated serializer
- **Attack Surface**: Network-exposed UDP endpoints accept untrusted serialized data
- **Existing Mitigations**: None enforced at runtime
- **Documentation**: Extensive threat analysis exists but no implementation

### Important Consideration: Orleans Codec Behavior

Orleans **only generates codecs** for types marked with `[GenerateSerializer]`. This means:
- Types without `[GenerateSerializer]` (like `System.Diagnostics.Process`) won't have codecs
- Attempting to deserialize such types will fail with a codec lookup error
- The attack surface is inherently limited to types the application explicitly marks as serializable

**Why type whitelisting is still valuable (defense in depth)**:
1. **Auditability**: Explicit list of allowed types for security review and compliance
2. **Logging**: Detect and log attempted attacks, even if they would fail at codec lookup
3. **Fallback serializers**: Orleans has some built-in codecs for common types
4. **`ISerializable` types**: Some Orleans configurations support legacy serialization (potential gadget vectors)
5. **Future-proofing**: Protection against Orleans serialization changes
6. **Type confusion**: Additional validation even when codecs exist

**Recommendation**: Type whitelisting is **optional but recommended** for internet-facing deployments. For internal/trusted networks, the Orleans codec requirement may provide sufficient protection.

### Target State

- Strict type whitelisting with runtime enforcement
- Automatic registration of `[GenerateSerializer]` types from trusted assemblies
- Explicit deny-by-default policy for all other types
- Resource limits preventing DoS via serialization
- Comprehensive security logging and monitoring

---

## Table of Contents

1. [Threat Model](#1-threat-model)
2. [Design Principles](#2-design-principles)
3. [Architecture Overview](#3-architecture-overview)
4. [Implementation Phases](#4-implementation-phases)
5. [Testing Strategy](#5-testing-strategy)
6. [Rollout Plan](#6-rollout-plan)
7. [Monitoring and Incident Response](#7-monitoring-and-incident-response)
8. [Dependencies](#8-dependencies)
9. [Risk Assessment](#9-risk-assessment)
10. [Appendices](#10-appendices)

---

## 1. Threat Model

### 1.1 Attack Vectors

#### 1.1.1 Arbitrary Type Instantiation
- [ ] **Understand the threat**: Attacker crafts serialized payload containing type information for dangerous classes
- [ ] **Document Orleans type resolution**: How `TypeCodec` resolves type names to `System.Type`
- [ ] **Identify dangerous type categories**:
  - [ ] Process execution types (`System.Diagnostics.Process`, `ProcessStartInfo`)
  - [ ] File system types (`FileStream`, `StreamWriter`)
  - [ ] Reflection types (`Assembly`, `MethodInfo`)
  - [ ] Code compilation types (`CSharpCodeProvider`)
  - [ ] Serialization callback types (`ISerializable`, `IDeserializationCallback`)
  - [ ] XAML/WPF types (`ObjectDataProvider`, `ResourceDictionary`)

#### 1.1.2 Gadget Chain Attacks
- [ ] **Research known .NET gadget chains**:
  - [ ] ysoserial.net gadgets applicable to Orleans
  - [ ] ObjectDataProvider chains
  - [ ] TypeConfuseDelegate chains
  - [ ] ActivitySurrogateSelector exploits
- [ ] **Analyze Orleans-specific gadget potential**:
  - [ ] Grain activation as a gadget
  - [ ] Serialization hooks in Orleans types
  - [ ] Custom codec exploitation

#### 1.1.3 Resource Exhaustion
- [ ] **Identify DoS vectors**:
  - [ ] Deeply nested object graphs (stack overflow)
  - [ ] Extremely large collections (memory exhaustion)
  - [ ] Circular references (infinite loops)
  - [ ] Large string allocations
- [ ] **Define resource limits**:
  - [ ] Maximum object graph depth (default: 100)
  - [ ] Maximum collection size (default: 10,000)
  - [ ] Maximum string length (default: 1MB)
  - [ ] Maximum total deserialized size (default: 10MB)

#### 1.1.4 Type Confusion
- [ ] **Understand type substitution attacks**:
  - [ ] Subtype substitution (sending derived class for base)
  - [ ] Interface implementation confusion
  - [ ] Generic type parameter manipulation
- [ ] **Define type matching policy**:
  - [ ] Exact type matching vs. assignability
  - [ ] Generic type handling rules
  - [ ] Interface serialization policy

### 1.2 Threat Actors

| Actor | Capability | Motivation | Likelihood |
|-------|------------|------------|------------|
| Script Kiddie | Uses public tools (ysoserial) | Disruption, fame | HIGH |
| Skilled Attacker | Custom exploit development | Data theft, persistence | MEDIUM |
| Insider | Application knowledge | Sabotage, fraud | LOW |
| Nation State | Zero-day capability | Espionage | LOW |

### 1.3 Attack Scenarios

- [ ] **Scenario 1**: Attacker sends crafted UDP packet with `System.Diagnostics.Process` payload
- [ ] **Scenario 2**: Attacker exploits gadget chain through legitimate-looking game data
- [ ] **Scenario 3**: Attacker sends malformed data to trigger excessive memory allocation
- [ ] **Scenario 4**: Attacker substitutes malicious subtype for expected parameter type

---

## 2. Design Principles

### 2.1 Core Security Principles

- [ ] **Deny by Default**: All types are blocked unless explicitly whitelisted
- [ ] **Fail Secure**: Unknown types cause exceptions, not silent behavior
- [ ] **Defense in Depth**: Multiple layers of validation
- [ ] **Least Privilege**: Whitelist only types actually needed
- [ ] **Auditability**: All decisions logged for security review

### 2.2 Compatibility Principles

- [ ] **Backward Compatible**: Existing applications continue working after adding whitelisting
- [ ] **Opt-in Strictness**: Gradual migration path from permissive to strict
- [ ] **Orleans Integration**: Work with Orleans serialization, not against it
- [ ] **Performance Preservation**: Minimal overhead for legitimate traffic

### 2.3 Operational Principles

- [ ] **Configuration Driven**: Whitelist managed via configuration, not code changes
- [ ] **Hot Reload**: Whitelist changes without application restart (stretch goal)
- [ ] **Diagnostics**: Clear error messages for debugging type rejection
- [ ] **Metrics**: Observable security metrics for monitoring

---

## 3. Architecture Overview

### 3.1 Component Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         GRANVILLE RPC SECURITY LAYER                        │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐         │
│  │ IRpcTypePolicy  │    │ TypeWhitelist   │    │ ResourceLimiter │         │
│  │                 │    │                 │    │                 │         │
│  │ - IsAllowed()   │◄───│ - AllowedTypes  │    │ - MaxDepth      │         │
│  │ - OnRejected()  │    │ - AllowedAssms  │    │ - MaxSize       │         │
│  │ - GetPolicy()   │    │ - DeniedTypes   │    │ - MaxLength     │         │
│  └────────┬────────┘    └────────┬────────┘    └────────┬────────┘         │
│           │                      │                      │                   │
│           └──────────────────────┼──────────────────────┘                   │
│                                  │                                          │
│                                  ▼                                          │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                    SecureDeserializationFilter                       │   │
│  │                                                                      │   │
│  │  - Wraps Orleans TypeCodec                                          │   │
│  │  - Intercepts type resolution                                       │   │
│  │  - Enforces whitelist + resource limits                             │   │
│  │  - Logs security events                                             │   │
│  └────────────────────────────────────┬────────────────────────────────┘   │
│                                       │                                     │
└───────────────────────────────────────┼─────────────────────────────────────┘
                                        │
                                        ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                         ORLEANS SERIALIZATION LAYER                         │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐         │
│  │ TypeCodec       │    │ CodecProvider   │    │ Serializer      │         │
│  │                 │    │                 │    │                 │         │
│  │ - TryRead()     │    │ - GetCodec()    │    │ - Deserialize() │         │
│  │ - WriteEncoded()│    │ - untypedCodecs │    │ - Serialize()   │         │
│  └─────────────────┘    └─────────────────┘    └─────────────────┘         │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 3.2 Integration Points

```csharp
// Option A: Wrap TypeCodec (Preferred - intercepts at type resolution)
public class SecureTypeCodec : TypeCodec
{
    private readonly IRpcTypePolicy _policy;

    public override Type TryRead<TInput>(ref Reader<TInput> reader)
    {
        var type = base.TryRead(ref reader);
        if (type != null && !_policy.IsAllowed(type))
        {
            _policy.OnRejected(type);
            throw new RpcTypeNotAllowedException(type);
        }
        return type;
    }
}

// Option B: Wrap CodecProvider (Alternative - intercepts at codec lookup)
public class SecureCodecProvider : ICodecProvider
{
    private readonly CodecProvider _inner;
    private readonly IRpcTypePolicy _policy;

    public IFieldCodec GetCodec(Type type)
    {
        if (!_policy.IsAllowed(type))
            throw new RpcTypeNotAllowedException(type);
        return _inner.GetCodec(type);
    }
}

// Option C: Custom deserializer wrapper (Most isolated)
public class SecureRpcDeserializer
{
    public T Deserialize<T>(ReadOnlyMemory<byte> data)
    {
        // Pre-scan for type markers before deserialization
        // Requires understanding Orleans binary format
    }
}
```

### 3.3 Recommended Approach

**Option A (Wrap TypeCodec)** is recommended because:
1. Intercepts at the earliest point (type resolution)
2. Works with all Orleans deserialization paths
3. Minimal performance overhead
4. Clear separation of concerns

---

## 4. Implementation Phases

### Phase 1: Foundation (Week 1-2)

#### 4.1.1 Create Security Infrastructure Project
- [ ] Create `Orleans.Rpc.Security` project in `/src/Rpc/`
  - [ ] Project file with appropriate dependencies
  - [ ] Reference to `Orleans.Serialization`
  - [ ] Reference to `Microsoft.Extensions.Logging`
- [ ] Set up project structure:
  ```
  Orleans.Rpc.Security/
  ├── TypeSafety/
  │   ├── IRpcTypePolicy.cs
  │   ├── ITypeWhitelist.cs
  │   ├── IResourceLimiter.cs
  │   ├── TypeWhitelistBuilder.cs
  │   └── RpcTypeNotAllowedException.cs
  ├── Configuration/
  │   ├── RpcSecurityOptions.cs
  │   └── TypeWhitelistOptions.cs
  ├── Internal/
  │   └── SecureTypeCodec.cs
  └── Extensions/
      └── RpcSecurityServiceExtensions.cs
  ```

#### 4.1.2 Define Core Interfaces
- [ ] **IRpcTypePolicy interface**:
  ```csharp
  public interface IRpcTypePolicy
  {
      bool IsAllowed(Type type);
      bool IsAllowed(Type type, out string? reason);
      void OnRejected(Type type, RpcSecurityContext context);
      TypePolicyDecision GetPolicy(Type type);
  }

  public enum TypePolicyDecision
  {
      Allowed,          // Explicitly whitelisted
      Denied,           // Explicitly blacklisted
      DefaultAllow,     // Not listed, default policy allows
      DefaultDeny       // Not listed, default policy denies
  }
  ```

- [ ] **ITypeWhitelist interface**:
  ```csharp
  public interface ITypeWhitelist
  {
      IReadOnlySet<Type> AllowedTypes { get; }
      IReadOnlySet<string> AllowedAssemblies { get; }
      IReadOnlySet<string> AllowedNamespaces { get; }
      IReadOnlySet<Type> ExplicitlyDeniedTypes { get; }

      bool Contains(Type type);
      bool ContainsAssembly(string assemblyName);
      bool ContainsNamespace(string @namespace);
      bool IsDenied(Type type);
  }
  ```

- [ ] **IResourceLimiter interface**:
  ```csharp
  public interface IResourceLimiter
  {
      int MaxObjectGraphDepth { get; }
      int MaxCollectionSize { get; }
      int MaxStringLength { get; }
      long MaxTotalBytes { get; }

      void EnterObject();
      void ExitObject();
      void ValidateCollectionSize(int size);
      void ValidateStringLength(int length);
      void AddBytes(int count);
  }
  ```

#### 4.1.3 Implement Type Whitelist Builder
- [ ] **TypeWhitelistBuilder fluent API**:
  ```csharp
  public class TypeWhitelistBuilder
  {
      // Explicit type additions
      public TypeWhitelistBuilder AllowType<T>();
      public TypeWhitelistBuilder AllowType(Type type);
      public TypeWhitelistBuilder AllowTypes(params Type[] types);

      // Assembly-level additions
      public TypeWhitelistBuilder AllowAssembly(Assembly assembly);
      public TypeWhitelistBuilder AllowAssembly(string assemblyName);
      public TypeWhitelistBuilder AllowAssemblyOf<T>();

      // Namespace-level additions
      public TypeWhitelistBuilder AllowNamespace(string @namespace);
      public TypeWhitelistBuilder AllowNamespaceOf<T>();

      // Auto-discovery of [GenerateSerializer] types
      public TypeWhitelistBuilder AllowGeneratedSerializers();
      public TypeWhitelistBuilder AllowGeneratedSerializersFrom(Assembly assembly);

      // Explicit denials (override allows)
      public TypeWhitelistBuilder DenyType<T>();
      public TypeWhitelistBuilder DenyType(Type type);
      public TypeWhitelistBuilder DenyDangerousTypes(); // Built-in dangerous type list

      // Primitives and common types
      public TypeWhitelistBuilder AllowPrimitives();
      public TypeWhitelistBuilder AllowCommonTypes(); // string, Guid, DateTime, etc.

      // Build
      public ITypeWhitelist Build();
  }
  ```

- [ ] **Implementation details**:
  - [ ] Thread-safe HashSet storage for types
  - [ ] Trie or sorted set for namespace prefix matching
  - [ ] Cached assembly scan results
  - [ ] Validation of added types (no dangerous types in allow list)

#### 4.1.4 Implement Dangerous Type Detection
- [ ] **Built-in dangerous type list** (deny always):
  ```csharp
  private static readonly HashSet<Type> DangerousTypes = new()
  {
      // Process execution
      typeof(System.Diagnostics.Process),
      typeof(System.Diagnostics.ProcessStartInfo),

      // File system
      typeof(System.IO.FileStream),
      typeof(System.IO.StreamWriter),
      typeof(System.IO.StreamReader),
      typeof(System.IO.File),
      typeof(System.IO.Directory),

      // Reflection/Code execution
      typeof(System.Reflection.Assembly),
      typeof(System.Reflection.MethodInfo),
      typeof(System.Type), // Type itself can be dangerous

      // Security sensitive
      typeof(System.Security.Principal.WindowsIdentity),
      typeof(System.Security.Principal.WindowsPrincipal),

      // Network
      typeof(System.Net.WebClient),
      typeof(System.Net.Http.HttpClient),

      // Delegates (code execution)
      typeof(System.Delegate),
      typeof(System.Action),
      typeof(System.Func<>),
  };
  ```

- [ ] **Dangerous namespace patterns** (deny unless explicitly allowed):
  ```csharp
  private static readonly string[] DangerousNamespaces = new[]
  {
      "System.Diagnostics",
      "System.Reflection",
      "System.CodeDom",
      "System.Runtime.Serialization.Formatters",
      "System.Runtime.Remoting",
      "System.Windows.Markup", // XAML/ObjectDataProvider
      "Microsoft.CSharp",
      "Microsoft.VisualBasic",
  };
  ```

#### 4.1.5 Create Exception Types
- [ ] **RpcTypeNotAllowedException**:
  ```csharp
  public class RpcTypeNotAllowedException : RpcException
  {
      public Type AttemptedType { get; }
      public string Reason { get; }
      public TypePolicyDecision Decision { get; }

      // Security: Don't expose type name in message by default
      public override string Message =>
          "Deserialization blocked: type not in whitelist. " +
          "See server logs for details.";
  }
  ```

- [ ] **RpcResourceLimitExceededException**:
  ```csharp
  public class RpcResourceLimitExceededException : RpcException
  {
      public ResourceLimitType LimitType { get; }
      public long Attempted { get; }
      public long Maximum { get; }
  }

  public enum ResourceLimitType
  {
      ObjectGraphDepth,
      CollectionSize,
      StringLength,
      TotalBytes
  }
  ```

### Phase 2: Orleans Integration (Week 2-3)

#### 4.2.1 Implement SecureTypeCodec
- [ ] **Analyze TypeCodec extension points**:
  - [ ] Review `TypeCodec.TryRead<TInput>()` method
  - [ ] Identify if class is sealed or has virtual methods
  - [ ] Determine inheritance vs. composition approach

- [ ] **Implementation approach A: Wrapper/Decorator**:
  ```csharp
  public class SecureTypeCodec
  {
      private readonly TypeCodec _inner;
      private readonly IRpcTypePolicy _policy;
      private readonly ILogger _logger;

      public Type? TryReadSecure<TInput>(ref Reader<TInput> reader)
      {
          // Read type using Orleans TypeCodec
          var type = _inner.TryRead(ref reader);

          if (type == null)
              return null;

          // Enforce whitelist
          if (!_policy.IsAllowed(type, out var reason))
          {
              _logger.LogWarning(
                  "[SECURITY] Blocked deserialization of type {TypeName}. Reason: {Reason}",
                  type.FullName, reason);

              _policy.OnRejected(type, CreateContext());
              throw new RpcTypeNotAllowedException(type, reason);
          }

          return type;
      }
  }
  ```

- [ ] **Implementation approach B: Custom TypeConverter** (if TypeCodec not extensible):
  ```csharp
  public class SecureTypeConverter : TypeConverter
  {
      private readonly TypeConverter _inner;
      private readonly IRpcTypePolicy _policy;

      public override Type? TryParse(ReadOnlySpan<char> typeName)
      {
          var type = _inner.TryParse(typeName);
          // ... validation logic
      }
  }
  ```

#### 4.2.2 Integrate with Serialization Session
- [ ] **Extend SerializerSession** (if possible):
  - [ ] Add `IRpcTypePolicy` to session context
  - [ ] Add `IResourceLimiter` to session context
  - [ ] Track deserialization depth during session

- [ ] **Alternative: Scoped service per deserialization**:
  ```csharp
  public class SecureDeserializationScope : IDisposable
  {
      private static readonly AsyncLocal<SecureDeserializationScope?> _current = new();

      public static SecureDeserializationScope? Current => _current.Value;

      public IRpcTypePolicy TypePolicy { get; }
      public IResourceLimiter ResourceLimiter { get; }
      public int CurrentDepth { get; private set; }

      public SecureDeserializationScope(IRpcTypePolicy policy, IResourceLimiter limiter)
      {
          TypePolicy = policy;
          ResourceLimiter = limiter;
          _current.Value = this;
      }

      public void Dispose() => _current.Value = null;
  }
  ```

#### 4.2.3 Modify RpcSerializationSessionFactory
- [ ] **Update DeserializeWithIsolatedSession**:
  ```csharp
  public T DeserializeWithIsolatedSession<T>(Serializer serializer, ReadOnlyMemory<byte> data)
  {
      using var securityScope = new SecureDeserializationScope(_typePolicy, _resourceLimiter);

      try
      {
          // Existing deserialization logic...
          var result = InnerDeserialize<T>(serializer, data);

          // Post-deserialization validation
          ValidateDeserializedObject(result);

          return result;
      }
      catch (RpcTypeNotAllowedException ex)
      {
          _securityLogger.LogBlockedDeserialization(ex);
          throw;
      }
  }

  private void ValidateDeserializedObject<T>(T obj)
  {
      // Validate the deserialized object graph
      // Check for unexpected types in nested properties
      // This is a defense-in-depth measure
  }
  ```

#### 4.2.4 Register Security Services
- [ ] **DI registration extension methods**:
  ```csharp
  public static class RpcSecurityServiceExtensions
  {
      public static IRpcBuilder UseTypeWhitelisting(
          this IRpcBuilder builder,
          Action<TypeWhitelistBuilder> configure)
      {
          var whitelistBuilder = new TypeWhitelistBuilder();
          configure(whitelistBuilder);

          builder.Services.AddSingleton<ITypeWhitelist>(
              whitelistBuilder.Build());
          builder.Services.AddSingleton<IRpcTypePolicy, DefaultRpcTypePolicy>();
          builder.Services.AddScoped<IResourceLimiter, DefaultResourceLimiter>();

          // Replace TypeCodec or add interceptor
          builder.Services.Decorate<TypeCodec, SecureTypeCodec>();

          return builder;
      }

      public static IRpcBuilder UseStrictSerialization(this IRpcBuilder builder)
      {
          return builder.UseTypeWhitelisting(whitelist =>
          {
              whitelist
                  .AllowPrimitives()
                  .AllowCommonTypes()
                  .AllowGeneratedSerializers()
                  .DenyDangerousTypes();
          });
      }
  }
  ```

### Phase 3: Auto-Discovery (Week 3-4)

#### 4.3.1 Implement [GenerateSerializer] Type Scanner
- [ ] **Assembly scanner for generated serializers**:
  ```csharp
  public class GeneratedSerializerScanner
  {
      public IReadOnlySet<Type> ScanAssembly(Assembly assembly)
      {
          var types = new HashSet<Type>();

          foreach (var type in assembly.GetTypes())
          {
              if (HasGenerateSerializerAttribute(type))
              {
                  types.Add(type);

                  // Also add nested types with [Id] attributes
                  foreach (var property in type.GetProperties())
                  {
                      if (HasIdAttribute(property))
                      {
                          AddPropertyType(types, property.PropertyType);
                      }
                  }
              }
          }

          return types;
      }

      private bool HasGenerateSerializerAttribute(Type type)
      {
          return type.GetCustomAttribute<GenerateSerializerAttribute>() != null;
      }
  }
  ```

- [ ] **Recursive type graph discovery**:
  ```csharp
  private void AddPropertyType(HashSet<Type> types, Type propertyType)
  {
      if (types.Contains(propertyType))
          return; // Already processed

      // Handle generic types (List<T>, Dictionary<K,V>, etc.)
      if (propertyType.IsGenericType)
      {
          foreach (var arg in propertyType.GetGenericArguments())
          {
              AddPropertyType(types, arg);
          }
      }

      // Handle arrays
      if (propertyType.IsArray)
      {
          AddPropertyType(types, propertyType.GetElementType()!);
      }

      // Add the type itself if it has [GenerateSerializer]
      if (HasGenerateSerializerAttribute(propertyType))
      {
          types.Add(propertyType);
      }
  }
  ```

#### 4.3.2 Implement Trusted Assembly Configuration
- [ ] **Configuration-based assembly trust**:
  ```csharp
  public class TypeWhitelistOptions
  {
      /// <summary>
      /// Assembly names that are fully trusted for serialization.
      /// All [GenerateSerializer] types from these assemblies are allowed.
      /// </summary>
      public HashSet<string> TrustedAssemblies { get; } = new()
      {
          "Shooter.Shared",
          "Granville.Rpc.Abstractions",
          // Add application-specific assemblies
      };

      /// <summary>
      /// Whether to auto-discover [GenerateSerializer] types at startup.
      /// </summary>
      public bool AutoDiscoverGeneratedSerializers { get; set; } = true;

      /// <summary>
      /// Whether to scan entry assembly and its references.
      /// </summary>
      public bool ScanEntryAssembly { get; set; } = true;
  }
  ```

- [ ] **Startup assembly scanning**:
  ```csharp
  public class TypeWhitelistInitializer : IHostedService
  {
      private readonly ITypeWhitelist _whitelist;
      private readonly TypeWhitelistOptions _options;

      public Task StartAsync(CancellationToken ct)
      {
          if (_options.AutoDiscoverGeneratedSerializers)
          {
              var scanner = new GeneratedSerializerScanner();

              foreach (var assemblyName in _options.TrustedAssemblies)
              {
                  var assembly = Assembly.Load(assemblyName);
                  var types = scanner.ScanAssembly(assembly);

                  foreach (var type in types)
                  {
                      _whitelist.Add(type);
                  }
              }
          }

          return Task.CompletedTask;
      }
  }
  ```

#### 4.3.3 Handle Generic Types
- [ ] **Generic type whitelist rules**:
  ```csharp
  public bool IsGenericTypeAllowed(Type genericType)
  {
      // Get the generic type definition
      var definition = genericType.GetGenericTypeDefinition();

      // Check if definition is allowed
      if (!IsAllowed(definition))
          return false;

      // Check all type arguments
      foreach (var arg in genericType.GetGenericArguments())
      {
          if (!IsAllowed(arg))
              return false;
      }

      return true;
  }
  ```

- [ ] **Built-in generic type allowlist**:
  ```csharp
  private static readonly HashSet<Type> AllowedGenericDefinitions = new()
  {
      typeof(List<>),
      typeof(Dictionary<,>),
      typeof(HashSet<>),
      typeof(Queue<>),
      typeof(Stack<>),
      typeof(ImmutableList<>),
      typeof(ImmutableDictionary<,>),
      typeof(Nullable<>),
      typeof(Task<>),
      typeof(ValueTask<>),
      // ... etc
  };
  ```

### Phase 4: Resource Limits (Week 4)

#### 4.4.1 Implement Resource Limiter
- [ ] **DefaultResourceLimiter implementation**:
  ```csharp
  public class DefaultResourceLimiter : IResourceLimiter
  {
      private readonly ResourceLimitOptions _options;
      private int _currentDepth;
      private long _totalBytes;

      public int MaxObjectGraphDepth => _options.MaxDepth;
      public int MaxCollectionSize => _options.MaxCollectionSize;
      public int MaxStringLength => _options.MaxStringLength;
      public long MaxTotalBytes => _options.MaxTotalBytes;

      public void EnterObject()
      {
          if (++_currentDepth > _options.MaxDepth)
          {
              throw new RpcResourceLimitExceededException(
                  ResourceLimitType.ObjectGraphDepth,
                  _currentDepth,
                  _options.MaxDepth);
          }
      }

      public void ExitObject()
      {
          _currentDepth--;
      }

      public void ValidateCollectionSize(int size)
      {
          if (size > _options.MaxCollectionSize)
          {
              throw new RpcResourceLimitExceededException(
                  ResourceLimitType.CollectionSize,
                  size,
                  _options.MaxCollectionSize);
          }
      }

      public void ValidateStringLength(int length)
      {
          if (length > _options.MaxStringLength)
          {
              throw new RpcResourceLimitExceededException(
                  ResourceLimitType.StringLength,
                  length,
                  _options.MaxStringLength);
          }
      }

      public void AddBytes(int count)
      {
          _totalBytes += count;
          if (_totalBytes > _options.MaxTotalBytes)
          {
              throw new RpcResourceLimitExceededException(
                  ResourceLimitType.TotalBytes,
                  _totalBytes,
                  _options.MaxTotalBytes);
          }
      }
  }
  ```

#### 4.4.2 Configure Default Limits
- [ ] **ResourceLimitOptions**:
  ```csharp
  public class ResourceLimitOptions
  {
      /// <summary>
      /// Maximum depth of nested objects during deserialization.
      /// Prevents stack overflow from deeply nested payloads.
      /// Default: 100
      /// </summary>
      public int MaxDepth { get; set; } = 100;

      /// <summary>
      /// Maximum number of elements in a collection.
      /// Prevents memory exhaustion from large arrays/lists.
      /// Default: 10,000
      /// </summary>
      public int MaxCollectionSize { get; set; } = 10_000;

      /// <summary>
      /// Maximum length of a single string in bytes.
      /// Prevents large string allocations.
      /// Default: 1 MB
      /// </summary>
      public int MaxStringLength { get; set; } = 1 * 1024 * 1024;

      /// <summary>
      /// Maximum total bytes deserialized per request.
      /// Prevents overall memory exhaustion.
      /// Default: 10 MB
      /// </summary>
      public long MaxTotalBytes { get; set; } = 10 * 1024 * 1024;
  }
  ```

#### 4.4.3 Integrate Resource Limits with Deserialization
- [ ] **Hook into collection/string deserialization** (requires Orleans code analysis):
  - [ ] Identify collection codec entry points
  - [ ] Identify string codec entry points
  - [ ] Add validation calls at appropriate points

### Phase 5: Security Logging (Week 5)

#### 4.5.1 Implement Security Event Logger
- [ ] **IRpcSecurityLogger interface**:
  ```csharp
  public interface IRpcSecurityLogger
  {
      void LogBlockedType(Type type, string reason, RpcSecurityContext context);
      void LogResourceLimitExceeded(ResourceLimitType limitType, long attempted, long max, RpcSecurityContext context);
      void LogSuspiciousActivity(string description, RpcSecurityContext context);
      void LogWhitelistMiss(Type type, RpcSecurityContext context);
  }
  ```

- [ ] **Structured logging implementation**:
  ```csharp
  public class RpcSecurityLogger : IRpcSecurityLogger
  {
      private readonly ILogger _logger;
      private readonly IRpcSecurityMetrics _metrics;

      public void LogBlockedType(Type type, string reason, RpcSecurityContext context)
      {
          _logger.LogWarning(
              "[SECURITY] Blocked deserialization. " +
              "Type={TypeName} Reason={Reason} " +
              "RemoteEndpoint={RemoteEndpoint} " +
              "PlayerId={PlayerId} " +
              "RequestId={RequestId}",
              type.FullName,
              reason,
              context.RemoteEndpoint,
              context.PlayerId,
              context.RequestId);

          _metrics.BlockedDeserializationCount.Inc(new[] { type.Name, reason });
      }
  }
  ```

#### 4.5.2 Define Security Metrics
- [ ] **Prometheus metrics**:
  ```csharp
  public interface IRpcSecurityMetrics
  {
      Counter BlockedDeserializationCount { get; }
      Counter ResourceLimitExceededCount { get; }
      Histogram DeserializationDuration { get; }
      Gauge WhitelistSize { get; }
  }
  ```

- [ ] **Metric implementation**:
  ```csharp
  public class RpcSecurityMetrics : IRpcSecurityMetrics
  {
      public Counter BlockedDeserializationCount { get; } = Metrics.CreateCounter(
          "granville_rpc_blocked_deserialization_total",
          "Number of blocked deserialization attempts",
          new CounterConfiguration
          {
              LabelNames = new[] { "type", "reason" }
          });

      // ... other metrics
  }
  ```

### Phase 6: Configuration & API (Week 5-6)

#### 4.6.1 Create User-Friendly Configuration API
- [ ] **Extension method overloads**:
  ```csharp
  // Simple: use defaults
  services.AddGranvilleRpc().UseStrictSerialization();

  // Medium: configure via builder
  services.AddGranvilleRpc().UseTypeWhitelisting(whitelist =>
  {
      whitelist
          .AllowPrimitives()
          .AllowCommonTypes()
          .AllowGeneratedSerializersFrom(typeof(MyModels).Assembly)
          .AllowType<MyCustomType>();
  });

  // Advanced: full options
  services.AddGranvilleRpc().UseTypeWhitelisting(options =>
  {
      options.TrustedAssemblies.Add("MyApp.Models");
      options.ResourceLimits.MaxDepth = 50;
      options.EnableStrictMode = true;
      options.LogWhitelistMisses = true;
  });
  ```

#### 4.6.2 Support JSON/YAML Configuration
- [ ] **appsettings.json configuration**:
  ```json
  {
    "GranvilleRpc": {
      "Security": {
        "TypeWhitelist": {
          "TrustedAssemblies": [
            "Shooter.Shared",
            "MyApp.Models"
          ],
          "AllowedTypes": [
            "MyNamespace.SpecialType"
          ],
          "DeniedTypes": [
            "SomeAssembly.UnsafeType"
          ]
        },
        "ResourceLimits": {
          "MaxObjectGraphDepth": 100,
          "MaxCollectionSize": 10000,
          "MaxStringLength": 1048576
        }
      }
    }
  }
  ```

#### 4.6.3 Add Health Check
- [ ] **Security configuration health check**:
  ```csharp
  public class RpcSecurityHealthCheck : IHealthCheck
  {
      private readonly ITypeWhitelist _whitelist;

      public Task<HealthCheckResult> CheckHealthAsync(
          HealthCheckContext context,
          CancellationToken ct = default)
      {
          var issues = new List<string>();

          if (_whitelist.AllowedTypes.Count == 0)
              issues.Add("No types in whitelist - all deserialization will fail");

          if (!_whitelist.ContainsAssembly("Granville.Rpc.Abstractions"))
              issues.Add("Core RPC types not whitelisted");

          return Task.FromResult(issues.Count == 0
              ? HealthCheckResult.Healthy("Type whitelist configured correctly")
              : HealthCheckResult.Degraded(string.Join("; ", issues)));
      }
  }
  ```

---

## 5. Testing Strategy

### 5.1 Unit Tests

#### 5.1.1 Type Whitelist Tests
- [ ] **Positive cases**:
  - [ ] Whitelisted primitive types allowed
  - [ ] Whitelisted custom types allowed
  - [ ] Types from trusted assemblies allowed
  - [ ] Types in allowed namespaces allowed
  - [ ] Generic types with allowed arguments allowed

- [ ] **Negative cases**:
  - [ ] Non-whitelisted types rejected
  - [ ] Dangerous types always rejected (even if explicitly added)
  - [ ] Types from untrusted assemblies rejected
  - [ ] Generic types with denied arguments rejected

- [ ] **Edge cases**:
  - [ ] Null type handling
  - [ ] Generic type definitions vs constructed types
  - [ ] Nested types
  - [ ] Private types
  - [ ] Dynamic types

#### 5.1.2 Resource Limiter Tests
- [ ] **Depth limit tests**:
  - [ ] Exactly at limit: allowed
  - [ ] One over limit: rejected
  - [ ] Proper depth tracking with enter/exit

- [ ] **Collection size tests**:
  - [ ] Various sizes under limit
  - [ ] Exactly at limit
  - [ ] Over limit

- [ ] **String length tests**:
  - [ ] Short strings
  - [ ] Strings at limit
  - [ ] Strings over limit

- [ ] **Total bytes tests**:
  - [ ] Cumulative tracking
  - [ ] Reset between requests

### 5.2 Integration Tests

#### 5.2.1 End-to-End Deserialization Tests
- [ ] **Legitimate traffic**:
  ```csharp
  [Fact]
  public async Task Deserialize_WhitelistedType_Succeeds()
  {
      var data = SerializeWithType(new PlayerState { ... });
      var result = await _client.DeserializeAsync<PlayerState>(data);
      Assert.NotNull(result);
  }
  ```

- [ ] **Blocked traffic**:
  ```csharp
  [Fact]
  public async Task Deserialize_ProcessType_Throws()
  {
      var data = CraftMaliciousPayload(typeof(Process));
      await Assert.ThrowsAsync<RpcTypeNotAllowedException>(
          () => _client.DeserializeAsync<object>(data));
  }
  ```

#### 5.2.2 Performance Tests
- [ ] **Baseline latency**:
  - [ ] Measure deserialization latency without security
  - [ ] Measure deserialization latency with security
  - [ ] Acceptable overhead: <5%

- [ ] **Throughput tests**:
  - [ ] Messages per second without security
  - [ ] Messages per second with security

### 5.3 Security Tests

#### 5.3.1 Gadget Chain Tests
- [ ] **Test known gadgets**:
  ```csharp
  [Theory]
  [MemberData(nameof(GetKnownGadgetPayloads))]
  public void Deserialize_KnownGadget_IsBlocked(byte[] payload, string gadgetName)
  {
      Assert.Throws<RpcTypeNotAllowedException>(
          () => _serializer.Deserialize<object>(payload));
  }

  public static IEnumerable<object[]> GetKnownGadgetPayloads()
  {
      yield return new object[] { YsoseialPayloads.ObjectDataProvider, "ObjectDataProvider" };
      yield return new object[] { YsoseialPayloads.TypeConfuseDelegate, "TypeConfuseDelegate" };
      // ... more gadgets
  }
  ```

#### 5.3.2 Fuzzing Tests
- [ ] **Random payload fuzzing**:
  ```csharp
  [Fact]
  public void Deserialize_RandomPayloads_DoesNotCrash()
  {
      var fuzzer = new SerializationFuzzer(seed: 12345);

      for (int i = 0; i < 10000; i++)
      {
          var payload = fuzzer.GenerateRandomPayload();

          try
          {
              _serializer.Deserialize<object>(payload);
          }
          catch (RpcTypeNotAllowedException) { /* Expected */ }
          catch (RpcResourceLimitExceededException) { /* Expected */ }
          catch (InvalidOperationException) { /* Expected for malformed data */ }
          // Any other exception is a test failure
      }
  }
  ```

#### 5.3.3 Penetration Test Scenarios
- [ ] **Manual pentest checklist**:
  - [ ] Attempt to deserialize `System.Diagnostics.Process`
  - [ ] Attempt to deserialize `System.IO.File`
  - [ ] Attempt ObjectDataProvider gadget chain
  - [ ] Attempt deeply nested object (depth bomb)
  - [ ] Attempt large collection (memory bomb)
  - [ ] Attempt large string allocation
  - [ ] Attempt type confusion with subclasses
  - [ ] Attempt generic type parameter manipulation

---

## 6. Rollout Plan

### 6.1 Phase 1: Development Environment (Week 6)
- [ ] Deploy to development environment
- [ ] Enable logging but not enforcement (audit mode)
- [ ] Monitor for false positives
- [ ] Tune whitelist based on actual traffic
- [ ] Fix any legitimate types being rejected

### 6.2 Phase 2: Staging Environment (Week 7)
- [ ] Deploy to staging with enforcement enabled
- [ ] Run full test suite including security tests
- [ ] Perform load testing
- [ ] Verify metrics and alerting work correctly
- [ ] Document any configuration changes needed

### 6.3 Phase 3: Production Canary (Week 8)
- [ ] Deploy to production with feature flag (audit mode only)
- [ ] Monitor production traffic patterns
- [ ] Identify any unexpected type usage
- [ ] Validate no legitimate traffic is blocked

### 6.4 Phase 4: Production Enforcement (Week 9)
- [ ] Enable enforcement for small percentage of traffic
- [ ] Gradually increase percentage (10% → 50% → 100%)
- [ ] Monitor error rates and latency
- [ ] Have rollback plan ready

### 6.5 Rollback Procedure
- [ ] **Immediate rollback steps**:
  1. Set `RpcSecurity:Enforcement:Enabled = false` in config
  2. Restart affected services (or use feature flag for hot toggle)
  3. Verify traffic flowing normally
  4. Investigate blocked types from logs
  5. Update whitelist
  6. Re-enable enforcement

---

## 7. Monitoring and Incident Response

### 7.1 Alerts

#### 7.1.1 Critical Alerts
- [ ] **Blocked deserialization spike**:
  - Trigger: >100 blocked attempts in 1 minute
  - Action: Page on-call, potential attack in progress

- [ ] **Unknown type pattern**:
  - Trigger: Same type blocked >10 times from same IP
  - Action: Investigate potential probing

#### 7.1.2 Warning Alerts
- [ ] **Whitelist miss rate**:
  - Trigger: >1% of deserialization attempts hitting whitelist miss
  - Action: Review and expand whitelist if legitimate

- [ ] **Resource limit hits**:
  - Trigger: Any resource limit exceeded
  - Action: Review if limits are too strict or if attack

### 7.2 Dashboards

- [ ] **Security overview dashboard**:
  - Blocked attempts by type (pie chart)
  - Blocked attempts over time (line graph)
  - Top source IPs for blocked attempts
  - Resource limit violations by type
  - Whitelist size over time

### 7.3 Incident Response Runbook

#### 7.3.1 Suspected Attack
1. Check `granville_rpc_blocked_deserialization_total` for spike
2. Identify source IP(s) from logs
3. Check if types being attempted are known gadgets
4. If confirmed attack:
   - Consider IP-level blocking (firewall)
   - Collect forensic data (full payloads if logging enabled)
   - Report to security team
5. If false positive:
   - Add type to whitelist
   - Document why type is safe

#### 7.3.2 Legitimate Traffic Blocked
1. Identify blocked type from logs
2. Verify type is safe (no dangerous operations)
3. Add to whitelist:
   ```csharp
   whitelist.AllowType<BlockedType>();
   ```
4. Deploy configuration change
5. Monitor for resolution

---

## 8. Dependencies

### 8.1 Internal Dependencies

| Dependency | Purpose | Status |
|------------|---------|--------|
| Orleans.Serialization | TypeCodec integration | Available |
| Orleans.Rpc.Abstractions | RpcException base | Available |
| Microsoft.Extensions.* | DI, Logging, Options | Available |

### 8.2 External Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| BouncyCastle.Cryptography | 2.x | May be needed for DTLS (Phase 2 of security roadmap) |
| prometheus-net | 8.x | Metrics (optional) |

### 8.3 Prerequisites

- [ ] Phase 1 of security roadmap (HTTP Auth) should ideally be complete
- [ ] Understanding of Orleans serialization internals (TypeCodec, CodecProvider)
- [ ] Test infrastructure for security testing

---

## 9. Risk Assessment

### 9.1 Implementation Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Orleans internals not extensible | Medium | High | Have backup plan (wrapper approach) |
| Performance degradation | Medium | Medium | Benchmark early, optimize hot paths |
| False positives blocking legitimate traffic | High | Medium | Audit mode first, gradual rollout |
| Incomplete gadget coverage | Low | High | Continuous security research, fuzzing |
| Configuration complexity | Medium | Low | Provide good defaults, clear docs |

### 9.2 Residual Risks

Even after implementation, these risks remain:

1. **Zero-day gadgets**: New gadget chains may be discovered
   - Mitigation: Deny-by-default policy limits exposure

2. **Orleans vulnerabilities**: Bugs in Orleans serialization itself
   - Mitigation: Keep Orleans updated, monitor CVEs

3. **Bypass via other channels**: Attackers may find other entry points
   - Mitigation: Defense in depth, overall security posture

---

## 10. Appendices

### Appendix A: Orleans Serialization Internals

#### A.1 Type Resolution Flow
```
Serialized Data → TypeCodec.TryRead() → TypeConverter.TryParse() → Type.GetType() → Type
```

#### A.2 Key Classes
- `TypeCodec`: Encodes/decodes type information in binary format
- `TypeConverter`: Converts type names to/from strings
- `CodecProvider`: Provides codecs for specific types
- `SerializerSession`: Maintains state during serialization

#### A.3 Extension Points
- Custom `IGeneralizedCodec` for type-specific handling
- Custom `TypeConverter` for type resolution
- Service replacement via DI

### Appendix B: Known .NET Gadget Chains

| Gadget | Vector | Blocked By |
|--------|--------|------------|
| ObjectDataProvider | XAML types | Namespace deny |
| TypeConfuseDelegate | Delegate types | Type deny |
| ActivitySurrogateSelector | ISerializable | Type deny |
| DataSet/DataTable | SQL types | Type deny |
| WindowsIdentity | Security types | Type deny |

### Appendix C: Checklist Summary

```
Total items: ~200
Phase 1 (Foundation): 45 items
Phase 2 (Orleans Integration): 35 items
Phase 3 (Auto-Discovery): 25 items
Phase 4 (Resource Limits): 20 items
Phase 5 (Security Logging): 15 items
Phase 6 (Configuration): 20 items
Testing: 30 items
Rollout: 20 items
```

### Appendix D: Code Locations

| Component | Location |
|-----------|----------|
| RpcSerializationSessionFactory (Server) | `/src/Rpc/Orleans.Rpc.Server/RpcSerializationSessionFactory.cs` |
| RpcSerializationSessionFactory (Client) | `/src/Rpc/Orleans.Rpc.Client/RpcSerializationSessionFactory.cs` |
| Orleans TypeCodec | `/src/Orleans.Serialization/TypeSystem/TypeCodec.cs` |
| Orleans CodecProvider | `/src/Orleans.Serialization/Serializers/CodecProvider.cs` |
| Security docs | `/src/Rpc/docs/security/` |
| New security project | `/src/Rpc/Orleans.Rpc.Security/` (to be created) |

---

## Change Log

| Date | Version | Author | Changes |
|------|---------|--------|---------|
| 2025-11-30 | 1.0 | Claude | Initial detailed plan |

---

## Sign-Off

| Role | Name | Date | Approval |
|------|------|------|----------|
| Security Lead | | | [ ] |
| Tech Lead | | | [ ] |
| Architect | | | [ ] |
