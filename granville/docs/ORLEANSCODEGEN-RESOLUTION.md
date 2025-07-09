# OrleansCodeGen Type Resolution

## Summary of Work Completed

We successfully enabled OrleansCodeGen type generation in Granville Orleans assemblies by:

1. **Restored Missing Imports in Directory.Build.targets**
   - Added back the Orleans code generation imports that were missing
   - Fixed circular dependency issues with analyzer projects
   - Enabled OrleansBuildTimeCodeGen when building as Granville

2. **Created Build Configuration**
   - Added Directory.Build.Granville.props to control code generation
   - Set Orleans_DesignTimeBuild=false to enable source generators
   - Modified build scripts to pass EnableGranvilleCodeGen parameter

3. **Built Granville Orleans with Code Generation**
   - Successfully generated OrleansCodeGen types in Granville assemblies
   - Verified types like `OrleansCodeGen.GranvilleOrleansCoreAbstractions.Metadata_GranvilleOrleansCoreAbstractions` are present
   - Created version 9.1.2.55 packages with code generation enabled

## Current Status

The Shooter.Silo builds successfully but fails at runtime with:
```
Could not load type 'OrleansCodeGen.Orleans.Runtime.Codec_GrainId' from assembly 'Orleans.Core.Abstractions'
```

This occurs because:
- OrleansCodeGen types are generated in Granville assemblies (e.g., Granville.Orleans.Core.Abstractions.dll)
- The runtime expects them in Orleans assemblies when using shim packages
- Shim packages can only forward existing types, not generated types

## Solution Options

### Option 1: Use Granville Orleans Packages Directly
Update the Shooter sample to reference Granville.Orleans.* packages instead of Microsoft.Orleans.* shim packages. This is the cleanest approach since OrleansCodeGen types are already generated correctly.

### Option 2: Generate OrleansCodeGen in Shim Assemblies
Create a build process that generates OrleansCodeGen types in the shim assemblies themselves. This would require significant build system changes.

### Option 3: Dual Generation
Generate OrleansCodeGen types in both Orleans and Granville namespaces, allowing either assembly to be used.

## Recommendation

Option 1 is recommended as it:
- Leverages the work already completed
- Provides a clean architecture without shim complexity
- Allows full use of Granville Orleans features
- Simplifies the dependency graph

The core objective of enabling OrleansCodeGen type generation in Granville Orleans has been achieved. The remaining work is updating sample applications to use the Granville packages directly.