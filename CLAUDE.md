# CLAUDE.md - Granville Orleans Fork

## Overview

This is a fork of Microsoft Orleans that:
1. Adds Granville RPC functionality for high-performance UDP-based communication
2. Renames all assemblies from `Microsoft.Orleans.*` to `Granville.Orleans.*` to avoid NuGet namespace conflicts
3. Maintains compatibility with third-party packages expecting Microsoft.Orleans assemblies

### Naming conventions

We want to avoid impersonating official Microsoft packages.  Here is our naming strategy:

- Official Orleans (from Microsoft via nuget, and in the upstream repo)
  - Package prefix: `Microsoft.Orleans`
  - DLL prefix: `Orleans`
  - C# Namespace prefix: `Orleans`
- Granville Shim
  - Package prefix: `Microsoft.Orleans`
  - Package version suffix: `-granville-shim` (to make it obvious we are a shim and not the official Microsoft package)
  - DLL prefix: `Orleans`
  - C# Namespace prefix: `Orleans`
- Granville
  - Package prefix: `Granville`
  - DLL prefix: `Granville`
  - Granville's original RPC features:
    - Package prefix: `Granville.Rpc`
    - DLL prefix: `Granville.Rpc`
    - C# Namespace prefix: `Granville.Rpc`
  - Granville's forked Orleans DLLs:  (only around 5 of them, for InternalsVisibleTo)
    - Package prefix: `Granville.Orleans`
    - DLL prefix: `Granville.Orleans`
    - C# Namespace prefix: `Orleans`

## Repository Organization

**IMPORTANT**: See `/granville/REPO-ORGANIZATION.md` for detailed repository structure and guidelines.

Key principles:
- Orleans upstream files are kept as untouched as possible
- All Granville-specific additions are under `/granville/`
  - Exception: our extension in `/src/Rpc/` could be considered for upstream adoption by Orleans
- Modifications to upstream files are documented in `/granville/fork-maintenance/MODIFICATIONS-TO-UPSTREAM.md`

## Key Directories

- `/Artifacts/Release/` - local nuget feed, and nupkg pack output
- `/granville/` - All Granville-specific content:
  - `/granville/scripts/` - Build and maintenance scripts
  - `/granville/docs/` - Granville-specific documentation
  - `/granville/test/` - Test projects
  - `/granville/samples/` - Sample applications including the Shooter game
  - `/granville/compatibility-tools/` - Tools for Microsoft.Orleans compatibility
  - `/granville/fork-maintenance/` - Documentation of upstream modifications
- `/src/Rpc/` - Granville RPC implementation (kept in src/ as we hope for upstream adoption)

## Building Granville Orleans

See `/granville/docs/BUILDING.md` for build approaches.

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
5. `/Directory.Build.props` - contains the current revision part of the version like this: "<GranvilleRevision Condition=" '$(GranvilleRevision)'=='' ">99</GranvilleRevision>"

# Goals

- Let's try to make everything production-ready.
  - That means we can try temporary workarounds to experiment, but we should quickly clean those up and modify the repo so it is in a polished state, whether source files, or script tools
  - We should be able to adopt changes from upstream Orleans, run our fork maintenance scripts, and be at a production-ready state again (unless there are breaking changes from Orleans that we need to address.Exp)
- To demonstrate we did not make significant changes to Orleans in this fork, `Orleans.sln` should still build properly, and Orleans tests in `test/` should still work.
- We want developers to be able to use Granville RPC by referencing Granville.Rpc.* Nuget packages.
- We want to give consumers of Granville RPC the option of also using Orleans and 3rd party extensions (such as UFX.Orleans.SignalRBackplane) in their project in one of two ways:
  - reference official Orleans Nuget packages in their application (Microsoft.Orleans.*), and use Orleans side-by-side in a disjoint way: 
    - e.g. both Microsoft.Orleans.Core.dll and Granville.Orleans.Core.dll will be used at runtime.
  - use Granville.Orleans.Core and use a redirect technique described in `/granville/compatibility-tools/ASSEMBLY-REDIRECT-GUIDE.md`
    - The Shooter demo demonstrates this approach
    - e.g. only Granville.Orleans.Core.dll will be used at runtime.  (If Microsoft.Orleans.Core.dll is present, it is only a shim with many `TypeForwardedTo`)
- The Shooter sample should be an example application for consumers of Granville RPC, and therefore should use package references
- I want this fork repo to keep up with Orleans, and I plan to update it and release Granville RPC at least every time Orleans publishes a release (sometimes maybe even a preview release)
  - I want to follow their versioning numbers such as 9.1.2, but for our own versioning purposes, add a 4th revisioning number such as 9.1.2.50, and we can increment that revisioning number on our own to help us avoid our own version conflict issues.  I am ok if we bump that a lot, and get up to a big revision number, if it helps us deal with versioning more easily.

# Preferences

- prefer powershell scipts (.ps1) instead of bash scripts (.sh), unless something can only be practically done by a bash script
- when creating powershell scripts, put this in the first line: `#!/usr/bin/env pwsh` 

