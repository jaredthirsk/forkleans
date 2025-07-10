# Granville Orleans Fork Repository Organization

This document describes the organization structure and guidelines for the Granville Orleans fork.

## Core Principle: Minimal Upstream Modifications

We strive to keep Orleans upstream C# and other source files as untouched as possible to facilitate easier merging from upstream. All modifications to upstream files are documented in `/granville/fork-maintenance/MODIFICATIONS-TO-UPSTREAM.md`.

## Directory Structure

### `/granville/` - All Granville-Specific Content
All Granville-specific additions that are not part of upstream Orleans are organized under the `/granville/` directory:

- **`/granville/scripts/`** - Build and maintenance scripts
  - `build-granville-orleans.ps1` - Builds Granville Orleans assemblies
  - `bump-granville-version.ps1` - Version management
  - `Fix-*.ps1` - Various fork maintenance scripts
  - `/historical/` - Archived/obsolete scripts kept for reference

- **`/granville/docs/`** - Granville-specific documentation
  - `MIGRATION-TO-9.1.2-SUMMARY.md` - Migration documentation
  - `build-scripts-migration-summary.md` - Build system documentation
  - `/historical/` - Outdated documentation kept for reference

- **`/granville/test/`** - Test projects for Granville features
  - `test-option2/` - Assembly redirect approach testing
  - `test-ufx-integration/` - UFX SignalR integration testing

- **`/granville/samples/`** - Sample applications
  - `/Rpc/` - RPC samples including the Shooter game
  - `/Streaming/` - Streaming samples (if any)
  - `/etc/` - Other sample categories

- **`/granville/compatibility-tools/`** - Compatibility Tools
  - Type forwarding shim generators
  - Assembly redirect templates and examples
  - Documentation for compatibility strategies

- **`/granville/fork-maintenance/`** - Fork Maintenance Documentation
  - `MODIFICATIONS-TO-UPSTREAM.md` - Comprehensive list of all modifications to upstream files

- **`/granville/NuGet.config.local`** - Local NuGet configuration for testing

### `/src/Rpc/` - Granville RPC Implementation
The complete Granville RPC implementation. This remains in `/src/` (not under `/granville/`) because we hope it will be adopted into upstream Orleans.

## File Organization Guidelines

### When Adding New Files
1. **Scripts**: Place in `/granville/scripts/`
2. **Documentation**: Place in `/granville/docs/`
3. **Test Projects**: Place in `/granville/test/`
4. **Obsolete Content**: Move to appropriate `/historical/` subdirectory

### When Modifying Upstream Files
1. Minimize changes as much as possible
2. Use separate `AssemblyInfo.Granville.cs` files for Granville-specific attributes
3. Document all changes in `/granville/fork-maintenance/MODIFICATIONS-TO-UPSTREAM.md`
4. Consider using MSBuild targets/props for build-time modifications instead of source changes

### Build Customizations
- `Directory.Build.targets` - Renames assemblies from Microsoft.Orleans.* to Granville.Orleans.*
- Individual projects can override with local Directory.Build.targets

### Build Output
- **NuGet Packages**: All built NuGet packages (.nupkg files) are output to `/Artifacts/Release/`
  - This includes both Granville.Orleans.* and Granville.Rpc.* packages
  - The `/Artifacts/Release/` directory serves as a local NuGet feed for testing
  - Configure NuGet.config to include this as a package source

## Key Files for Understanding the Fork

1. **`/granville/fork-maintenance/MODIFICATIONS-TO-UPSTREAM.md`** - Lists all upstream modifications
2. **`/granville/docs/BUILDING.md`** - Comprehensive build instructions
3. **`/granville/scripts/build-all.ps1/.sh`** - Meta scripts to build everything
4. **`/CLAUDE.md`** - Instructions for AI assistance with this codebase
5. **`/src/Rpc/docs/`** - RPC-specific documentation
6. **`/granville/compatibility-tools/README.md`** - Compatibility strategies documentation

## Syncing with Upstream

When syncing with upstream Orleans:
1. Review `/granville/fork-maintenance/MODIFICATIONS-TO-UPSTREAM.md`
2. Use scripts in `/granville/scripts/` to reapply Granville customizations
3. Test compatibility tools and assembly redirects
4. Update documentation as needed

## Versioning

Granville Orleans uses a versioning suffix to distinguish from upstream:
- Upstream: `9.1.2`
- Granville: `9.1.2-granville`

Use `/granville/scripts/bump-granville-version.ps1` to manage versions.