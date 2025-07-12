# Orleans Serialization Shim Package Issue

## Problem Summary

The Shooter.Silo fails to start with the error:
```
Orleans.Serialization.CodecNotFoundException: Could not find a copier for type Orleans.Serialization.Invocation.Response`1[Orleans.MembershipTableData]
```

This occurs even though:
1. We've added InternalsVisibleTo attributes to allow Orleans shim assemblies to access internal types in Granville assemblies
2. The Orleans.Serialization shim includes TypeForwardedTo for PooledResponseCopier and PooledResponseActivator
3. The actual types exist in Granville.Orleans.Serialization with the correct Orleans namespaces

## User Requirements

Per user instructions:
- Keep Microsoft.Orleans shim packages (don't remove them)
- Use InternalsVisibleTo to overcome internal visibility issues
- Don't change Orleans source files except for InternalsVisibleTo attributes
- Don't change the namespace - keep `Orleans.Serialization.Invocation.Response<T>` (not Granville prefix)

## Technical Analysis

### 1. Assembly and Type Locations

**IDeepCopier<T> Interface**
- Assembly: `Orleans.Serialization.Abstractions.dll` (shim) → `Granville.Orleans.Serialization.Abstractions.dll`
- Namespace: `Orleans.Serialization.Cloning`
- Visibility: Public interface

**Response<T> Class**
- Assembly: `Orleans.Serialization.dll` (shim) → `Granville.Orleans.Serialization.dll`
- Namespace: `Orleans.Serialization.Invocation`
- Visibility: Public abstract class

**PooledResponseCopier<T> Class**
- Assembly: `Granville.Orleans.Serialization.dll`
- Namespace: `Orleans.Serialization.Invocation`
- Visibility: Internal sealed class
- Implements: `IDeepCopier<Response<TResult>>`
- Attribute: `[RegisterCopier]`

**MembershipTableData Class**
- Assembly: `Orleans.Core.dll` (shim) → `Granville.Orleans.Core.dll`
- Namespace: `Orleans`
- Visibility: Public sealed class
- Attributes: `[Serializable]`, `[GenerateSerializer]`

### 2. How [RegisterCopier] Works and Metadata Embedding

The `[RegisterCopier]` attribute triggers compile-time code generation:

1. **During Compilation** of `Granville.Orleans.Serialization.dll`:
   - Orleans.CodeGenerator scans for types with `[RegisterCopier]`
   - Finds `PooledResponseCopier<T>` (internal class)
   - Generates a metadata class that inherits from `TypeManifestProviderBase`
   - This generated class is embedded in `Granville.Orleans.Serialization.dll`

2. **Generated Metadata** (embedded in Granville.Orleans.Serialization.dll):
   ```csharp
   // Generated code (approximate)
   internal sealed class OrleansSerializationMetadata : TypeManifestProviderBase
   {
       public OrleansSerializationMetadata() : base(new TypeManifestOptions
       {
           Copiers = new[]
           {
               new CopierMetadata(
                   typeof(Orleans.Serialization.Invocation.Response<>), 
                   typeof(Orleans.Serialization.Invocation.PooledResponseCopier<>))
           }
       })
       { }
   }
   ```

3. **Runtime Registration**:
   - When `Granville.Orleans.Serialization.dll` is loaded
   - The metadata class is discovered and instantiated
   - Copiers are registered in the `CodecProvider`
   - The registration maps: `Response<T>` → `PooledResponseCopier<T>`

### 3. Why the Current Approach Fails

**The Core Issue**: The generated metadata is embedded in `Granville.Orleans.Serialization.dll`, but the runtime is looking for copiers through the Orleans.Serialization shim assembly.

**Sequence of Events**:
1. Orleans runtime needs to copy `Response<MembershipTableData>`
2. It asks CodecProvider for a copier for type `Orleans.Serialization.Invocation.Response<Orleans.MembershipTableData>`
3. CodecProvider looks in assemblies loaded in the current context
4. The Orleans.Serialization shim assembly has no metadata (it's just type forwards)
5. The metadata is in Granville.Orleans.Serialization.dll, but:
   - It's registered under the assembly's context
   - The lookup happens before our custom serialization configuration runs

**Why TypeForwardedTo + InternalsVisibleTo Doesn't Work**:
- TypeForwardedTo can forward the type definition
- InternalsVisibleTo allows access to internal types
- BUT: The generated metadata class and its registration data cannot be forwarded

### 3.1. Orleans Startup Flow and Serialization Discovery Timing

The critical issue is **when** Orleans discovers and validates serialization metadata versus **when** our custom assembly registration occurs. Here's the detailed startup flow:

#### Application Startup Sequence

1. **Program.cs Execution**:
   ```
   Program.cs:140-146: Early assembly loading (our workaround)
   Program.cs:150-162: AddSerializer() call with AddGranvilleAssemblies()
   Program.cs:165-168: TypeManifestOptions configuration
   Program.cs:180: app = builder.Build() ← Critical point
   ```

2. **WebApplication.Build() Execution**:
   ```
   WebApplicationBuilder.Build()
   ├── ServiceCollection.BuildServiceProvider()
   ├── Orleans.Hosting.SiloHostedService construction
   └── Configuration validation (WHERE THE ERROR OCCURS)
   ```

3. **Orleans SiloHostedService Construction Stack** (where the error occurs):
   ```csharp
   Orleans.Hosting.SiloHostedService..ctor(Silo silo, IEnumerable<IConfigurationValidator> configurationValidators, ILogger logger)
   ├── ValidateSystemConfiguration(configurationValidators) ← LINE 41
   │   └── Orleans.SerializerConfigurationValidator.ValidateConfiguration() ← LINE 60
   │       └── Orleans.Serialization.SerializerConfigurationAnalyzer.AnalyzeSerializerAvailability(codecProvider, options) ← LINE 44
   │           └── VisitType(typeof(ImmutableArray<GrainManifest>), methodInfo, context) ← LINE 63
   │               └── Orleans.Serialization.Serializers.CodecProvider.TryGetCodec(fieldType) ← LINE 169
   │                   └── Orleans.Serialization.Serializers.CodecProvider.GetCodec<TField>() ← LINE 201
   │                       └── Orleans.Serialization.ServiceCollectionExtensions.FieldCodecHolder<T>.get_Value() ← LINE 152
   │                           └── Orleans.Serialization.GeneratedCodeHelpers.OrleansGeneratedCodeHelper.GetService<TService>(caller, codecProvider) ← LINE 75
   │                               └── OrleansCodeGen.Orleans.Metadata.Codec_ClusterManifest..ctor(IActivator<ImmutableArraySurrogate<GrainManifest>> _activator, ICodecProvider codecProvider) ← LINE 3221
   │                                   └── EXCEPTION: Could not find a codec for type System.Collections.Immutable.ImmutableArray`1[Orleans.Metadata.GrainManifest]
   ```

