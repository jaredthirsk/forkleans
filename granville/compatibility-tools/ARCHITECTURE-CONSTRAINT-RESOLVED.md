# Architecture Constraint Resolution: Assembly Naming

## The Issue

When testing the Microsoft.Orleans.* shim assemblies with UFX.Orleans.SignalRBackplane in the Shooter sample, we encountered CS1704 errors:

```
error CS1704: An assembly with the same simple name 'Orleans.Core' has already been imported
```

## Root Cause

The issue stemmed from a misunderstanding of how official Orleans assemblies are named:

### Official Orleans Structure
- **NuGet Package Name**: `Microsoft.Orleans.Core`
- **DLL Filename**: `Orleans.Core.dll`
- **Internal AssemblyName**: `Orleans.Core` (no Microsoft prefix!)

### Initial Shim Implementation (Incorrect)
- **DLL Filename**: `Microsoft.Orleans.Core.dll`
- **Internal AssemblyName**: `Microsoft.Orleans.Core` ❌

This caused conflicts because:
1. `Granville.Orleans.Core.dll` has internal name `Orleans.Core`
2. Our shim `Microsoft.Orleans.Core.dll` had internal name `Microsoft.Orleans.Core`
3. Both assemblies were loaded, and the type forwarding tried to reference types from `Orleans.Core`
4. The compiler saw two different assemblies trying to use the same underlying assembly

## The Solution

We corrected the shim assemblies to match the official Orleans naming:

### Corrected Shim Implementation
- **NuGet Package Name**: `Microsoft.Orleans.Core` (version `9.1.2.51-granville-shim`)
- **DLL Filename**: `Microsoft.Orleans.Core.dll`
- **Internal AssemblyName**: `Orleans.Core` ✅

Now the architecture works correctly:
- UFX references our `Microsoft.Orleans.Core` package
- This provides `Microsoft.Orleans.Core.dll` with internal name `Orleans.Core`
- The shim forwards all types to `Granville.Orleans.Core.dll` (which also has internal name `Orleans.Core`)
- There's only one assembly with the name `Orleans.Core` in the compilation

## Architecture Implications

### Current Design Benefits
1. **Compatibility**: Third-party packages like UFX work seamlessly with Granville Orleans
2. **Consistency**: Maintains the same assembly naming structure as official Orleans
3. **Flexibility**: Allows either approach:
   - Use Granville Orleans with compatibility shims for third-party packages
   - Use official Orleans + Granville.Rpc packages together (no shims needed)

### Limitations
- Cannot mix official Orleans.Core and Granville.Orleans.Core in the same project (they have the same internal assembly name)
- This is by design and ensures clean separation between the two approaches

## Summary

The fundamental constraint was resolved by understanding that Orleans assemblies don't include the "Microsoft" prefix in their internal assembly names. Our shims now correctly match this pattern, enabling full compatibility with third-party Orleans packages while using Granville Orleans.