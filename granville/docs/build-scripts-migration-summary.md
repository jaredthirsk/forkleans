# Build Scripts Migration Summary

## Overview
We've successfully migrated from the old packaging approach to a new streamlined approach that aligns with our strategy of only packaging Granville.Rpc packages while using official Microsoft.Orleans packages from nuget.org.

## Old Scripts (Archived)
- **granville-version-bump.ps1** - Complex script that packaged both Orleans and RPC packages
- **Create-GranvillePackages.ps1** - Helper script for creating all packages

These scripts have been archived to `archived-scripts/` directory.

## New Scripts
- **bump-granville-version.ps1** - Simple version bumping script
  - Only updates version in Directory.Build.props
  - Supports revision bumping or full version setting
  - Much simpler and focused

- **build-granville-rpc.ps1** - Streamlined build and package script
  - Builds Orleans.sln first (for dependencies)
  - Then builds each RPC project individually
  - Only packages the 6 Granville.Rpc.* projects
  - Clear output directory structure

## Key Improvements
1. **Simplified Process**: No longer trying to package Orleans assemblies
2. **Faster Execution**: Only packages what we need (6 RPC packages vs 23+ packages)
3. **Clearer Strategy**: Aligns with using official Microsoft.Orleans.* packages
4. **Better Maintainability**: Less complex scripts, easier to understand and modify

## Usage Examples

### Bump version and build packages:
```bash
./bump-granville-version.ps1
./build-granville-rpc.ps1
```

### Just build packages (no version bump):
```bash
./build-granville-rpc.ps1
```

### Bump to specific version:
```bash
./bump-granville-version.ps1 -VersionPart Full -NewVersion 9.2.0.1
```

### Build with custom configuration:
```bash
./build-granville-rpc.ps1 -Configuration Debug
```

## Version Strategy
- Version format: `Major.Minor.Patch.Revision`
- First three parts (9.1.2) match the Orleans version we're based on
- Last part (revision) is our fork-specific version number
- Current version: 9.1.2.51

## Package Output
Packages are created in: `./Artifacts/Release/`
- Granville.Rpc.Abstractions
- Granville.Rpc.Client
- Granville.Rpc.Server
- Granville.Rpc.Sdk
- Granville.Rpc.Transport.LiteNetLib
- Granville.Rpc.Transport.Ruffles

## Migration Helper
A migration script `migrate-to-new-scripts.ps1` was created to help transition between old and new scripts. It has already been run to archive the old scripts.

## Fixed Issues
- Line ending problems (CRLF vs LF) have been fixed on all PowerShell scripts
- Scripts now work correctly on Linux/WSL environments