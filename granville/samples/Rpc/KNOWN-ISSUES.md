# Known Issues - Shooter RPC Sample

## Orleans 9.1.2 Code Generation Duplicate Errors

### Issue
When building the Shooter sample with Orleans 9.1.2 packages (either Microsoft.Orleans or Granville.Orleans), the Orleans source generator creates duplicate type definitions causing CS0101 compilation errors.

### Symptoms
```
error CS0101: The namespace 'OrleansCodeGen.ShooterShared' already contains a definition for 'Metadata_ShooterShared'
```

### Root Cause
The Orleans source generator (OrleansSerializationSourceGenerator) appears to be running multiple times or creating duplicate definitions. This issue affects:
- Microsoft.Orleans 9.1.2 packages
- Granville.Orleans packages (which use the same generator)

### Workarounds

#### Option 1: Use Pre-built Assemblies
If you have previously built Shooter.Shared successfully:
1. Copy the pre-built `Shooter.Shared.dll` from another project's bin folder
2. Place it in `Shooter.Shared/bin/Release/net9.0/`
3. Build other projects normally

#### Option 2: Downgrade Orleans Version
Use an earlier version of Orleans that doesn't exhibit this issue (e.g., 8.x).

#### Option 3: Simplify Grain Interfaces
Reduce the complexity of grain interfaces in Shooter.Shared to avoid triggering the duplicate generation bug.

### Status
- This is a known issue with Orleans 9.1.2
- The Granville fork inherits this issue as it uses the same code generator
- Assembly redirects (for UFX compatibility) have been implemented and tested separately

### Related Files
- `/granville/docs/code-generation-issues.md` - Detailed investigation
- `/granville/samples/Rpc/Shooter.Shared/AssemblyRedirectHelper.cs` - UFX compatibility solution
- `/granville/samples/Rpc/build-shooter-workaround.ps1` - Build script with workaround