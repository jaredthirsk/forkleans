# Granville Orleans Shim Build Process Guide

## Overview

This guide documents the repeatable build process for creating Orleans compatibility shims that allow third-party packages expecting Microsoft.Orleans assemblies to work with Granville.Orleans assemblies.

## Build Process

### 1. Prerequisites

- Granville Orleans assemblies must be built first (using `/granville/scripts/build-granville.ps1`)
- All assemblies are renamed from `Microsoft.Orleans.*` to `Granville.Orleans.*`

### 2. Core Build Scripts

The build process uses these essential scripts in `/granville/compatibility-tools/`:

1. **fix-all-shims.ps1** - Main entry point that:
   - Rebuilds Granville Orleans assemblies
   - Regenerates type forwarding shims
   - Compiles all shim assemblies
   
2. **compile-\*.ps1** - Individual shim compilation scripts:
   - `compile-core-shim.ps1`
   - `compile-core-abstractions-shim.ps1`
   - `compile-runtime-shim.ps1`
   - `compile-serialization-shim.ps1`
   - `compile-serialization-abstractions-shim.ps1`

3. **package-orleans-\*.ps1** - Package individual shims into NuGet packages:
   - `package-orleans-core.ps1`
   - `package-orleans-core-abstractions.ps1`
   - `package-orleans-serialization.ps1`
   - `package-orleans-serialization-abstractions.ps1`

4. **clean-all-artifacts.ps1** - Cleans all build outputs

### 3. Build Steps

```powershell
# 1. Clean previous builds
./clean-all-artifacts.ps1

# 2. Rebuild all shims (builds Granville assemblies and generates shims)
./fix-all-shims.ps1

# 3. Package shims (update version in scripts first)
./package-orleans-core.ps1
./package-orleans-core-abstractions.ps1
./package-orleans-serialization.ps1
./package-orleans-serialization-abstractions.ps1
```

### 4. Package Versioning

- Use semantic versioning: `9.1.2.XX-granville-shim`
- Increment XX for each new build
- All shim packages should use the same version number

### 5. Type Forwarding Process

The shims use .NET's TypeForwardedTo attribute to redirect type resolution:

```csharp
[assembly: TypeForwardedTo(typeof(Orleans.Runtime.ValueStopwatch))]
```

Types are forwarded from `Orleans.*` assemblies to `Granville.Orleans.*` assemblies.

## Current Issues

### OrleansCodeGen Types (Codec_GrainId)

**Problem**: Orleans code generation creates types like `OrleansCodeGen.Orleans.Runtime.Codec_GrainId` that expect to be in Microsoft.Orleans assemblies, but are actually generated in Granville.Orleans assemblies.

**Error**: 
```
Could not load type 'OrleansCodeGen.Orleans.Runtime.Codec_GrainId' from assembly 'Orleans.Core.Abstractions'
```

**Root Cause**: The Orleans source generator creates codec types in the consuming assembly, but references them as if they're in the original Orleans assemblies.

## Options for Solving Codec_GrainId Issue

### Option 1: Modify Code Generation Templates
- Modify Orleans source generators to generate types that reference Granville assemblies
- Requires changes to `/src/Orleans.CodeGenerator/`
- Most comprehensive but complex solution

### Option 2: Assembly Redirect at Runtime
- Use the existing AssemblyRedirectHelper to redirect type loading
- Already partially implemented in Shooter.Shared
- May need to handle generated types specially

### Option 3: Dual Assembly Approach
- Generate code into both Microsoft.Orleans and Granville.Orleans assemblies
- Use conditional compilation or multiple passes
- Complex but maintains compatibility

### Option 4: Type Forwarding for Generated Types
- Dynamically generate TypeForwardedTo for OrleansCodeGen types
- Challenge: Types don't exist at shim compile time
- Would need runtime generation or scanning

### Option 5: Custom Assembly Resolver
- Implement a custom assembly resolver that handles OrleansCodeGen types
- Redirect type loading requests dynamically
- Similar to Option 2 but more targeted

## Recommended Approach

The most practical solution appears to be **Option 2 (Assembly Redirect)** combined with **Option 5 (Custom Resolver)**:

1. Enhance the existing AssemblyRedirectHelper to specifically handle OrleansCodeGen types
2. When a type like `OrleansCodeGen.Orleans.Runtime.Codec_GrainId` is requested from Orleans.Core.Abstractions:
   - Intercept the type load request
   - Look for the type in Granville.Orleans.Core.Abstractions
   - Return the type from the Granville assembly

This approach:
- Leverages existing infrastructure
- Doesn't require modifying Orleans source generators
- Can be implemented incrementally
- Maintains compatibility with both assembly naming schemes

## Testing the Build

After building shims:

1. Clear NuGet cache: `dotnet nuget locals all --clear`
2. Update package versions in consuming projects
3. Test with Shooter sample: `cd granville/samples/Rpc/Shooter.Silo && dotnet run`

## Notes

- The type forwarding generator (`/type-forwarding-generator/`) automatically detects public and internal types
- InternalsVisibleTo attributes are preserved to allow forwarding of internal types
- Some types may need manual addition to shims (e.g., ReminderInstruments)