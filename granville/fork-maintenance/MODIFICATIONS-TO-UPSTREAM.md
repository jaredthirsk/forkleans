# Modifications to Upstream Orleans

This document tracks all modifications made to upstream Orleans files in the Granville fork.

**Note**: For repository organization guidelines and the location of Granville-specific files, see `/granville/REPO-ORGANIZATION.md`.

## New Files Added (No upstream conflict)

These files are completely new and do not exist in upstream Orleans:

### Assembly Info Files for Granville
- `src/Orleans.Core.Abstractions/Properties/AssemblyInfo.Granville.cs` - InternalsVisibleTo for Granville assemblies
- `src/Orleans.Runtime/Properties/AssemblyInfo.Granville.cs` - InternalsVisibleTo for Granville.Rpc assemblies
- `src/Orleans.Core/Properties/AssemblyInfo.Granville.cs` - InternalsVisibleTo for Granville.Rpc assemblies

### Build Infrastructure
- `Directory.Build.targets` - MSBuild customizations to rename assemblies from Microsoft.Orleans.* to Granville.Orleans.*
- `build-granville.sh` - Bash script to build Granville Orleans assemblies in dependency order
- `build-granville.ps1` - PowerShell script to build Granville Orleans assemblies in dependency order
- `Fix-ProjectReferences.ps1` - Script to fix project references after namespace conversion

### Compatibility Tools
- `compatibility-tools/` - Directory containing tools for Orleans compatibility
  - `GenerateTypeForwardingShims.csx` - Generates Microsoft.Orleans.* shim assemblies that forward to Granville.Orleans.*
  - `GenerateTypeForwardingShims.ps1` - PowerShell version of the shim generator
  - `assembly-redirects-template.config` - XML template for .NET Framework assembly binding redirects
  - `AssemblyRedirectDemo.cs` - Example code for implementing assembly redirects in .NET Core/5+
  - `README.md` - Documentation for using the compatibility tools

### Documentation
- `COEXISTENCE-STRATEGIES.md` - Detailed documentation of strategies for Granville/Microsoft Orleans coexistence
- `fork-maintenance/MODIFICATIONS-TO-UPSTREAM.md` - This file

### Sample Projects
- `samples/Rpc/test-option2/` - Test project demonstrating assembly redirect approach
- `samples/Rpc/test-ufx-integration/` - Integration test proving UFX.Orleans.SignalRBackplane works with Granville Orleans via assembly redirects

## Modified Upstream Files

These files had to be modified from their upstream versions:

### Assembly Info Files (Restored to Original)
The following files were temporarily modified but have now been restored to their original state:
- `src/Orleans.Core.Abstractions/Properties/AssemblyInfo.cs` - Removed Granville InternalsVisibleTo entries (moved to AssemblyInfo.Granville.cs)
- `src/Orleans.Runtime/Properties/AssemblyInfo.cs` - Removed Granville InternalsVisibleTo entries (moved to AssemblyInfo.Granville.cs)
- `src/Orleans.Core/Properties/AssemblyInfo.cs` - Removed Granville InternalsVisibleTo entries (moved to AssemblyInfo.Granville.cs)

### RPC Sample Files
- `src/Rpc/Orleans.Rpc.Abstractions/Properties/AssemblyInfo.cs` - Contains ApplicationPartAttribute for Granville.Rpc.Abstractions
- `samples/Rpc/Shooter.Silo/Program.cs` - Added assembly redirect handler for .NET Core/5+ to redirect Microsoft.Orleans.* to Granville.Orleans.*
- `samples/Rpc/Shooter.Silo/Shooter.Silo.csproj` - Added import for CopyGranvilleAssemblies.targets and UFX.Orleans.SignalRBackplane package reference
- `samples/Rpc/Shooter.Silo/CopyGranvilleAssemblies.targets` - MSBuild targets to copy Granville assemblies for runtime redirection
- `samples/Rpc/Shooter.Silo/Directory.Build.props` - Defines GranvilleOrleansPath property

### Git Configuration
- `.gitignore` - Added entries for build artifacts and compatibility tools

## Minimal Impact Strategy

The fork maintains minimal changes to upstream files by:
1. Using separate `AssemblyInfo.Granville.cs` files for Granville-specific InternalsVisibleTo attributes
2. Using `Directory.Build.targets` for build-time assembly renaming without modifying individual project files
3. Keeping all Granville-specific tools and scripts in separate directories
4. Using assembly redirects at runtime rather than modifying source code

## Syncing with Upstream

When syncing with upstream Orleans:
1. The modified files listed above may have merge conflicts
2. New files added by this fork will not conflict
3. The assembly info files have been restored to their original state, reducing conflicts
4. The main point of conflict will be `Directory.Build.targets` if upstream adds one

## Building Granville Orleans

To build Granville Orleans assemblies:
```bash
./build-granville.sh
```

Or on Windows:
```powershell
./build-granville.ps1
```

This will build all Orleans assemblies with the Granville.Orleans.* prefix while maintaining compatibility with code expecting Microsoft.Orleans.* assemblies.