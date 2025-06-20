# Forkleans Version Bump Guide

This guide explains how to bump the Forkleans NuGet package version number.

## Overview

Forkleans packages use a 4-part version number: `MAJOR.MINOR.PATCH.REVISION-SUFFIX`
- Example: `9.2.0.3-preview3`

## Steps to Bump Version

### 1. Update the Version in Directory.Build.props

Edit `/Directory.Build.props` and update the `<Version>` property:

```xml
<PropertyGroup>
  <Version>9.2.0.3-preview3</Version>  <!-- Update this line -->
</PropertyGroup>
```

Location: Around line 108 in the file.

### 2. Build the Core Forkleans Projects

Build the essential projects first to ensure they compile:

```bash
# Build core projects
dotnet build src/Orleans.Core.Abstractions/Orleans.Core.Abstractions.csproj -c Release
dotnet build src/Orleans.Core/Orleans.Core.csproj -c Release
dotnet build src/Orleans.Client/Orleans.Client.csproj -c Release

# Build RPC projects
dotnet build src/Rpc/Orleans.Rpc.Abstractions/Orleans.Rpc.Abstractions.csproj -c Release
dotnet build src/Rpc/Orleans.Rpc.Sdk/Orleans.Rpc.Sdk.csproj -c Release
dotnet build src/Rpc/Orleans.Rpc.Client/Orleans.Rpc.Client.csproj -c Release
dotnet build src/Rpc/Orleans.Rpc.Server/Orleans.Rpc.Server.csproj -c Release
dotnet build src/Rpc/Orleans.Rpc.Transport.LiteNetLib/Orleans.Rpc.Transport.LiteNetLib.csproj -c Release
```

### 3. Create NuGet Packages

Use the PowerShell script to create packages:

```powershell
# Windows PowerShell
./Create-ForkleansPackages.ps1 -Configuration Release -VersionSuffix preview3 -SkipBuild

# Or on Linux/WSL
pwsh -Command "./Create-ForkleansPackages.ps1 -Configuration Release -VersionSuffix preview3 -SkipBuild"
```

The script will:
- Create packages with the version from Directory.Build.props
- Place them in `./local-packages` directory (works on both Windows and Linux)
- Skip building if you already built in step 2

### 4. Update Sample Projects

Update any sample projects that reference the old version. For example, in the Shooter sample:

```bash
# Find all project files referencing Forkleans packages
grep -r "Forkleans.*Version=" samples/ --include="*.csproj"
```

Update each reference to the new version:
```xml
<PackageReference Include="Forkleans.Rpc.Server" Version="9.2.0.3-preview3" />
<PackageReference Include="Forkleans.Rpc.Client" Version="9.2.0.3-preview3" />
<!-- etc. -->
```

### 5. Clear NuGet Caches and Test

Clear caches to ensure the new packages are used:

```bash
# Clear NuGet HTTP cache
dotnet nuget locals http-cache --clear

# Build a sample project to test
cd samples/Rpc
dotnet build Shooter.ActionServer/Shooter.ActionServer.csproj -c Debug
```

## Important Files and Locations

- **Version Definition**: `/Directory.Build.props` (line ~108)
- **Package Output**: `/local-packages/` 
- **NuGet Config**: `/NuGet.config` (configured to use `./local-packages`)
- **Package Script**: `/Create-ForkleansPackages.ps1`

## Version Numbering Convention

- **Major**: Follows Orleans major version (currently 9)
- **Minor**: Follows Orleans minor version (currently 2)
- **Patch**: For Forkleans-specific major changes (currently 0)
- **Revision**: For Forkleans-specific minor changes/fixes (increment this most often)
- **Suffix**: Preview/alpha/beta/rc designation (e.g., `-preview3`)

## Troubleshooting

### "Unable to find package" errors
1. Check that packages exist in `/local-packages/`
2. Clear all NuGet caches: `dotnet nuget locals all --clear`
3. Ensure NuGet.config points to `./local-packages`

### Build failures when creating packages
1. Build projects individually first (step 2)
2. Some projects may fail to pack - focus on the essential ones listed in step 2

### Cross-platform issues
- The relative path `./local-packages` works on both Windows and Linux
- Use forward slashes in paths for compatibility
- Run PowerShell scripts with `pwsh` on Linux/WSL

## Quick Version Bump Checklist

- [ ] Update `<Version>` in Directory.Build.props
- [ ] Build core Forkleans projects
- [ ] Run Create-ForkleansPackages.ps1
- [ ] Update sample project references
- [ ] Clear NuGet caches
- [ ] Test build