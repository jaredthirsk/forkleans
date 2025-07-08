# Type-Forwarding Shim Packages Guide

This guide explains how the Microsoft.Orleans.* shim packages work and how to use them in the Granville Orleans ecosystem.

> **IMPORTANT**: Only 5 Orleans assemblies were modified and need shims. However, Microsoft.Orleans.CodeGenerator also requires a shim because it needs compile-time type resolution. See the guidance below.

## Which Assemblies Need Shims?

**Only 5 Orleans assemblies were modified** in the Granville fork (with InternalsVisibleTo attributes):

1. **Orleans.Core.Abstractions**
2. **Orleans.Core**
3. **Orleans.Runtime**
4. **Orleans.Serialization.Abstractions**
5. **Orleans.Serialization**

**Additionally, Microsoft.Orleans.CodeGenerator requires a shim** because:
- Code generators need to resolve types at compile time
- Type forwarding only works at runtime, not during compilation
- The code generator must find types like `Orleans.ApplicationPartAttribute` to generate code

**All other Orleans assemblies** (Server, Client, Persistence.Memory, Reminders, etc.) were NOT modified and should use official Microsoft.Orleans packages from NuGet.

## What Are Shim Packages?

Shim packages are special NuGet packages with names like `Microsoft.Orleans.Core.9.1.2.53-granville-shim` that:
- Have the same package IDs as official Microsoft.Orleans packages
- Contain assemblies that forward all types to corresponding Granville.Orleans assemblies
- Allow third-party packages expecting Microsoft.Orleans to work with Granville.Orleans
- **Should only be used for the 5 modified assemblies listed above**

## How Shims Work

### Architecture
```
Third-party Package (e.g., UFX.Orleans.SignalRBackplane)
    ↓ references
Microsoft.Orleans.Core (shim package)
    ↓ contains Orleans.Core.dll that forwards types to
Granville.Orleans.Core (actual implementation)
```

### Type Forwarding Example
The shim's Orleans.Core.dll contains:
```csharp
[assembly: TypeForwardedTo(typeof(Orleans.IGrain))]
[assembly: TypeForwardedTo(typeof(Orleans.IGrainFactory))]
// ... hundreds more type forwards
```

At runtime, when code tries to use `Orleans.IGrain`, it's automatically redirected to the implementation in `Granville.Orleans.Core.dll`.

## Using Shim Packages

### Correct Approach: Mix Shims and Official Packages

```xml
<ItemGroup>
  <!-- Shim packages for the 5 modified assemblies + CodeGenerator -->
  <PackageVersion Include="Microsoft.Orleans.Core" Version="9.1.2.53-granville-shim" />
  <PackageVersion Include="Microsoft.Orleans.Core.Abstractions" Version="9.1.2.53-granville-shim" />
  <PackageVersion Include="Microsoft.Orleans.Runtime" Version="9.1.2.53-granville-shim" />
  <PackageVersion Include="Microsoft.Orleans.Serialization" Version="9.1.2.53-granville-shim" />
  <PackageVersion Include="Microsoft.Orleans.Serialization.Abstractions" Version="9.1.2.53-granville-shim" />
  <PackageVersion Include="Microsoft.Orleans.CodeGenerator" Version="9.1.2.53-granville-shim" />  <!-- Needs shim for compile-time type resolution -->
  
  <!-- Official Microsoft packages for ALL OTHER assemblies -->
  <PackageVersion Include="Microsoft.Orleans.Server" Version="9.1.2" />
  <PackageVersion Include="Microsoft.Orleans.Client" Version="9.1.2" />
  <PackageVersion Include="Microsoft.Orleans.Persistence.Memory" Version="9.1.2" />
  <PackageVersion Include="Microsoft.Orleans.Reminders" Version="9.1.2" />
  <PackageVersion Include="Microsoft.Orleans.Serialization.SystemTextJson" Version="9.1.2" />
  <PackageVersion Include="Microsoft.Orleans.Sdk" Version="9.1.2" />
  <PackageVersion Include="Microsoft.Orleans.Analyzers" Version="9.1.2" />
  
  <!-- Granville Orleans packages (for modified assemblies + CodeGenerator) -->
  <PackageVersion Include="Granville.Orleans.Core" Version="9.1.2.53" />
  <PackageVersion Include="Granville.Orleans.Core.Abstractions" Version="9.1.2.53" />
  <PackageVersion Include="Granville.Orleans.Runtime" Version="9.1.2.53" />
  <PackageVersion Include="Granville.Orleans.Serialization" Version="9.1.2.53" />
  <PackageVersion Include="Granville.Orleans.Serialization.Abstractions" Version="9.1.2.53" />
  <PackageVersion Include="Granville.Orleans.CodeGenerator" Version="9.1.2.53" />
</ItemGroup>
```

