# Hybrid Package Approach for Shooter Sample

## Current Issue

The Shooter sample is attempting to use a hybrid approach:
- **Granville RPC packages** for UDP game communication
- **Microsoft Orleans packages** for compatibility with third-party extensions (UFX.Orleans.SignalRBackplane)

However, this creates conflicts because:
1. Granville.Rpc.Sdk has transitive dependencies on Granville.Orleans packages
2. Both Microsoft.Orleans.CodeGenerator and Granville.Orleans.CodeGenerator attempt to generate code
3. Type conflicts occur between Granville.Orleans and Microsoft.Orleans assemblies

## Solutions

### Option 1: Use Orleans_DesignTimeBuild (Partial Solution)

Add to your .csproj files:
```xml
<PropertyGroup>
  <Orleans_DesignTimeBuild>true</Orleans_DesignTimeBuild>
</PropertyGroup>
```

This disables the official Orleans source generator to prevent duplicate code generation but doesn't resolve type conflicts.

### Option 2: Full Granville Stack (Recommended)

Use Granville Orleans packages throughout and implement assembly redirects for third-party compatibility:
- Use Granville.Orleans.* packages everywhere
- Implement AssemblyRedirectHelper for third-party packages
- See `/granville/compatibility-tools/ASSEMBLY-REDIRECT-GUIDE.md`

### Option 3: Separate Projects

Create separate projects that don't share types:
- Orleans-only projects using Microsoft.Orleans packages
- RPC-only projects using Granville.Rpc packages
- Shared interfaces in a neutral project

## Current Status

The Shooter sample is experiencing build failures due to these conflicts. A refactoring is needed to implement one of the above solutions consistently.

## Future Work

1. Create Granville.Rpc packages that don't depend on Granville.Orleans
2. Implement proper package isolation to support hybrid scenarios
3. Provide clear examples of each approach