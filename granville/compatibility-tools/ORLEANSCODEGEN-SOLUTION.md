# OrleansCodeGen Type Forwarding Solution

## Problem Summary

When running the Shooter sample with Granville Orleans, we get:
```
System.TypeLoadException: Could not load type 'OrleansCodeGen.Orleans.Runtime.Codec_GrainId' 
from assembly 'Orleans.Core.Abstractions, Version=9.0.0.0, Culture=neutral, PublicKeyToken=null'.
```

## Root Cause Analysis

1. **Orleans Code Generation**: Orleans uses source generators to create serialization types (codecs, copiers, etc.) during build time. These types are embedded in the Orleans assemblies with names like `OrleansCodeGen.Orleans.Runtime.Codec_GrainId`.

2. **Microsoft Orleans Assemblies**: When Microsoft builds Orleans, these OrleansCodeGen types ARE generated and embedded in their assemblies (verified in `Artifacts/DistributedTests/DistributedTests.Server/net8.0/Orleans.Core.Abstractions.dll`).

3. **Granville Orleans Assemblies**: Our Granville build has `Orleans_DesignTimeBuild=true` set in the props files (`src/Orleans.Core/build/Granville.Orleans.Core.props`), which disables code generation. This prevents OrleansCodeGen types from being generated.

4. **The Shim Problem**: Our shims try to forward OrleansCodeGen types, but these types don't exist in Granville assemblies to forward to.

## Why This Happens

The Granville fork intentionally disables Orleans code generation by default to prevent conflicts when both Microsoft.Orleans and Granville.Orleans packages are used together. This is done via props files that set `Orleans_DesignTimeBuild=true`.

## Solution Options

### Option 1: Use Microsoft Orleans DLLs for OrleansCodeGen Types (Not Viable)
We cannot forward from our shims to Microsoft Orleans DLLs because that would create circular dependencies and defeat the purpose of the shims.

### Option 2: Enable Code Generation in Granville Assemblies (Recommended)
Modify the Granville build to generate OrleansCodeGen types:

1. Remove or conditionally set `Orleans_DesignTimeBuild` in props files
2. Ensure the Orleans source generator runs during Granville builds
3. The generated types will be in Granville assemblies, and shims can forward to them

### Option 3: Remove OrleansCodeGen Forwards from Shims
Simply don't forward these types, and let them be generated in consuming assemblies. However, this won't work because the Orleans runtime expects these types to exist in specific assemblies.

### Option 4: Create Separate OrleansCodeGen Assemblies
Build separate assemblies that contain just the generated types. This is complex and not recommended.

## Recommended Implementation

We should pursue **Option 2** by modifying the Granville build process:

1. Update `/src/Orleans.Core/build/Granville.Orleans.Core.props` and similar files to NOT set `Orleans_DesignTimeBuild=true` by default
2. Or, add a build property that allows overriding this during the Granville assembly build
3. Rebuild Granville assemblies with code generation enabled
4. The OrleansCodeGen types will then exist in Granville assemblies for the shims to forward to

## Next Steps

1. Modify the props files in the Orleans source
2. Rebuild Granville Orleans assemblies
3. Verify OrleansCodeGen types are generated
4. Test with the Shooter sample