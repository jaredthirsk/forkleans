# Granville Orleans Compatibility Tools

This directory contains tools to help third-party Orleans packages work with Granville Orleans assemblies.

## Overview

Since Granville Orleans builds assemblies with `Granville.Orleans.*` names (to avoid legal issues with the Microsoft prefix), we provide two approaches for compatibility with packages expecting `Microsoft.Orleans.*` assemblies:

1. **Type Forwarding Shims** - Generate thin Microsoft.Orleans.* assemblies that forward all types to Granville
2. **Assembly Binding Redirects** - Configure your application to redirect Microsoft references to Granville

## Tools Included

### GenerateTypeForwardingShims.csx
A dotnet-script that generates a type forwarding shim for a single Granville assembly.

**Usage:**
```bash
dotnet-script GenerateTypeForwardingShims.csx <granville-assembly-path> [output-directory]
```

**Example:**
```bash
dotnet-script GenerateTypeForwardingShims.csx ../src/Orleans.Core/bin/Release/net8.0/Granville.Orleans.Core.dll ./shims/
```

### GenerateAllShims.ps1
PowerShell script that generates shims for all core Orleans assemblies.

**Usage:**
```powershell
./GenerateAllShims.ps1 [-Configuration Release] [-OutputPath shims]
```

### assembly-redirects-template.config
Template XML configuration for assembly binding redirects.

**Usage:**
1. Copy relevant sections to your app.config or web.config
2. Adjust version numbers as needed
3. See ASSEMBLY-REDIRECT-GUIDE.md for detailed instructions

### ASSEMBLY-REDIRECT-GUIDE.md
Complete guide for using assembly redirects with different .NET versions and scenarios.

## Quick Start

### Option 1: Using Type Forwarding Shims

1. Build the Granville Orleans solution
2. Generate shims:
   ```powershell
   ./GenerateAllShims.ps1
   ```
3. Deploy both Granville assemblies and generated shims to your application

### Option 2: Using Assembly Redirects

1. Build the Granville Orleans solution
2. Copy assembly redirect configuration from template
3. Add to your application's config file
4. Deploy only Granville assemblies

## When to Use Each Approach

**Use Type Forwarding Shims when:**
- You want a drop-in replacement with no configuration
- You're comfortable deploying extra assemblies
- You need maximum compatibility

**Use Assembly Redirects when:**
- You want to avoid any Microsoft-named assemblies
- You have control over application configuration
- You're using .NET Framework with app.config support

## Troubleshooting

### Shim generation fails
- Ensure dotnet-script is installed: `dotnet tool install -g dotnet-script`
- Check that Granville assemblies are built first
- Verify paths are correct

### Assembly redirects not working
- For .NET Core/5+, use the custom resolver approach (see guide)
- Enable fusion logging to debug assembly resolution
- Ensure redirect versions match your Granville assembly versions

## Legal Note

These tools help you use Granville Orleans (which avoids Microsoft naming) with packages expecting Microsoft Orleans. The shims are generated locally and not distributed by the Granville project, avoiding any trademark concerns.