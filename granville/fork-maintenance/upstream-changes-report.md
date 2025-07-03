# Upstream Changes Assessment Report

Generated on: 2025-07-01 22:53:07
Compared against upstream merge base: 6905fa9b309446682aabd7265ce98d8825e0390d

## Summary

**Total changes (excluding granville/ and src/Rpc/):**
- **Added files**: 6
- **Modified files**: 5
- **Deleted files**: 0

### By Location:
- **src/ folder** (excluding src/Rpc/): Added: 5, Modified: 0, Deleted: 0
- **Root directory**: Added: 1, Modified: 5, Deleted: 0
- **Other directories**: Added: 0, Modified: 0, Deleted: 0
## Changes in src/ folder (excluding src/Rpc/)
### Added Files in src/

These files are new in the Granville fork and do not exist in upstream Orleans:
- `src/Orleans.Core.Abstractions/Properties/AssemblyInfo.Granville.cs` - Granville-specific InternalsVisibleTo declarations
- `src/Orleans.Core/Properties/AssemblyInfo.Granville.cs` - Granville-specific InternalsVisibleTo declarations
- `src/Orleans.Runtime/Properties/AssemblyInfo.Granville.cs` - Granville-specific InternalsVisibleTo declarations
- `src/Orleans.Serialization.Abstractions/Properties/AssemblyInfo.Granville.cs` - Granville-specific InternalsVisibleTo declarations
- `src/Orleans.Serialization/Properties/AssemblyInfo.Granville.cs` - Granville-specific InternalsVisibleTo declarations
No files modified in src/ folder.


## Changes in Root Directory
### Added Files in Root
- `CLAUDE.md` - Guidance for Claude Code AI assistant

### Modified Files in Root
- `.gitignore`
- `Directory.Build.props`
  -  insertions,  deletions
- `Directory.Build.targets`
  -  insertions,  deletions
- `Directory.Packages.props`
- `NuGet.Config`
  -  insertions,  deletions

## Analysis by File Type

### Assembly Info Files- `src/Orleans.Core.Abstractions/Properties/AssemblyInfo.Granville.cs` (Added) - Granville-specific assembly attributes
- `src/Orleans.Core/Properties/AssemblyInfo.Granville.cs` (Added) - Granville-specific assembly attributes
- `src/Orleans.Runtime/Properties/AssemblyInfo.Granville.cs` (Added) - Granville-specific assembly attributes
- `src/Orleans.Serialization.Abstractions/Properties/AssemblyInfo.Granville.cs` (Added) - Granville-specific assembly attributes
- `src/Orleans.Serialization/Properties/AssemblyInfo.Granville.cs` (Added) - Granville-specific assembly attributes

### Build Configuration Files- `Directory.Build.props` (Modified)
- `Directory.Build.targets` (Modified)
- `Directory.Packages.props` (Modified)

## Excluded from Analysis

The following directories were excluded from this analysis as they are Granville-specific:
- granville/ - All Granville-specific tools, scripts, and documentation
- src/Rpc/ - Granville RPC implementation (hoped for upstream contribution)
- 	est/Rpc/ - Tests for Granville RPC implementation

To see the contents of these directories, explore them directly in the repository.
