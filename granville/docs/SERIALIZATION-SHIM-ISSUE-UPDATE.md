# ApplicationPart Fix Implementation (Dec 2024)

## Problem Discovered
When building with `BuildAsGranville=true`, the Orleans code generator was using the original assembly names (e.g., "Orleans.Core") instead of the renamed assembly names (e.g., "Granville.Orleans.Core") for ApplicationPart attributes. This caused the serialization system to not discover the assemblies at runtime.

## Solution Implemented
1. **Added Granville_FinalAssemblyName CompilerVisibleProperty**:
   - Added to Microsoft.Orleans.CodeGenerator.props and Granville.Orleans.CodeGenerator.props
   - Set in Directory.Build.targets when BuildAsGranville=true

2. **Modified Orleans Code Generator**:
   - Updated CodeGenerator constructor to accept optional finalAssemblyName parameter
   - Modified GenerateCode to use finalAssemblyName if provided
   - Updated OrleansSourceGenerator to read and pass Granville_FinalAssemblyName

3. **Enabled Code Generation for Orleans.Serialization**:
   - Removed IsOrleansFrameworkPart=false to enable ApplicationPart generation

## Results
After implementation:
- Granville.Orleans.Core has [ApplicationPart]: True ✓
- Granville.Orleans.Core.Abstractions has [ApplicationPart]: True ✓
- Granville.Orleans.Runtime has [ApplicationPart]: True ✓
- Granville.Orleans.Reminders has [ApplicationPart]: True ✓
- Granville.Orleans.Serialization has [ApplicationPart]: True (verified via external tool)

## Current Status
Despite ApplicationPart attributes being correctly generated, the Shooter.Silo still fails with:
```
Orleans.Serialization.CodecNotFoundException: Could not find a copier for type Orleans.Serialization.Invocation.Response`1[Orleans.MembershipTableData]
```

This suggests that:
1. ApplicationPart generation is now correct
2. The AddGranvilleAssemblies detection logic may have issues (shows False for Serialization)
3. There may be additional timing or discovery issues beyond ApplicationPart attributes

## Next Steps
1. Investigate why AddGranvilleAssemblies shows ApplicationPart as False for Granville.Orleans.Serialization despite external verification showing it's present
2. Consider if there are additional metadata requirements beyond ApplicationPart
3. Explore if TypeManifestProvider generation is working correctly for all assemblies