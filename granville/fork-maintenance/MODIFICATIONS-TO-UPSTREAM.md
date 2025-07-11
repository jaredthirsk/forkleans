# Modifications to Upstream Orleans

This document tracks all modifications made to upstream Orleans files in the Granville fork.

**Note**: For repository organization guidelines and the location of Granville-specific files, see `/granville/REPO-ORGANIZATION.md`.

**Last assessed**: 2025-07-01 (compared against upstream merge base: 6905fa9b309446682aabd7265ce98d8825e0390d)

## Assessment Summary

Based on automated analysis using `/granville/compatibility-tools/Assess-UpstreamChanges.ps1`:

**Total changes (excluding granville/, src/Rpc/, and test/Rpc/):**
- **Added files**: 6 (5 in src/, 1 in root)
- **Modified files**: 5 (all in root directory)
- **Deleted files**: 0

**By Location:**
- **src/ folder** (excluding src/Rpc/): Added: 5, Modified: 0, Deleted: 0
- **Root directory**: Added: 1, Modified: 5, Deleted: 0
- **Other directories**: Added: 0, Modified: 0, Deleted: 0

This demonstrates our minimal impact approach - we have made NO modifications to existing Orleans source files in the src/ folder.

## New Files Added (No upstream conflict)

These files are completely new and do not exist in upstream Orleans:

### Assembly Info Files for Granville
- `src/Orleans.Core.Abstractions/Properties/AssemblyInfo.Granville.cs` - InternalsVisibleTo for Granville assemblies
- `src/Orleans.Core/Properties/AssemblyInfo.Granville.cs` - InternalsVisibleTo for Granville.Rpc assemblies
- `src/Orleans.Runtime/Properties/AssemblyInfo.Granville.cs` - InternalsVisibleTo for Granville.Rpc assemblies
- `src/Orleans.Serialization.Abstractions/Properties/AssemblyInfo.Granville.cs` - InternalsVisibleTo for Granville.Rpc assemblies
- `src/Orleans.Serialization/Properties/AssemblyInfo.Granville.cs` - InternalsVisibleTo for Granville.Rpc assemblies

### Root Directory Files (Added)
- `CLAUDE.md` - Guidance for Claude Code when working with this fork

### Granville-Specific Projects Added
- `src/Granville.Orleans.Shims/` - Helper package for Orleans shim compatibility

### Build Files Added to Source Projects
- `src/Orleans.Core/build/Granville.Orleans.Core.props` - Auto-disables official Orleans code generator
- `src/Orleans.Sdk/build/Granville.Orleans.Sdk.props` - Auto-disables official Orleans code generator  
- `src/Orleans.CodeGenerator/build/Granville.Orleans.CodeGenerator.props` - Auto-disables official Orleans code generator

### Root Directory Files (Modified)
- `.gitignore` - Added entries for Granville-specific build artifacts
- `Directory.Build.props` - Minor changes for Granville configuration
- `Directory.Build.targets` - MSBuild customizations to rename assemblies from Microsoft.Orleans.* to Granville.Orleans.*
- `Directory.Packages.props` - Package version management adjustments
- `NuGet.Config` - Configuration updates for package sources

## Source Code Modifications

### Orleans.Serialization
- `src/Orleans.Serialization/Hosting/SerializerBuilderExtensions.cs` - Modified AddAssembly to follow TypeForwardedTo attributes for metadata discovery
- `src/Orleans.Serialization/Hosting/ReferencedAssemblyProvider.cs` - Modified AddAssembly to include assemblies referenced via TypeForwardedTo

These modifications enable Orleans to discover metadata in Granville assemblies when using shim packages that forward types.

### Granville.Orleans.Shims (New Project)
- `src/Granville.Orleans.Shims/` - New project providing helper methods for shim compatibility
- Provides `AddOrleansShims()` extension method to work around serialization metadata discovery issues
- See `/granville/docs/SERIALIZATION-SHIM-ISSUE.md` for details

Note: All other Granville-specific files are located under `/granville/` directory as per our repository organization guidelines.

## Modified Upstream Files

### Source Files Modified
1. `src/Orleans.CodeGenerator/OrleansSourceGenerator.cs` - Changed to check `granville_designtimebuild` instead of `orleans_designtimebuild`
2. `src/Orleans.CodeGenerator/build/Microsoft.Orleans.CodeGenerator.props` - Changed to use `Granville_DesignTimeBuild` property
3. `src/Orleans.Core/Orleans.Core.csproj` - Added packaging of Granville.Orleans.Core.props
4. `src/Orleans.Sdk/Orleans.Sdk.csproj` - Added packaging of Granville.Orleans.Sdk.props
5. `src/Orleans.CodeGenerator/Orleans.CodeGenerator.csproj` - Added packaging of Granville.Orleans.CodeGenerator.props
6. `src/Orleans.Runtime/Properties/AssemblyInfo.cs` - Added InternalsVisibleTo for Granville.Orleans.Streaming and Granville.Orleans.TestingHost
7. `src/Orleans.Transactions/Properties/AssemblyInfo.cs` - Added InternalsVisibleTo for Granville.Orleans.Transactions.TestKit.Base

### Root-Level Configuration Files Modified
1. `.gitignore` - Added entries for Granville-specific patterns
2. `Directory.Build.props` - Minor adjustments for build configuration
3. `Directory.Build.targets` - Assembly renaming logic from Microsoft.Orleans.* to Granville.Orleans.*
4. `Directory.Packages.props` - Package version management
5. `NuGet.Config` - Package source configuration

Note: Files under `/granville/` and `/src/Rpc/` are Granville-specific additions, not modifications of existing Orleans files.

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

## Assessing Upstream Changes

To run the automated assessment of upstream changes:
```powershell
./granville/compatibility-tools/Assess-UpstreamChanges.ps1
```

This script will:
1. Compare against the upstream Orleans repository
2. Identify all added, modified, and deleted files in src/ (excluding src/Rpc)
3. Generate a detailed markdown report
4. Help track our minimal impact on the Orleans codebase

## Building Granville Orleans

To build Granville Orleans assemblies:
```bash
./granville/scripts/build-granville.sh
```

Or on Windows:
```powershell
./granville/scripts/build-granville-orleans.ps1
```

This will build all Orleans assemblies with the Granville.Orleans.* prefix while maintaining compatibility with code expecting Microsoft.Orleans.* assemblies.