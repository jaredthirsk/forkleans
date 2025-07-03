# Orleans Code Generation Issues in Shooter Sample

## Problem Summary

When attempting to build the Shooter.Shared project with Microsoft.Orleans 9.1.2 packages, we encounter CS0101 errors indicating duplicate type definitions in the generated code. The error occurs even after:

1. Cleaning obj/bin directories
2. Excluding analyzers from packages
3. Disabling analyzers in project settings
4. Using only official Microsoft.Orleans packages

## Error Pattern

```
error CS0101: The namespace 'OrleansCodeGen.ShooterShared' already contains a definition for 'Metadata_ShooterShared'
error CS0101: The namespace 'OrleansCodeGen.Shooter.Shared.GrainInterfaces' already contains a definition for 'Invokable_IPlayerGrain_GrainReference_2A3F2A9A'
```

## Root Cause Analysis

The Orleans source generator (OrleansSerializationSourceGenerator) appears to be generating duplicate code. This could be due to:

1. Source generator running multiple times
2. Conflict between build configurations in the Granville fork
3. Interaction with Granville.Rpc.Sdk code generators
4. Build system configuration inheriting from parent directories

## Attempted Solutions

1. **Clean Build**: Removed obj/bin directories - issue persists
2. **Exclude Analyzers**: Added ExcludeAssets="analyzers" - issue persists  
3. **Disable Analyzers**: Set EnableNETAnalyzers=false, RunAnalyzersDuringBuild=false - issue persists
4. **Remove Granville.Rpc References**: Tested with only Orleans packages - issue persists

## Recommended Solution

Given the code generation issues, we recommend proceeding with **Option 2: Assembly Redirects** approach:

1. Use pre-built Granville.Orleans assemblies
2. Implement assembly redirect resolvers in each host project
3. This avoids the code generation issues entirely
4. Provides compatibility with UFX.Orleans.SignalRBackplane

## Implementation Plan

1. Copy pre-built Granville.Orleans assemblies to Shooter projects
2. Add assembly resolver to redirect Microsoft.Orleans.* to Granville.Orleans.*
3. Test with UFX.Orleans.SignalRBackplane integration
4. Document the approach for future reference

## Future Investigation

The code generation issue should be investigated further:
- Test with different Orleans versions
- Isolate the source generator behavior
- Check for MSBuild target conflicts
- Review Directory.Build.targets inheritance chain

## Update: Granville Package Structure

Investigation revealed that Granville.Orleans packages contain assemblies named `Orleans.*.dll` (not `Granville.Orleans.*.dll`). This is intentional for compatibility:
- Package IDs: `Granville.Orleans.*`
- Assembly names: `Orleans.*`
- Namespaces: `Orleans`

This means Granville packages are drop-in replacements for Microsoft.Orleans packages at the binary level.

## Final Resolution

After extensive testing, we found that:

1. **Microsoft.Orleans 9.1.2 packages have code generation issues** - The Orleans source generator creates duplicate type definitions in the Shooter sample
2. **Type-forwarding shims don't work with source generators** - The Orleans code generator requires actual type definitions, not type forwards
3. **Granville.Orleans packages also have generator issues** - The "Cannot find type with metadata name Orleans.ApplicationPartAttribute" error persists

## Recommendation

For the Shooter sample specifically:
1. Continue using assembly redirects (Option 2) as implemented
2. Pre-compile the Shooter.Shared project with a working Orleans version or disable code generation
3. Consider using a simpler grain interface structure that doesn't trigger the duplicate generation bug
4. File an issue with Orleans about the code generation duplicate problem in 9.1.2

For other projects:
- The hybrid package strategy (using official packages for unmodified assemblies) remains valid
- Assembly redirects work well for runtime compatibility with UFX and other third-party packages
- Type-forwarding shims are not suitable when source generators are involved