#### Critical Timing Issue

**The Problem**: Orleans serialization validation happens **during service provider construction** (`builder.Build()`), which occurs **before** any of our runtime workarounds can execute.

**What Happens**:
1. **Too Early**: Service provider construction triggers `SiloHostedService` creation
2. **Validation Runs**: `SerializerConfigurationValidator` analyzes all serializable types
3. **Codec Discovery**: Tries to find codec for `ImmutableArray<GrainManifest>`
4. **Missing Metadata**: Only shim assemblies are discovered, no metadata found
5. **Exception Thrown**: Before any of our runtime assembly registration can run

**What We Expected**:
1. Service provider construction completes
2. Our `AddGranvilleAssemblies()` configuration takes effect
3. Granville assemblies are registered with serializer
4. Orleans startup proceeds with all assemblies available

#### Why Our Workarounds Don't Work

1. **Early Assembly Loading** (`Program.cs:142-146`):
   - Loads assemblies into AppDomain
   - But Orleans metadata discovery is assembly-attribute based
   - Loaded assemblies without `[ApplicationPart]` are ignored

2. **AddSerializer() Configuration** (`Program.cs:150-162`):
   - Configures the serializer correctly
   - But validation happens during service construction, before configuration applies

3. **Explicit Assembly Registration** (`AddGranvilleAssemblies()`):
   - Would work if it ran before validation
   - But it's part of the serializer configuration that applies later

