# CLAUDE.md - Granville Orleans Fork

This file provides guidance to Claude Code when working with the Granville Orleans fork.

## Overview

This is a fork of Microsoft Orleans that:
1. Renames all assemblies from `Microsoft.Orleans.*` to `Granville.Orleans.*` to avoid NuGet namespace conflicts
2. Adds Granville RPC functionality for high-performance UDP-based communication
3. Maintains compatibility with third-party packages expecting Microsoft.Orleans assemblies

## Repository Organization

**IMPORTANT**: See `/granville/REPO-ORGANIZATION.md` for detailed repository structure and guidelines.

Key principles:
- Orleans upstream files are kept as untouched as possible
- All Granville-specific additions are under `/granville/`
- Modifications to upstream files are documented in `/granville/fork-maintenance/MODIFICATIONS-TO-UPSTREAM.md`

## Key Directories

- `/granville/` - All Granville-specific content:
  - `/granville/scripts/` - Build and maintenance scripts
  - `/granville/docs/` - Granville-specific documentation
  - `/granville/test/` - Test projects
  - `/granville/samples/` - Sample applications including the Shooter game
  - `/granville/compatibility-tools/` - Tools for Microsoft.Orleans compatibility
  - `/granville/fork-maintenance/` - Documentation of upstream modifications
- `/src/Rpc/` - Granville RPC implementation (kept in src/ as we hope for upstream adoption)

## Building Granville Orleans

To build Granville Orleans assemblies:
```bash
./granville/scripts/build-granville.sh
```

Or on Windows:
```powershell
./granville/scripts/build-granville.ps1
```

## Working with the Fork

1. **Adding new features**: Place in appropriate directories under `/granville/` or `/src/Rpc/`
2. **Modifying Orleans**: Minimize changes, document in `/granville/fork-maintenance/MODIFICATIONS-TO-UPSTREAM.md`
3. **Scripts**: Use and update scripts in `/granville/scripts/`
4. **Documentation**: Update docs in `/granville/docs/`

## Assembly Compatibility

The fork supports two approaches for compatibility with packages expecting Microsoft.Orleans:
1. **Type Forwarding** (Option 1): Shim assemblies that forward types
2. **Assembly Redirects** (Option 2): Runtime redirection of assembly loads

See `/granville/compatibility-tools/README.md` for details.

## Specific Component Guides

- For RPC samples: See `/granville/samples/Rpc/CLAUDE.md`
- For RPC implementation: See `/src/Rpc/docs/`

## Important Files

1. `/granville/REPO-ORGANIZATION.md` - Repository structure guide
2. `/granville/fork-maintenance/MODIFICATIONS-TO-UPSTREAM.md` - All upstream changes
3. `/Directory.Build.targets` - MSBuild customizations for assembly renaming
4. `/granville/scripts/build-granville.ps1` - Main build script