# Repository Cleanup Summary

This document summarizes the repository reorganization performed to keep the Orleans fork root as pristine as possible.

**Last Updated**: 2025-07-01

## Files Moved to `/granville/`

### Scripts (`/granville/scripts/`)
- **Build Scripts**:
  - `build-granville-orleans.ps1`, `build-granville-orleans.sh` - Main Granville Orleans build scripts
  - `bump-granville-version.ps1` - Version management
  - `build-granville-rpc*.ps1` - RPC build scripts
  - `build-orleans-packages.ps1` - Package building
  - `clean-build-artifacts.ps1` - Cleanup utility
  - `setup-local-feed.ps1` - Local NuGet feed setup

- **Fork Maintenance Scripts**:
  - `Convert-OrleansNamespace-Fixed.ps1` - Namespace conversion
  - `Fix-*.ps1` (multiple) - Various fix scripts for fork maintenance
  - `Revert-GranvilleToOrleans.ps1` - Reversion utility
  - `Smart-Fix-References.ps1` - Reference fixing

- **Utility Scripts**:
  - `Add-GlobalUsings.ps1` - Global using management
  - `Analyze-PackageDependencies.ps1` - Dependency analysis
  - `analyze_public_api*.sh`, `count_public_types.sh` - API analysis
  - `analyze_api_surface.csx` - API surface analysis
  - `common.ps1` - Common utilities
  - Various other utility scripts

- **Historical** (`/granville/scripts/historical/archived-scripts/`):
  - `Create-GranvillePackages.ps1` - Old package creation script
  - `granville-version-bump.ps1` - Old version bump script

### Documentation (`/granville/docs/`)
- `MIGRATION-TO-9.1.2-SUMMARY.md` - Migration documentation
- `build-scripts-migration-summary.md` - Build script migration details
- `REPOSITORY-CLEANUP-SUMMARY.md` - This file

### Test Projects (`/granville/test/`)
- `test-option2/` - Assembly redirect approach testing
- `test-ufx-integration/` - UFX SignalR integration testing

### Configuration
- `NuGet.config.local` - Local NuGet configuration

## Files Remaining in Root (Upstream Orleans)
- Standard Orleans files: `README.md`, `LICENSE.md`, `Orleans.sln`, etc.
- Build files: `build.ps1`, `Build.cmd`, `Test.cmd`, `TestAll.cmd`
- Configuration: `.editorconfig`, `.gitignore`, `Directory.Build.*`
- CI/CD: `.github/`, `azure-pipelines.yml`

## New Files Added to Root
- `CLAUDE.md` - AI assistance guide for the Granville fork
- `/granville/REPO-ORGANIZATION.md` - Repository organization guide

### Additional Directories Moved (Phase 2)
- `/compatibility-tools/` → `/granville/compatibility-tools/`
- `/fork-maintenance/` → `/granville/fork-maintenance/`
- `/samples/` → `/granville/samples/`

**Note**: `/src/Rpc/` remains in the root `/src/` directory as we hope for upstream adoption of the RPC functionality.

## Result
The repository root now contains primarily upstream Orleans files, with all Granville-specific additions organized under `/granville/`, making it easier to:
1. Merge updates from upstream Orleans
2. Identify Granville-specific customizations
3. Maintain a clean separation between fork additions and upstream code

## Aspire AppHost Status
The Shooter.AppHost builds and starts successfully from the new location (`/granville/samples/Rpc/Shooter.AppHost`), though there may be service discovery issues to resolve. The individual services (Shooter.Silo, etc.) run correctly when started directly.