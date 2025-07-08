# Granville Orleans Compatibility Tools

This directory contains tools for creating compatibility shims that allow third-party Orleans packages to work with Granville Orleans.

> **Important**: After building packages, run the dependency fixing script. See `/granville/docs/PACKAGE-DEPENDENCY-FIXING.md` for details.

## Overview

Granville Orleans renames assemblies from `Microsoft.Orleans.*` to `Granville.Orleans.*` to avoid NuGet namespace conflicts. However, third-party packages like `UFX.Orleans.SignalRBackplane` expect the original Microsoft.Orleans assemblies.

We provide two compatibility approaches:

1. **Assembly Redirects** (Runtime) - Documented in `ASSEMBLY-REDIRECT-GUIDE.md`
2. **Type-Forwarding Shims** (Compile-time) - Implemented by the tools in this directory

## Type-Forwarding Shims

Type-forwarding creates Microsoft.Orleans.* packages that contain no actual implementation, but instead forward all type requests to the corresponding Granville.Orleans.* assemblies at runtime.

### Architecture

```
Third-party package
    ↓ references
Microsoft.Orleans.Core.Abstractions (shim)
    ↓ forwards types to
Granville.Orleans.Core.Abstractions (implementation)
```

This allows:
- Compile-time: Third-party packages compile against familiar Microsoft.Orleans APIs
- Runtime: Types resolve to Granville.Orleans implementations automatically
- NuGet: No conflicts between Microsoft.Orleans and Granville.Orleans packages

## Directory Structure

```
compatibility-tools/
├── README.md                              # This file
├── type-forwarding-generator/              # Type-forwarding generator tool
│   ├── GenerateTypeForwardingAssemblies.cs # Main generator source
│   ├── GenerateTypeForwardingAssemblies.csproj
│   └── GenerateTypeForwardingAssemblies.exe # Compiled generator
├── generate-individual-shims.ps1          # Script to generate shim assemblies
├── package-shims-direct.ps1              # Script to package shims (outputs to Artifacts/Release)
├── shims-proper/                          # Generated shim assemblies
│   ├── Orleans.Core.dll
│   ├── Orleans.Core.Abstractions.dll
│   └── ...
└── ASSEMBLY-REDIRECT-GUIDE.md            # Alternative approach documentation
```

## Usage

### 1. Generate Type-Forwarding Shim Assemblies

First, ensure Granville Orleans assemblies are built:

```bash
# From repository root
./granville/scripts/build-granville.ps1
```

Then generate the shim assemblies:

```bash
cd granville/compatibility-tools
./generate-individual-shims.ps1
```

This creates Orleans.*.dll files in `shims-proper/` that forward types to Granville.Orleans.*.dll assemblies.

### 2. Package Shims as NuGet Packages

```bash
./package-shims-direct.ps1
```

This creates Microsoft.Orleans.*-granville-shim.nupkg files directly in `../../Artifacts/Release/`.

## Generated Packages

The system generates these shim packages:

- `Microsoft.Orleans.Core.Abstractions` → forwards to `Granville.Orleans.Core.Abstractions`
- `Microsoft.Orleans.Core` → forwards to `Granville.Orleans.Core`
- `Microsoft.Orleans.Serialization` → forwards to `Granville.Orleans.Serialization`
- `Microsoft.Orleans.Serialization.Abstractions` → forwards to `Granville.Orleans.Serialization.Abstractions`
- `Microsoft.Orleans.Runtime` → forwards to `Granville.Orleans.Runtime`
- `Microsoft.Orleans.Server` → forwards to `Granville.Orleans.Server`
- `Microsoft.Orleans.Client` → forwards to `Granville.Orleans.Client`
- `Microsoft.Orleans.Sdk` → forwards to `Granville.Orleans.Sdk`
- `Microsoft.Orleans.Reminders` → forwards to `Granville.Orleans.Reminders`
- `Microsoft.Orleans.Persistence.Memory` → forwards to `Granville.Orleans.Persistence.Memory`
- `Microsoft.Orleans.CodeGenerator` → forwards to `Granville.Orleans.CodeGenerator`
- `Microsoft.Orleans.Analyzers` → forwards to `Granville.Orleans.Analyzers`
- `Microsoft.Orleans.Serialization.SystemTextJson` → forwards to `Granville.Orleans.Serialization.SystemTextJson`

