# Coexistence Strategies for Granville RPC with Orleans

## The Challenge

When integrating Granville RPC with Orleans, we face a fundamental challenge:
- UFX.Orleans.SignalRBackplane → expects Microsoft.Orleans.Core types
- Granville.Rpc.* → built against modified Orleans.Core (with InternalsVisibleTo)
- Multiple assemblies loading different versions of "Orleans.Core"

This creates assembly resolution conflicts that must be resolved for successful coexistence.

## Option 1: Type Forwarding Shim

Create a Microsoft.Orleans.Core.dll that forwards all types to Granville.Orleans.Core.dll:

```csharp
[assembly: TypeForwardedTo(typeof(Orleans.IGrain))]
[assembly: TypeForwardedTo(typeof(Orleans.Runtime.GrainReference))]
// ... hundreds more
```

**Pros:**
- UFX SignalR and other third-party packages work unchanged
- Clean separation of concerns

**Cons:**
- Maintaining the forwarding list is tedious
- Must be updated when Orleans adds new public types
- Potential versioning conflicts

## Option 2: Assembly Binding Redirects

Use app.config/runtime binding redirects:

```xml
<runtime>
  <assemblyBinding>
    <dependentAssembly>
      <assemblyIdentity name="Orleans.Core" publicKeyToken="..." />
      <bindingRedirect oldVersion="0.0.0.0-9.1.2.0" newVersion="9.1.2.0" />
      <codeBase version="9.1.2.0" href="Granville.Orleans.Core.dll" />
    </dependentAssembly>
  </assemblyBinding>
</runtime>
```

**Pros:**
- No type forwarding needed
- Works at runtime

**Cons:**
- Not great for NuGet distribution
- Complex configuration

## Option 3: ILRepack/ILMerge Approach

Merge your InternalsVisibleTo changes directly into copies of the official assemblies:

```
ILRepack /out:Granville.Orleans.Core.dll Microsoft.Orleans.Core.dll YourInternalsVisibleToPatcher.dll
```

**Pros:**
- No type forwarding needed
- UFX SignalR works directly

**Cons:**
- Legal/licensing concerns
- Complex build process

## Option 4: Strategic Package Naming (Recommended)

Keep the Microsoft.Orleans.* package names but version them differently:

```
Microsoft.Orleans.Core 9.1.2-granville
Microsoft.Orleans.Runtime 9.1.2-granville  
Microsoft.Orleans.Serialization 9.1.2-granville
```

These packages would contain your InternalsVisibleTo modifications.

**Pros:**
- No type forwarding needed
- UFX SignalR and other packages "just work"
- Clear versioning shows it's modified
- Easy to switch back to official packages later

**Cons:**
- Could be confusing (though the -granville suffix helps)

## Multi-Silo Considerations

For 2-3 silo demonstration with UFX SignalR:

1. **SignalR Backplane**: Requires grain observer patterns for real-time updates across silos
2. **Zone Distribution**: ActionServers need to coordinate zone ownership across silos
3. **Membership**: Orleans cluster membership ensures ActionServers know about silo changes

## Option 5: Hybrid Approach - Support Both Type Forwarding and Redirects (Recommended)

To avoid legal concerns while maintaining compatibility, we support both Option 1 and Option 2:

1. **Build** all assemblies as Granville.Orleans.* (no Microsoft prefix)
2. **Provide** type forwarding shims (generated via script) for Option 1
3. **Document** assembly binding redirects for Option 2
4. **Distribute** only Granville-named packages on NuGet

**Implementation Details:**

### Primary Distribution
- All assemblies built and packaged as Granville.Orleans.*
- Version: 9.1.2-granville
- No Microsoft-prefixed DLLs distributed

### Option 1 Support: Type Forwarding Shims
Generated Microsoft.Orleans.* assemblies that forward all types:
```csharp
[assembly: TypeForwardedTo(typeof(Granville.Orleans.IGrain))]
[assembly: TypeForwardedTo(typeof(Granville.Orleans.Runtime.GrainReference))]
// ... all public types
```