### Option 2: Mix Official and Shim Packages

For third-party packages, you might need to use shims selectively:

```xml
<ItemGroup>
  <!-- Use official Microsoft.Orleans for most packages -->
  <PackageVersion Include="Microsoft.Orleans.Core" Version="9.1.2" />
  <PackageVersion Include="Microsoft.Orleans.Core.Abstractions" Version="9.1.2" />
  
  <!-- But use shims for packages that third-party libraries depend on -->
  <PackageVersion Include="Microsoft.Orleans.Runtime" Version="9.1.2.51-granville-shim" />
  <PackageVersion Include="Microsoft.Orleans.Server" Version="9.1.2.51-granville-shim" />
</ItemGroup>
```

**Note**: This approach requires careful management to avoid conflicts.

## Building Shim Packages

### Prerequisites
1. Build Granville Orleans assemblies first:
   ```bash
   ./granville/scripts/build-granville.ps1
   ```

2. Build the type-forwarding generator:
   ```bash
   cd granville/compatibility-tools/type-forwarding-generator
   dotnet build -c Release
   ```

### Generate Shim Assemblies (Minimal Approach - Recommended)
```bash
cd granville/compatibility-tools
./generate-minimal-shims.ps1
```

This creates Orleans.*.dll files in `shims-proper/` for ONLY the 5 modified assemblies.

### Package Shims (Minimal Approach - Recommended)
```bash
./package-minimal-shims.ps1
```

This creates Microsoft.Orleans.*-granville-shim.nupkg files for ONLY the 5 modified assemblies.

### Legacy Full Shim Approach
If you need shims for all Orleans assemblies (not recommended):
```bash
./generate-individual-shims.ps1
./package-shims-direct.ps1
```

See [MINIMAL-SHIMS-APPROACH.md](/granville/compatibility-tools/MINIMAL-SHIMS-APPROACH.md) for why the minimal approach is preferred.

## Package Output Location

All NuGet packages are output to `/Artifacts/Release/`:
- Granville.Orleans.* packages (from build-granville.ps1)
- Granville.Rpc.* packages (from build-granville-rpc-packages.ps1)
- Microsoft.Orleans.*-granville-shim packages (from package-shims-direct.ps1)

This directory serves as a local NuGet feed for testing.

## Available Shim Packages

The following shim packages are available:
- `Microsoft.Orleans.Core.Abstractions` (184 type forwards)
- `Microsoft.Orleans.Core` (146 type forwards)
- `Microsoft.Orleans.Serialization` (312 type forwards)
- `Microsoft.Orleans.Serialization.Abstractions` (31 type forwards)
- `Microsoft.Orleans.Runtime` (80 type forwards)
- `Microsoft.Orleans.Server`
- `Microsoft.Orleans.Sdk`
- `Microsoft.Orleans.CodeGenerator`
- `Microsoft.Orleans.Analyzers` (12 type forwards)
- `Microsoft.Orleans.Reminders`
- `Microsoft.Orleans.Persistence.Memory`
- `Microsoft.Orleans.Serialization.SystemTextJson`

## Troubleshooting

### "Type could not be found" errors
If you get errors about types being forwarded but not found:
1. Ensure Granville.Orleans packages are referenced
2. Check that versions match (shim version should align with Granville version)
3. Clean and rebuild the solution

### Code generator conflicts
If using shims with code generators:
1. Exclude analyzers from one set of packages
2. See [code-generator-conflicts.md](code-generator-conflicts.md) for detailed solutions

### Package restore issues
1. Clear NuGet cache: `dotnet nuget locals all --clear`
2. Ensure `/Artifacts/Release/` is configured as a package source in NuGet.config
3. Check that shim package versions match what's specified in Directory.Packages.props

## When to Use Shims vs Assembly Redirects

### Use Shims When:
- You need compile-time compatibility with third-party packages
- You want a cleaner solution without runtime redirects
- You're building packages that others will consume

### Use Assembly Redirects When:
- You're building an end application (not a library)
- You want to use official Microsoft.Orleans packages directly
- You need more control over the redirect behavior

See [ASSEMBLY-REDIRECT-GUIDE.md](/granville/compatibility-tools/ASSEMBLY-REDIRECT-GUIDE.md) for the assembly redirect approach.

## Related Documentation
- [Repository Organization](/granville/REPO-ORGANIZATION.md)
- [Code Generator Conflicts](code-generator-conflicts.md)
- [Compatibility Tools README](/granville/compatibility-tools/README.md)