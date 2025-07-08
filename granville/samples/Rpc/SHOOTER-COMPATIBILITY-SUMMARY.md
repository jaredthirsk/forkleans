# Shooter Sample Compatibility Summary

## Current Situation

The Shooter sample is attempting to use both:
- **Granville RPC packages** for UDP-based game communication
- **Microsoft Orleans packages** for compatibility with UFX.Orleans.SignalRBackplane

## Issues Encountered

### 1. Type Forwarding Shims
- The shim packages (Microsoft.Orleans.* that forward to Granville.Orleans.*) work at runtime but not at compile time
- Orleans source generator can't find types through type forwarding
- Results in "Cannot find type with metadata name Orleans.ApplicationPartAttribute" errors

### 2. Package Conflicts
- Granville.Rpc.Sdk has transitive dependencies on Granville.Orleans packages
- This creates conflicts when trying to use Microsoft.Orleans packages directly
- Both code generators attempt to run, causing duplicate type definitions

### 3. Missing RPC Attributes
- RpcMethod, RpcDeliveryMode, and other RPC attributes are not found
- This suggests Granville.Rpc.Sdk is not being properly included

## Solutions Attempted

1. **Orleans_DesignTimeBuild property** - Works to prevent duplicate generation by disabling official Orleans source generator
2. **Type-forwarding shims** - Don't work with source generators
3. **Mixed package approach** - Creates too many conflicts

## Recommendations

### Short Term
Use Microsoft.Orleans packages exclusively for the Shooter sample until Granville RPC packages can be decoupled from Granville Orleans dependencies.

### Long Term
1. Create Granville.Rpc packages that don't depend on Orleans at all
2. Or create a compatibility layer that properly isolates the two stacks
3. Or fully commit to using Granville Orleans throughout with proper assembly redirects for third-party packages

## Current Status
The Shooter sample cannot build successfully with the current hybrid approach. A decision needs to be made on which direction to take.

## Update: Build Issues Resolved (2025-07-08)

The build issues have been resolved by:
1. Creating a PowerShell script (`fix-granville-dependencies.ps1`) that fixes NuGet package dependencies post-build
2. Ensuring all Granville.Orleans.* packages depend on Microsoft.Orleans.*-granville-shim versions
3. Clearing the NuGet cache to remove stale packages

The Shooter sample now builds and starts successfully with .NET Aspire.

## Known Runtime Issues

When running the Shooter sample, you may encounter:

1. **Serialization Generator Warning**: `CS8785: Generator 'OrleansSerializationSourceGenerator' failed to generate source`
   - The generator cannot find `Orleans.Serialization.Configuration.TypeManifestProviderBase`
   - This may cause serialization issues at runtime

2. **Assembly Redirect Warnings**: `SYSLIB0037` warnings about obsolete AssemblyName properties
   - These are from the AssemblyRedirectHelper and can be safely ignored

3. **Other Runtime Errors**: Specific runtime errors are being investigated

Despite these warnings, the application starts and runs with .NET Aspire orchestration.