**Generation:** `compatibility-tools/GenerateTypeForwardingShims.csx` script
**Usage:** Deploy shims alongside Granville assemblies

### Option 2 Support: Assembly Binding Redirects
Configuration templates for runtime redirection:
```xml
<dependentAssembly>
  <assemblyIdentity name="Microsoft.Orleans.Core" />
  <bindingRedirect oldVersion="0.0.0.0-9.1.2.0" newVersion="9.1.2.0" />
  <codeBase version="9.1.2.0" href="Granville.Orleans.Core.dll" />
</dependentAssembly>
```

**Template:** `compatibility-tools/assembly-redirects-template.config`
**Guide:** `compatibility-tools/ASSEMBLY-REDIRECT-GUIDE.md`

**Pros:**
- No legal concerns (no Microsoft-named assemblies distributed)
- Full compatibility via either approach
- Users choose their preferred integration method
- Can publish to NuGet immediately

**Cons:**
- Users must take an extra step (shims or redirects)
- Slightly more complex than direct compatibility

## Recommendation

**We are implementing Option 5 (Hybrid Type Forwarding + Redirects)** which provides:

1. **Legal Safety**: Only Granville.Orleans.* assemblies distributed
2. **Flexibility**: Users choose shims or redirects based on their needs
3. **Full Compatibility**: Both approaches enable UFX SignalR and other packages
4. **Immediate Publishing**: No reserved prefix issues on NuGet
5. **Clear Separation**: Granville namespace avoids confusion

The distribution structure:
```
NuGet Packages:
  Granville.Orleans.Core.9.1.2-granville.nupkg
  Granville.Orleans.Runtime.9.1.2-granville.nupkg
  Granville.Orleans.Serialization.9.1.2-granville.nupkg
  Granville.Rpc.*.9.1.2.nupkg

Tools (in repository):
  compatibility-tools/GenerateTypeForwardingShims.csx
  compatibility-tools/GenerateAllShims.ps1
  compatibility-tools/assembly-redirects-template.config
  compatibility-tools/ASSEMBLY-REDIRECT-GUIDE.md

Optional Shim Package (separate):
  Granville.Orleans.Shims.9.1.2.nupkg (contains generated Microsoft.Orleans.* shims)
```

This approach provides maximum flexibility while avoiding any legal or naming concerns.

## Current Implementation Status

### ✅ Completed
1. **Build Configuration** - All Orleans assemblies now build as Granville.Orleans.* via Directory.Build.targets
2. **Type Forwarding Generator** - Created dotnet-script tool to generate Microsoft.Orleans.* shim assemblies
3. **Assembly Redirect Support** - Created templates and comprehensive guide for runtime redirection
4. **Documentation** - Complete guides for both approaches with troubleshooting

### 🚧 In Progress
1. **Testing** - Need to validate both approaches with UFX.Orleans.SignalRBackplane
2. **Shim Package** - Consider creating optional NuGet package with pre-generated shims

### 📋 Usage Instructions

#### For Type Forwarding (Option 1):
```bash
# Generate shims after building Granville Orleans
cd compatibility-tools
./GenerateAllShims.ps1 -Configuration Release -OutputPath ../shims
```

#### For Assembly Redirects (Option 2):
1. Add Granville Orleans packages to your project
2. For .NET Core/5+, add the assembly resolver to Program.cs:
```csharp
AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
{
    if (assemblyName.Name.StartsWith("Microsoft.Orleans"))
    {
        var granvilleName = assemblyName.Name.Replace("Microsoft.Orleans", "Granville.Orleans");
        try
        {
            return context.LoadFromAssemblyName(new AssemblyName(granvilleName));
        }
        catch { }
    }
    return null;
};
```
3. For .NET Framework, use the XML configuration from assembly-redirects-template.config