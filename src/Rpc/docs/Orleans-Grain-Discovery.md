# Orleans Grain Discovery

This document explains how Orleans discovers grains and grain interfaces, both for activation and serialization. Understanding these mechanisms is crucial for troubleshooting issues like "No active nodes are compatible with grain."

## Overview

Orleans grain discovery involves several distinct but related systems:
1. **Type Discovery** - Finding grain interfaces and implementations in assemblies
2. **Type Manifest** - Registering types for serialization
3. **Grain Manifest** - Registering grain types for activation
4. **Placement** - Deciding which silo can host a grain

## 1. Type Discovery Process

### How Orleans Finds Grain Types

Orleans uses several mechanisms to discover grain types:

1. **Assembly Scanning**
   - Orleans scans referenced assemblies for grain interfaces and implementations
   - Looks for interfaces inheriting from `IGrain` or `IGrainWithIntegerKey`, etc.
   - Looks for classes inheriting from `Grain` or implementing grain interfaces

2. **Automatic Discovery (Orleans 7.0+)**
   - By default, Orleans automatically discovers types from all loaded assemblies
   - Uses `ApplicationPartManager` internally (different from the removed `ConfigureApplicationParts` API)
   - Discovers both grain interfaces and implementations

3. **Code Generation**
   - Orleans generates proxy classes for grain interfaces at build time
   - Generated proxy classes are named like `Proxy_IWorldManagerGrain` in namespace like `ForkleansCodeGen.Shooter.Shared.GrainInterfaces`
   - Internally, Orleans uses lowercase aliases like `proxy_iworldmanager` (drops "Grain" suffix, lowercases)
   - Generated code is placed in the same assembly as the grain interface

### Key Classes Involved

- `ApplicationPartManager` - Manages discovered application parts
- `GrainInterfaceTypeResolver` - Resolves grain interface types
- `GrainClassTypeResolver` - Resolves grain implementation types
- `TypeManifest` - Contains all discovered types

## 2. Grain Manifest vs Type Manifest

### Type Manifest (Serialization)
- **Purpose**: Knows about all types that can be serialized
- **Registration**: Via `AddSerializer` and `AddAssembly`
- **Used by**: Serialization system
- **Contains**: All types including DTOs, grain interfaces, etc.

### Grain Manifest (Activation)
- **Purpose**: Knows which grain types can be activated on which silos
- **Registration**: Automatic from discovered grain classes
- **Used by**: Placement system
- **Contains**: Only grain implementations and their interfaces

## 3. The Placement Process

When a client calls a grain method:

1. **Client creates proxy** - Uses generated proxy class (e.g., `proxy_iworldmanager`)
2. **Message routing** - Client sends message to a gateway silo
3. **Placement decision** - PlacementService determines which silo can host the grain
4. **Activation** - Selected silo creates grain instance

### The "No active nodes are compatible" Error

This error occurs when:
- The placement service cannot find any silo that knows about the grain implementation
- Common causes:
  1. Grain implementation assembly not loaded on silo
  2. Grain class not discovered during startup
  3. Mismatch between interface and implementation discovery

## 4. Discovery in Orleans 7.0+

### Changes from Earlier Versions
- `ConfigureApplicationParts` API removed
- Automatic discovery is now the default
- More reliance on build-time code generation

### Current Discovery Flow

```
Startup:
1. Silo starts
2. ApplicationPartManager discovers assemblies
3. Grain class map built from discovered types
4. Manifest published to cluster
5. Clients connect and receive manifest

Runtime:
1. Client calls grain.Method()
2. Proxy creates message with grain type
3. Gateway routes to placement service
4. Placement service checks grain class map
5. Selects compatible silo or throws error
```

## 5. Troubleshooting Grain Discovery

### Diagnostic Steps

1. **Enable detailed logging**
   ```csharp
   builder.Logging.AddFilter("Orleans.Runtime.Catalog", LogLevel.Trace);
   builder.Logging.AddFilter("Orleans.Metadata", LogLevel.Trace);
   builder.Logging.AddFilter("Orleans.Runtime.Placement", LogLevel.Trace);
   ```