## Type-Forwarding Generator

The generator tool (`type-forwarding-generator/GenerateTypeForwardingAssemblies.exe`) automatically:

1. **Loads** the Granville.Orleans assembly using reflection
2. **Extracts** all public types, interfaces, classes, structs, enums
3. **Handles** generic types, nested types, and complex type signatures
4. **Generates** C# source code with `[assembly: TypeForwardedTo(...)]` attributes
5. **Compiles** the source into a shim assembly with Orleans.* naming

### Example Generated Code

```csharp
using System.Runtime.CompilerServices;

[assembly: TypeForwardedTo(typeof(Orleans.IGrain))]
[assembly: TypeForwardedTo(typeof(Orleans.IGrainFactory))]
[assembly: TypeForwardedTo(typeof(Orleans.IGrainWithStringKey))]
[assembly: TypeForwardedTo(typeof(Orleans.GrainId))]
// ... 184 type forwards for Core.Abstractions
```

### Advanced Features

- **Dependency Resolution**: Automatically loads and resolves assembly dependencies
- **Generic Type Handling**: Correctly forwards generic types like `IStorage<T>`
- **Nested Type Support**: Handles nested classes and complex type hierarchies
- **Error Recovery**: Continues processing when individual types fail to load
- **Detailed Logging**: Reports exactly how many types were forwarded

## Testing the Shims

To verify the shims work correctly:

1. **Check Package Resolution**:
   ```bash
   cd granville/samples/Rpc
   dotnet restore Shooter.Shared/Shooter.Shared.csproj
   ```

2. **Verify Type Forwarding**:
   ```bash
   # Should show type forwards
   ildasm shims-proper/Orleans.Core.Abstractions.dll
   ```

3. **Test with Third-party Package**:
   ```xml
   <PackageReference Include="Microsoft.Orleans.Core.Abstractions" Version="9.1.2.51-granville-shim" />
   <PackageReference Include="UFX.Orleans.SignalRBackplane" Version="8.2.2" />
   ```

## Troubleshooting

### Empty Shim Assemblies
- **Cause**: Granville assemblies not built or not found
- **Solution**: Run `./granville/scripts/build-granville.ps1` first

### Generator Errors
- **Cause**: Assembly loading conflicts or missing dependencies
- **Solution**: Check that all Granville.Orleans.* assemblies exist and are valid

### Package Restore Failures
- **Cause**: Version mismatches or missing dependencies
- **Solution**: Clear NuGet cache: `dotnet nuget locals all --clear`

### Runtime Type Load Errors
- **Cause**: Both Microsoft.Orleans and Granville.Orleans assemblies loaded
- **Solution**: Ensure only one set of assemblies is referenced

## Implementation Notes

### Why Type-Forwarding?

1. **Compile-time Compatibility**: Third-party packages can compile against familiar APIs
2. **Runtime Efficiency**: No wrapper overhead, types resolve directly to implementations  
3. **NuGet Compatibility**: Packages can coexist without conflicts
4. **Automatic Resolution**: No manual configuration required

### Limitations

1. **Build Dependency**: Must build Granville assemblies before generating shims
2. **Version Coupling**: Shim versions must match Granville assembly versions
3. **Internal Types**: Cannot forward internal or private types
4. **Generic Constraints**: Complex generic constraints may not forward perfectly

### Future Improvements

- Automated CI integration for shim generation
- Version synchronization tools
- Enhanced error reporting and recovery
- Support for custom type mappings