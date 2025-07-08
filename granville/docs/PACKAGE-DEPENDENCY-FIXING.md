# Package Dependency Fixing for Granville Orleans

## Overview

When building Granville Orleans packages, there's a critical issue where the packages end up with dependencies on stable Microsoft.Orleans.* packages (e.g., `9.1.2.53`) that don't exist. We only publish prerelease shim packages with the `-granville-shim` suffix (e.g., `9.1.2.53-granville-shim`).

## The Problem

The NuGet pack process captures project references and converts them to package dependencies. Even though we rename assemblies to Granville.Orleans.*, the dependency metadata still references Microsoft.Orleans.* packages without the required suffix.

## The Solution

We use a post-build PowerShell script to fix package dependencies after they're built.

### Scripts

1. **`fix-granville-dependencies.ps1`** - Fixes dependencies in built NuGet packages
   - Extracts each Granville.Orleans.* package
   - Modifies the nuspec file to append `-granville-shim` to Microsoft.Orleans.* dependencies
   - Repackages the modified contents

2. **`build-granville.ps1`** - Main build script that automatically runs the dependency fixer

### How It Works

```powershell
# The script uses regex to find and fix dependencies
$pattern = '<dependency id="(Microsoft\.Orleans\.[^"]+)" version="([^"]+)"'
# Replaces with: <dependency id="$1" version="$2-granville-shim"
```

## MSBuild Approach (Attempted but Insufficient)

We attempted to fix this at the MSBuild level in `Directory.Build.targets`:
- Created targets to modify PackageDependency items before NuSpec generation
- Added post-processing to modify the generated nuspec file

However, this approach had timing issues and didn't reliably work, so we use the PowerShell post-processing approach instead.

## Verification

To verify dependencies are correct:

```powershell
# Extract and check a package
$tempDir = New-TemporaryFile | %{ Remove-Item $_; New-Item -ItemType Directory -Path $_ }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::ExtractToDirectory("path/to/package.nupkg", $tempDir)
Get-Content "$tempDir/*.nuspec" | Select-String "Microsoft.Orleans"
```

All Microsoft.Orleans.* dependencies should have the `-granville-shim` suffix.

## Known Issues

1. The MSBuild approach doesn't work reliably due to timing of when dependencies are resolved
2. Old packages in the NuGet cache can cause conflicts - clear with:
   ```bash
   rm -rf ~/.nuget/packages/granville.orleans.* ~/.nuget/packages/microsoft.orleans.*
   ```

## Future Improvements

Ideally, we would fix this at the MSBuild/NuGet pack level to avoid post-processing, but the current solution is reliable and maintainable.