2. **Check loaded assemblies**
   - Verify grain implementation assembly is loaded
   - Check for assembly loading errors in logs

3. **Verify grain registration**
   - Look for "Registering grain class" messages in silo logs
   - Check if grain type appears in cluster manifest

4. **Common Issues and Solutions**

| Issue | Cause | Solution |
|-------|-------|----------|
| No active nodes compatible | Grain impl not on silo | Ensure silo references grain assembly |
| Interface version mismatch | Different interface versions | Rebuild all projects |
| Proxy not found | Code generation failed | Check build output for generator errors |
| Grain class not registered | Discovery failed | Add explicit assembly reference |

## 6. Manual Registration Options

While automatic discovery is preferred, you can influence it:

### For Serialization
```csharp
builder.Services.AddSerializer(serializerBuilder =>
{
    serializerBuilder.AddAssembly(typeof(GrainImpl).Assembly);
});
```

### For Grain Discovery
In Orleans 7.0+, grain discovery is automatic, but you can ensure assemblies are loaded:

```csharp
// Force assembly loading
_ = typeof(WorldManagerGrain).Assembly;

// Or use assembly load context
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
```

## 7. RPC-Specific Considerations

When using Orleans with RPC:

1. **Separate Manifest Providers**
   - Orleans client uses Orleans manifest provider
   - RPC server uses RPC manifest provider
   - Must be registered as keyed services to avoid conflicts

2. **Assembly Loading Order**
   - Orleans grain assemblies must load before RPC setup
   - Client-side only needs interfaces
   - Server-side needs implementations

3. **Common RPC + Orleans Issues**
   - Manifest provider conflicts (use keyed services)
   - Assembly loading timing issues
   - Network isolation between RPC and Orleans

## 8. Orleans Naming Conventions

### Generated Proxy Classes vs Internal Aliases

Orleans uses a two-tier naming system:

1. **Generated Classes** (in DLL):
   - Pattern: `Proxy_<InterfaceName>`
   - Example: `Proxy_IWorldManagerGrain`
   - Namespace: `ForkleansCodeGen.<Original.Namespace>`
   - Location: Same assembly as grain interface

2. **Internal Type Aliases** (in logs/errors):
   - Pattern: `proxy_<interfacename>` (lowercase, drops "Grain" suffix)
   - Example: `proxy_iworldmanager` for `IWorldManagerGrain`
   - Used in: Error messages, placement decisions, logs

### Understanding the Alias Transformation

```
IWorldManagerGrain → Proxy_IWorldManagerGrain (generated class)
                  ↓
           proxy_iworldmanager (internal alias)
```

Key transformations:
- Remove `I` prefix and `Grain` suffix
- Convert to lowercase
- Prepend `proxy_`

## Example: Fixing "No active nodes" Error

For the error: "No active nodes are compatible with grain proxy_iworldmanager"

1. **Understand the naming**
   - `proxy_iworldmanager` is the internal alias
   - Looking for interface `IWorldManagerGrain`
   - Implementation should be `WorldManagerGrain`

2. **Verify generated proxy exists**
   - Check that `Proxy_IWorldManagerGrain` was generated
   - Look in the assembly containing `IWorldManagerGrain`
   - Use a tool like ILSpy or dotPeek to inspect the DLL

3. **Verify Silo has grain implementation**
   ```csharp
   // In Silo Program.cs - Force load grain assembly
   var grainAssembly = typeof(WorldManagerGrain).Assembly;
   Console.WriteLine($"Loaded grain assembly: {grainAssembly.FullName}");
   ```

4. **Check grain is properly inherited**
   ```csharp
   // Grain must inherit from Grain base class
   public class WorldManagerGrain : Grain, IWorldManagerGrain
   {
       // Implementation
   }
   ```

5. **Verify no assembly conflicts**
   - Check for duplicate grain assemblies
   - Ensure consistent versions across projects
   - Verify code generation succeeded during build

## 9. Debugging Tools and Techniques

### Inspecting Generated Types

Since Orleans generates types at build time, you need tools to inspect DLLs:

