# Minimal Shims Approach

## Overview

Based on analysis of the Granville Orleans fork, only 5 Orleans assemblies were actually modified:

1. **Orleans.Core.Abstractions** - Added InternalsVisibleTo for Granville assemblies
2. **Orleans.Core** - Added InternalsVisibleTo for Granville.Rpc assemblies
3. **Orleans.Runtime** - Added InternalsVisibleTo for Granville.Rpc assemblies
4. **Orleans.Serialization.Abstractions** - Added InternalsVisibleTo for Granville.Rpc assemblies
5. **Orleans.Serialization** - Added InternalsVisibleTo for Granville.Rpc assemblies

All other Orleans assemblies (Server, Client, Persistence.Memory, Reminders, etc.) were NOT modified.

## Implications

This means:
- **Only 5 assemblies need shim packages** that forward types to Granville.Orleans
- **All other assemblies can use official Microsoft.Orleans packages** from NuGet
- This significantly reduces complexity and maintenance burden

## New Scripts

Two new scripts have been created to support this minimal approach:

### 1. generate-minimal-shims.ps1
Generates type-forwarding shim assemblies for ONLY the 5 modified assemblies.

```powershell
./generate-minimal-shims.ps1
```

### 2. package-minimal-shims.ps1
Packages the 5 shim assemblies into NuGet packages.

```powershell
./package-minimal-shims.ps1
```

## Benefits

1. **Smaller footprint** - Only 5 shim packages instead of 13+
2. **Better compatibility** - Official packages work without modification
3. **Easier maintenance** - Fewer shims to update when Orleans releases new versions
4. **Clearer architecture** - Easy to see which assemblies were actually modified

## Migration Guide

If you're currently using shims for all Orleans assemblies:

1. Update your Directory.Packages.props:
   - Use shim packages ONLY for the 5 modified assemblies
   - Use official Microsoft.Orleans packages for everything else

2. Remove unnecessary Granville package references:
   - Only reference Granville packages for the 5 modified assemblies
   - Don't reference Granville.Orleans.Server, Granville.Orleans.Client, etc.

3. Disable or remove AssemblyRedirectHelper:
   - With proper shim packages, assembly redirects are not needed

## Example Configuration

```xml
<!-- Directory.Packages.props -->

<!-- Shim packages ONLY for modified assemblies -->
<PackageVersion Include="Microsoft.Orleans.Core" Version="9.1.2.53-granville-shim" />
<PackageVersion Include="Microsoft.Orleans.Core.Abstractions" Version="9.1.2.53-granville-shim" />
<PackageVersion Include="Microsoft.Orleans.Runtime" Version="9.1.2.53-granville-shim" />
<PackageVersion Include="Microsoft.Orleans.Serialization" Version="9.1.2.53-granville-shim" />
<PackageVersion Include="Microsoft.Orleans.Serialization.Abstractions" Version="9.1.2.53-granville-shim" />

<!-- Official packages for everything else -->
<PackageVersion Include="Microsoft.Orleans.Server" Version="9.1.2" />
<PackageVersion Include="Microsoft.Orleans.Client" Version="9.1.2" />
<PackageVersion Include="Microsoft.Orleans.Persistence.Memory" Version="9.1.2" />
<!-- ... etc ... -->

<!-- Granville implementations for the 5 modified assemblies -->
<PackageVersion Include="Granville.Orleans.Core" Version="9.1.2.53" />
<PackageVersion Include="Granville.Orleans.Core.Abstractions" Version="9.1.2.53" />
<PackageVersion Include="Granville.Orleans.Runtime" Version="9.1.2.53" />
<PackageVersion Include="Granville.Orleans.Serialization" Version="9.1.2.53" />
<PackageVersion Include="Granville.Orleans.Serialization.Abstractions" Version="9.1.2.53" />
```