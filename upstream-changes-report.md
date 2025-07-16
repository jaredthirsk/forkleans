# Upstream Changes Assessment Report

Generated on: 2025-07-16 04:59:44
Compared against upstream merge base: 6905fa9b309446682aabd7265ce98d8825e0390d

## Summary

**Total changes (excluding granville/ and src/Rpc/):**
- **Added files**: 24
- **Modified files**: 19
- **Deleted files**: 0

### By Location:
- **src/ folder** (excluding src/Rpc/): Added: 9, Modified: 14, Deleted: 0
- **Root directory**: Added: 11, Modified: 5, Deleted: 0
- **Other directories**: Added: 4, Modified: 0, Deleted: 0
## Changes in src/ folder (excluding src/Rpc/)
### Added Files in src/

These files are new in the Granville fork and do not exist in upstream Orleans:
- `src/Orleans.CodeGenerator/build/Granville.Orleans.CodeGenerator.props`
- `src/Orleans.Core.Abstractions/Properties/AssemblyInfo.Granville.cs` - Granville-specific InternalsVisibleTo declarations
- `src/Orleans.Core/build/Granville.Orleans.Core.props`
- `src/Orleans.Core/Properties/AssemblyInfo.Granville.cs` - Granville-specific InternalsVisibleTo declarations
- `src/Orleans.Runtime/Properties/AssemblyInfo.Granville.cs` - Granville-specific InternalsVisibleTo declarations
- `src/Orleans.Sdk/build/Granville.Orleans.Sdk.props`
- `src/Orleans.Sdk/build/Granville.Orleans.Sdk.targets`
- `src/Orleans.Serialization.Abstractions/Properties/AssemblyInfo.Granville.cs` - Granville-specific InternalsVisibleTo declarations
- `src/Orleans.Serialization/Properties/AssemblyInfo.Granville.cs` - Granville-specific InternalsVisibleTo declarations

### Modified Files in src/

These files have been modified from their upstream versions:
- `src/Orleans.Analyzers/Orleans.Analyzers.csproj`
  -  insertions,  deletions
- `src/Orleans.CodeGenerator/build/Microsoft.Orleans.CodeGenerator.props`
  -  insertions,  deletions
- `src/Orleans.CodeGenerator/CodeGenerator.cs`
  -  insertions,  deletions
- `src/Orleans.CodeGenerator/Orleans.CodeGenerator.csproj`
  -  insertions,  deletions
- `src/Orleans.CodeGenerator/OrleansSourceGenerator.cs`
  -  insertions,  deletions
- `src/Orleans.Core.Abstractions/Properties/AssemblyInfo.cs`
- `src/Orleans.Core/Properties/AssemblyInfo.cs`
- `src/Orleans.Runtime/Properties/AssemblyInfo.cs`
- `src/Orleans.Sdk/Orleans.Sdk.csproj`
  -  insertions,  deletions
- `src/Orleans.Serialization/Hosting/ReferencedAssemblyProvider.cs`
- `src/Orleans.Serialization/Hosting/SerializerBuilderExtensions.cs`
- `src/Orleans.Serialization/Orleans.Serialization.csproj`
  -  insertions,  deletions
- `src/Orleans.Server/Orleans.Server.csproj`
  -  insertions,  deletions
- `src/Orleans.Transactions/Properties/AssemblyInfo.cs`

## Changes in Root Directory
### Added Files in Root
- `check-codegen.ps1`
- `CLAUDE.md` - Guidance for Claude Code AI assistant
- `create-sdk-shim.sh`
- `Directory.Build.Granville.props`
- `Directory.Build.targets.compatibility` - MSBuild configuration for assembly renaming
- `Directory.Build.targets.original` - MSBuild configuration for assembly renaming
- `Directory.Build.targets.pack` - MSBuild configuration for assembly renaming
- `Granville.Minimal.sln`
- `Granville.sln`
- `test-codegen.csproj`
- `TestGrain.cs`

### Modified Files in Root
- `.gitignore`
- `Directory.Build.props`
  - targets insertions,  deletions
- `Directory.Build.targets`
  - targets insertions,  deletions
- `Directory.Packages.props`
- `NuGet.Config`
  - targets insertions,  deletions

## Changes in Other Directories (test/, samples/, etc.)
### Added Files
- `.roo/mcp.json`
- `"test/Benchmarks/.\\BenchmarkDotNet.Aritfacts.2025-07-15_11-06-37Z/results/Benchmarks.ComplexTypeBenchmarks-report-github.md"`
- `"test/Benchmarks/.\\BenchmarkDotNet.Aritfacts.2025-07-15_11-06-37Z/results/Benchmarks.ComplexTypeBenchmarks-report.csv"`
- `"test/Benchmarks/.\\BenchmarkDotNet.Aritfacts.2025-07-15_11-06-37Z/results/Benchmarks.ComplexTypeBenchmarks-report.html"`

## Analysis by File Type

### Assembly Info Files- `src/Orleans.Core.Abstractions/Properties/AssemblyInfo.cs` (Modified)
- `src/Orleans.Core.Abstractions/Properties/AssemblyInfo.Granville.cs` (Added) - Granville-specific assembly attributes
- `src/Orleans.Core/Properties/AssemblyInfo.cs` (Modified)
- `src/Orleans.Core/Properties/AssemblyInfo.Granville.cs` (Added) - Granville-specific assembly attributes
- `src/Orleans.Runtime/Properties/AssemblyInfo.cs` (Modified)
- `src/Orleans.Runtime/Properties/AssemblyInfo.Granville.cs` (Added) - Granville-specific assembly attributes
- `src/Orleans.Serialization.Abstractions/Properties/AssemblyInfo.Granville.cs` (Added) - Granville-specific assembly attributes
- `src/Orleans.Serialization/Properties/AssemblyInfo.Granville.cs` (Added) - Granville-specific assembly attributes
- `src/Orleans.Transactions/Properties/AssemblyInfo.cs` (Modified)

### Build Configuration Files- `Directory.Build.Granville.props` (Added)
- `Directory.Build.props` (Modified)
- `Directory.Build.targets` (Modified)
- `Directory.Build.targets.compatibility` (Added)
- `Directory.Build.targets.original` (Added)
- `Directory.Build.targets.pack` (Added)
- `Directory.Packages.props` (Modified)
- `src/Orleans.Analyzers/Orleans.Analyzers.csproj` (Modified)
- `src/Orleans.CodeGenerator/build/Granville.Orleans.CodeGenerator.props` (Added)
- `src/Orleans.CodeGenerator/build/Microsoft.Orleans.CodeGenerator.props` (Modified)
- `src/Orleans.CodeGenerator/Orleans.CodeGenerator.csproj` (Modified)
- `src/Orleans.Core/build/Granville.Orleans.Core.props` (Added)
- `src/Orleans.Sdk/build/Granville.Orleans.Sdk.props` (Added)
- `src/Orleans.Sdk/build/Granville.Orleans.Sdk.targets` (Added)
- `src/Orleans.Sdk/Orleans.Sdk.csproj` (Modified)
- `src/Orleans.Serialization/Orleans.Serialization.csproj` (Modified)
- `src/Orleans.Server/Orleans.Server.csproj` (Modified)
- `test-codegen.csproj` (Added)

## Excluded from Analysis

The following directories were excluded from this analysis as they are Granville-specific:
- granville/ - All Granville-specific tools, scripts, and documentation
- src/Rpc/ - Granville RPC implementation (hoped for upstream contribution)
- 	est/Rpc/ - Tests for Granville RPC implementation

To see the contents of these directories, explore them directly in the repository.