1. **ILSpy or dotPeek** - GUI tools for browsing .NET assemblies
2. **ildasm** - Command-line IL disassembler
3. **Reflection in code**:
   ```csharp
   var assembly = typeof(IWorldManagerGrain).Assembly;
   var generatedTypes = assembly.GetTypes()
       .Where(t => t.Namespace?.StartsWith("ForkleansCodeGen") == true);
   foreach (var type in generatedTypes)
   {
       Console.WriteLine($"Generated: {type.FullName}");
   }
   ```

4. **Proposed: DLL grep tool** - Would be helpful to have a tool that can search DLLs for generated types that don't come from source code

### Key Debugging Commands

```bash
# Find all Proxy_ types in an assembly
dotnet ildasm Shooter.Shared.dll | grep "class.*Proxy_"

# Search for grain registrations in logs
grep "Registering grain" silo.log

# Find placement errors
grep "No active nodes are compatible" *.log
```

## 10. Grain Type Resolution Classes

### Core Type Resolvers

**GrainTypeResolver**
- Associates a `GrainType` identifier with a grain class (concrete implementation)
- Uses `IGrainTypeProvider` implementations to resolve types
- Falls back to convention-based naming (removes "Grain" suffix, handles generics)

**GrainInterfaceTypeResolver**
- Associates a `GrainInterfaceType` identifier with a grain interface type
- Uses `IGrainInterfaceTypeProvider` implementations
- Default convention uses the full type name as the interface identifier

**GrainInterfaceTypeToGrainTypeResolver**
- Maps grain interfaces to their implementing grain types
- **Critical**: Depends on `IClusterManifestProvider` to get grain metadata
- Used by `IGrainFactory.GetGrain<IMyGrain>()` to find implementations

### Type Providers

**IGrainTypeProvider**
- Interface for custom grain type resolution strategies
- Implementations include `AttributeGrainTypeProvider`

**IGrainInterfaceTypeProvider**
- Interface for custom grain interface type resolution strategies  
- Implementations include `AttributeGrainInterfaceTypeProvider`

**AttributeGrainTypeProvider**
- Resolves grain types from `[GrainType("my-grain")]` attributes
- Takes precedence over convention-based naming

### Property Providers

**IGrainPropertiesProvider**
- Populates metadata properties for grain types
- Used to add diagnostic information

**TypeNameGrainPropertiesProvider**
- Adds type name information to grain/interface metadata:
  - `TypeName`: Simple class name
  - `FullTypeName`: Fully qualified type name
  - Assembly information for diagnostics

### Critical Dependency Chain

```
IGrainFactory.GetGrain<IWorldManagerGrain>()
    ↓
GrainInterfaceTypeToGrainTypeResolver
    ↓
IClusterManifestProvider (must have grain metadata)
    ↓
Returns grain type mapping or throws "No active nodes"
```

## 11. Assembly Loading

### Orleans 7.0+ Changes
- `ConfigureApplicationParts` API removed
- Use `AddSerializer` with `AddAssembly` instead
- Automatic type discovery from referenced assemblies

### Registration Pattern
```csharp
builder.Services.AddSerializer(serializerBuilder =>
{
    serializerBuilder.AddAssembly(typeof(GrainInterface).Assembly);
    serializerBuilder.AddAssembly(typeof(GrainImplementation).Assembly);
});
```

## Summary

Orleans grain discovery is a multi-step process involving:
1. Assembly scanning at startup
2. Code generation creating Proxy classes
3. Internal aliasing for grain types
4. Type registration for serialization
5. Grain registration for activation
6. Manifest distribution to cluster
7. Runtime placement decisions

The "No active nodes are compatible" error typically means:
- The silo doesn't know about the grain implementation
- OR the client's `IClusterManifestProvider` doesn't have the grain metadata
- OR the wrong `IClusterManifestProvider` is being used (e.g., RPC instead of Orleans)

Key points:
- The error message uses internal aliases (e.g., `proxy_iworldmanager`)
- The actual generated class has a different name (e.g., `Proxy_IWorldManagerGrain`)
- `GrainInterfaceTypeToGrainTypeResolver` is the critical component that maps interfaces to implementations
- It depends on having the correct `IClusterManifestProvider` with grain metadata