#### The Assembly Discovery Process

Orleans discovers assemblies through this process during service construction:

```csharp
// In Orleans.Serialization.Hosting.ServiceCollectionExtensions.AddSerializer (line 40)
foreach (var asm in ReferencedAssemblyProvider.GetRelevantAssemblies())
{
    context.Builder.AddAssembly(asm);  // This is where our assemblies should be added
}
```

The `ReferencedAssemblyProvider.GetRelevantAssemblies()` method:
1. Gets assemblies from `AppDomain.CurrentDomain.GetAssemblies()`
2. Filters to only those with `[ApplicationPart]` attribute
3. **Does NOT** follow `TypeForwardedTo` attributes
4. **Does NOT** include our explicitly loaded Granville assemblies (they lack `[ApplicationPart]`)

#### Stack Trace Analysis

From the actual error during Shooter.Silo startup:

```
Orleans.Serialization.CodecNotFoundException: Could not find a codec for type System.Collections.Immutable.ImmutableArray`1[Orleans.Metadata.GrainManifest].
   at Orleans.Serialization.Serializers.CodecProvider.ThrowCodecNotFound(Type fieldType) in CodecProvider.cs:line 672
   at Orleans.Serialization.Serializers.CodecProvider.GetCodec[TField]() in CodecProvider.cs:line 201
   at Orleans.Serialization.ServiceCollectionExtensions.FieldCodecHolder`1.get_Value() in ServiceCollectionExtensions.cs:line 152
   at Orleans.Serialization.GeneratedCodeHelpers.OrleansGeneratedCodeHelper.GetService[TService](Object caller, ICodecProvider codecProvider) in OrleansGeneratedCodeHelper.cs:line 75
   at OrleansCodeGen.Orleans.Metadata.Codec_ClusterManifest..ctor(IActivator`1 _activator, ICodecProvider codecProvider) in Granville.Orleans.Core.Abstractions.orleans.g.cs:line 3221
   ...
   at Orleans.SerializerConfigurationValidator.Orleans.IConfigurationValidator.ValidateConfiguration() in SerializerConfigurationValidator.cs:line 60
   at Orleans.Hosting.SiloHostedService.ValidateSystemConfiguration(IEnumerable`1 configurationValidators) in SiloHostedService.cs:line 41
   at Orleans.Hosting.SiloHostedService..ctor(Silo silo, IEnumerable`1 configurationValidators, ILogger`1 logger) in SiloHostedService.cs:line 20
```

This confirms:
1. The error occurs during `SiloHostedService` constructor
2. Which happens during service provider construction (`builder.Build()`)
3. Before any runtime configuration or our workarounds can take effect
4. The generated code `Codec_ClusterManifest` needs `ImmutableArray<GrainManifest>` codec
5. The codec exists in `Granville.Orleans.Serialization.dll` but isn't discovered

#### Why ImmutableArray<GrainManifest> Specifically

The `ClusterManifest` type is generated by Orleans code generation and contains:
```csharp
[GenerateSerializer]
public sealed class ClusterManifest
{
    [Id(0)]
    public ImmutableArray<GrainManifest> Grains { get; set; }  // ← This field needs ImmutableArray codec
    // ... other fields
}
```

The Orleans code generator creates `Codec_ClusterManifest` which needs:
- A codec for `ImmutableArray<GrainManifest>`
- This codec is `ImmutableArrayCodec<GrainManifest>` with `[RegisterSerializer]` attribute
- Located in `Granville.Orleans.Serialization.dll`
- But the discovery process only sees shim assemblies, not Granville assemblies
- The [RegisterCopier] attribute's effect (the generated code) stays in the original assembly

### 4. Why Orleans.Persistence.Memory Needs InternalsVisibleTo

**The Dependency Chain**:
1. `Microsoft.Orleans.Persistence.Memory.dll` (official NuGet package)
   - References: Orleans.Runtime (expecting Microsoft's version)
   - Uses internal types from Orleans.Runtime for grain storage

2. At runtime in our setup:
   - Orleans.Persistence.Memory.dll is loaded (Microsoft's)
   - Orleans.Runtime.dll (shim) forwards to Granville.Orleans.Runtime.dll
   - If Orleans.Persistence.Memory needs internal types, it fails without InternalsVisibleTo

**Specific Types That May Need Access**:
- Internal storage interfaces in Orleans.Runtime
- Internal serialization helpers
- Internal types used in grain state management

However, this might not solve our immediate issue because Orleans.Persistence.Memory doesn't contain the copier for Response<MembershipTableData>.

## Proposed Solution Details

### Step 1: Document the Problem (Completed)
This document explains the technical details of why the shim approach fails for internal copiers.

### Step 2: Remove Invalid TypeForwardedTo
The entries for PooledResponseCopier and PooledResponseActivator should be removed from Orleans.Serialization shim because:
- They're internal types
- TypeForwardedTo only works for types that are publicly accessible
- Even with InternalsVisibleTo, the forwarding mechanism doesn't support internal types

### Step 3: Add Orleans.Persistence.Memory to InternalsVisibleTo
While this won't solve the immediate copier issue, it's needed because:
- Orleans.Persistence.Memory is a Microsoft assembly we're using directly
- It may need access to internal types in Granville.Orleans.* assemblies
- Without this, we might encounter other runtime errors after fixing the copier issue

### Step 4: Create a Runtime Workaround
Since we can't change the architecture, we need a workaround that:
1. Happens very early in the startup process (before Orleans initialization)
2. Manually registers the copier for the specific problematic type
3. Uses reflection to access the internal PooledResponseCopier from Granville.Orleans.Serialization
4. Registers it in a way that Orleans' CodecProvider will find it

## Alternative Approaches Considered

1. **Making PooledResponseCopier public**: Rejected per user requirements (no changes to Orleans source except InternalsVisibleTo)
2. **Removing shim packages**: Rejected per user requirements (must keep shim packages)
3. **Custom serializer configuration**: Attempted but fails because the error occurs before configuration runs
4. **Assembly redirect at runtime**: Complex and might not work with the generated metadata

## How Orleans Discovers Codecs at Runtime

### Assembly Discovery Process

1. **Initial Assembly Loading** (`ServiceCollectionExtensions.AddSerializer` line 40):
```csharp
foreach (var asm in ReferencedAssemblyProvider.GetRelevantAssemblies())
{
    context.Builder.AddAssembly(asm);
}
```

2. **Assembly Filtering** (`ReferencedAssemblyProvider.GetRelevantAssemblies`):
   - Loads assemblies from current AppDomain
   - Only includes assemblies marked with `[ApplicationPart]` attribute
   - Does NOT follow TypeForwardedTo - it works with loaded assemblies directly

3. **Metadata Discovery** (`SerializerBuilderExtensions.AddAssembly` line 60):
```csharp
var attrs = assembly.GetCustomAttributes<TypeManifestProviderAttribute>();
foreach (var attr in attrs)
{
    builder.Services.AddSingleton(typeof(IConfigureOptions<TypeManifestOptions>), attr.ProviderType);
}
```

4. **Runtime Codec Lookup** (`CodecProvider.TryCreateCopier` line 341):
```csharp
private IDeepCopier TryCreateCopier(Type fieldType)
{
    if (!_initialized) Initialize();
    
    // Try to create from registered metadata
    if (CreateCopierInstance(fieldType, ...) is { } res)
        return res;
    
    // Fall back to specialized/generalized copiers
    // ...
}
```

### The Critical Gap

The problem is in the metadata discovery phase. When Orleans.Serialization (shim) is loaded:
1. It has NO `[TypeManifestProvider]` attribute
2. It has NO generated metadata classes
3. TypeForwardedTo only forwards type definitions, NOT assembly attributes or generated classes
4. The actual metadata is in Granville.Orleans.Serialization, which isn't checked

### Current ImmutableArray<GrainManifest> Error

The same issue affects `ImmutableArrayCodec<T>` and `ImmutableArrayCopier<T>`:
- These are defined in Granville.Orleans.Serialization
- The metadata registering them is also in Granville.Orleans.Serialization
- The shim has no way to make this metadata discoverable

## Solution Implemented: Making Orleans Smarter

Based on the user's suggestion, we've modified Orleans to follow TypeForwardedTo when discovering metadata:

### 1. **SerializerBuilderExtensions.AddAssembly** (Modified)
```csharp
// Check for TypeForwardedTo attributes and add the target assemblies
var forwardedTypes = assembly.GetCustomAttributes<System.Runtime.CompilerServices.TypeForwardedToAttribute>();
var processedAssemblies = new HashSet<Assembly> { assembly };

foreach (var forwarded in forwardedTypes)
{
    var targetAssembly = forwarded.Destination.Assembly;
    if (processedAssemblies.Add(targetAssembly))
    {
        // Recursively add the target assembly to discover its metadata
        builder.AddAssembly(targetAssembly);
    }
}
```

### 2. **ReferencedAssemblyProvider.AddAssembly** (Modified)
```csharp
// Add assemblies that this assembly forwards types to
try
{
    var forwardedTypes = assembly.GetCustomAttributes<System.Runtime.CompilerServices.TypeForwardedToAttribute>();
    foreach (var forwarded in forwardedTypes)
    {
        var targetAssembly = forwarded.Destination.Assembly;
        if (targetAssembly != assembly) // Avoid self-reference
        {
            AddAssembly(parts, targetAssembly);
        }
    }
}
catch
{
    // Ignore errors when checking for forwarded types
}
```

This solution makes the shim approach work transparently - when Orleans processes a shim assembly, it automatically discovers and includes the Granville assemblies that contain the actual implementations and metadata.

### Update: Limitations Discovered

During testing, we found that the TypeForwardedTo discovery approach has a limitation:

1. **Timing Issue**: The serialization error occurs during Orleans initialization (specifically during membership table initialization) before the assembly discovery phase runs.

2. **ApplicationPart Requirement**: Assemblies need the `[ApplicationPart]` attribute to be discovered by Orleans, but the shim assemblies can't easily add this attribute without referencing the Granville assemblies.

3. **Early Serialization**: Orleans attempts to serialize `Response<MembershipTableData>` objects very early in the startup process, before user configuration or assembly discovery completes.

### Alternative Solutions Needed

Given these limitations, we need solutions that work with the early serialization requirement:

## Solution Options

### Option 1: Pre-load Granville Assemblies (Module Initializer)

**Approach**: Use a module initializer in the shim assemblies to force-load Granville assemblies before Orleans initialization.

```csharp
// In Orleans.Serialization shim
using System.Runtime.CompilerServices;

internal static class ModuleInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Force load the Granville assembly
        _ = typeof(Granville.Orleans.Serialization.Serializer).Assembly;
    }
}
```

**Pros**:
- Simple to implement
- Works automatically when shim is loaded
- No changes to application code

**Cons**:
- Module initializers require .NET 5+
- Might not run early enough if serialization happens in static constructors
- Creates a hard dependency from shim to Granville assembly

### Option 2: Assembly Redirect Hook

**Approach**: Hook into AppDomain.AssemblyResolve to redirect assembly lookups.

```csharp
// In application startup
AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
{
    var name = new AssemblyName(args.Name);
    if (name.Name?.StartsWith("Orleans.") == true)
    {
        var granvilleName = name.Name.Replace("Orleans.", "Granville.Orleans.");
        return Assembly.Load(granvilleName);
    }
    return null;
};
```

**Pros**:
- Works at the assembly resolution level
- Can handle all Orleans assemblies uniformly

**Cons**:
- In .NET Core/5+, AssemblyResolve only fires when assembly load fails
- Requires changes in end-developer application code
- May not work for already-loaded assemblies

### Option 3: Modified Shim with Minimal Metadata Provider

**Approach**: Include a minimal TypeManifestProvider in the shim that redirects to Granville assemblies.

```csharp
// In Orleans.Serialization shim
[assembly: ApplicationPart]
[assembly: TypeManifestProvider(typeof(GranvilleRedirectMetadata))]

internal sealed class GranvilleRedirectMetadata : IConfigureOptions<TypeManifestOptions>
{
    public void Configure(TypeManifestOptions options)
    {
        // Load the actual Granville assembly and its metadata
        var granvilleAssembly = Assembly.Load("Granville.Orleans.Serialization");
        var providers = granvilleAssembly
            .GetCustomAttributes<TypeManifestProviderAttribute>();
        
        foreach (var provider in providers)
        {
            var instance = Activator.CreateInstance(provider.ProviderType) 
                as IConfigureOptions<TypeManifestOptions>;
            instance?.Configure(options);
        }
    }
}
```

**Pros**:
- Works within Orleans' metadata discovery system
- No changes to application code
- Elegant solution that only requires shim modifications
- Follows Orleans patterns

**Cons**:
- Requires ApplicationPart attribute on shim
- Creates runtime dependency from shim to Granville
- More complex than other options

### Option 4: Early Serializer Configuration

**Approach**: Configure the serializer very early in the application lifecycle.

```csharp
// In Program.cs, before any Orleans code
static class Program
{
    static Program()
    {
        // Static constructor runs before Main
        ConfigureSerializer();
    }
    
    static void ConfigureSerializer()
    {
        // Manually register Granville assemblies
        // This is complex and requires internal knowledge
    }
}
```

**Pros**:
- Runs very early
- Direct control over configuration

**Cons**:
- Requires changes in every application using Granville
- Complex to implement correctly
- May still be too late for some scenarios

### Option 5: Custom Host Builder Extension

**Approach**: Provide a Granville-specific host builder extension that handles early configuration.

```csharp
// Application uses this instead of UseOrleans
Host.CreateDefaultBuilder(args)
    .UseGranvilleOrleans(siloBuilder =>
    {
        // Normal Orleans configuration
    });
```

**Pros**:
- Clean API for applications
- Full control over initialization order
- Can handle all Granville-specific setup

**Cons**:
- Requires changes to application code
- Another API for developers to learn
- May not integrate well with existing Orleans extensions

## Recommended Solution: Option 3

Option 3 (Modified Shim with Minimal Metadata Provider) is the most elegant solution because:
1. It only requires changes in the shim code, not in end-developer applications
2. It works within Orleans' existing metadata discovery system
3. It's transparent to the application - the shim "just works"
4. It follows Orleans patterns and conventions

### Implementation Challenges

During implementation of Option 3, we discovered additional challenges:

1. **Circular Dependencies**: The enhanced shims need to reference both Orleans types (for ApplicationPart) and Granville assemblies (for metadata), creating circular dependencies.

2. **Build Order Issues**: The shims must be built before Granville assemblies (which reference the shims), but the enhanced shims need Granville assemblies to exist.

3. **Type Forwarding Limitations**: TypeForwardedTo attributes cannot forward assembly-level attributes like ApplicationPart or TypeManifestProvider.

## Current Workaround

The current workaround is provided by the `Granville.Orleans.Shims` package:

### Using Granville.Orleans.Shims Package (Recommended)

```csharp
using Granville.Orleans.Shims;

var builder = WebApplication.CreateBuilder(args);

// Add Orleans shim compatibility
builder.Services.AddOrleansShims();

// Or with custom serializer configuration
builder.Services.AddOrleansShims(serializerBuilder =>
{
    // Add your application assemblies
    serializerBuilder.AddAssembly(typeof(YourGrain).Assembly);
});
```

### Manual Workaround

If you can't use the package, you can manually register Granville assemblies:

```csharp
builder.Services.AddSerializer(serializerBuilder =>
{
    // Force load and register Granville assemblies
    serializerBuilder.AddAssembly(typeof(Orleans.Serialization.Serializer).Assembly);
    serializerBuilder.AddAssembly(typeof(Orleans.Grain).Assembly);
    serializerBuilder.AddAssembly(typeof(Orleans.IGrain).Assembly);
    // Add your application assemblies
    serializerBuilder.AddAssembly(typeof(YourGrain).Assembly);
});
```

### Benefits of Granville.Orleans.Shims Package

1. **Encapsulation**: Hides the workaround complexity from application code
2. **Maintainability**: Single place to update if a better solution is found
3. **Convenience**: Simple extension method API
4. **Future-proof**: Can be updated transparently when Orleans or Granville changes

## Future Work

Potential long-term solutions include:

1. **Two-Phase Build Process**: Build basic shims first, then Granville assemblies, then enhanced shims.
2. **Post-Build Enhancement**: Use IL rewriting tools to add metadata providers to shims after compilation.
3. **Orleans Core Modification**: Propose changes to Orleans to better support assembly forwarding scenarios.

## Option 2 Implementation Results

Based on implementing Option 2 (Fix Assembly Discovery), we discovered:

### What Works:
1. **Removing SerializerConfigurationValidator allows silo to start**
   - Successfully removes the validator from service collection
   - Bypasses early validation that was blocking startup
   - Silo reaches "Started" state

2. **Assembly loading is partially successful**
   - All Granville assemblies can be loaded and added to serializer
   - Assemblies contain TypeManifestProvider metadata
   - ImmutableArrayCodec is found with proper attributes

### What Doesn't Work:
1. **Granville assemblies lack [ApplicationPart] attribute**
   - None of the Granville.Orleans.* assemblies have this attribute
   - This prevents Orleans' default discovery from finding them
   - Even when explicitly added, runtime codec lookup still fails

2. **Runtime copier discovery still fails**
   - Error occurs during membership table operations
   - Cannot find copier for `Response<MembershipTableData>`
   - The metadata exists but isn't accessible at runtime

### Key Insight:
The issue has two parts:
1. **Early validation** (solved by removing validator)
2. **Runtime codec discovery** (still unsolved)

Even with assemblies loaded and added to the serializer, the runtime codec provider cannot find the copiers. This suggests the issue is deeper in how Orleans' codec provider resolves types at runtime.

## Conclusion

The fundamental issue is an architectural mismatch between:
- How Orleans generates and embeds copier metadata at compile time
- How the shim package approach splits type definitions from their implementations
- When Orleans needs these copiers (very early, before user configuration)
- Orleans doesn't follow TypeForwardedTo when discovering metadata

While we've attempted to make Orleans smarter by following TypeForwardedTo, the timing issue means we need a solution that works within the existing architecture. The current workaround of explicit assembly registration, while not as elegant as we'd like, provides a reliable solution for applications using Granville Orleans.

### Recommended Approach:
1. **For Development**: Remove SerializerConfigurationValidator to bypass validation
2. **For Production**: Complete the Granville.Orleans.Shims package with proper metadata forwarding
3. **Long-term**: Consider adding [ApplicationPart] to Granville assemblies or implementing a custom codec provider