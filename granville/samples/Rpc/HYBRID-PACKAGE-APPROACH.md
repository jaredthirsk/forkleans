# Hybrid Package Approach for Shooter Sample

## Summary

The Shooter sample uses a **hybrid package approach** that combines:
- **Microsoft Orleans shim packages** for the 5 modified assemblies only
- **Official Microsoft Orleans packages** for all other unmodified assemblies
- **Granville Orleans packages** for the actual implementations of the 5 modified assemblies

## Why This Approach?

Only 5 Orleans assemblies were modified in the Granville fork (with InternalsVisibleTo attributes):
1. Orleans.Core.Abstractions
2. Orleans.Core
3. Orleans.Runtime
4. Orleans.Serialization.Abstractions
5. Orleans.Serialization

All other Orleans assemblies (Server, Client, Persistence.Memory, etc.) were NOT modified and can use official Microsoft packages.

## Package Configuration

### Directory.Packages.props Setup

```xml
<!-- Microsoft Orleans shim packages ONLY for the 5 modified assemblies -->
<PackageVersion Include="Microsoft.Orleans.Core" Version="9.1.2.53-granville-shim" />
<PackageVersion Include="Microsoft.Orleans.Core.Abstractions" Version="9.1.2.53-granville-shim" />
<PackageVersion Include="Microsoft.Orleans.Runtime" Version="9.1.2.53-granville-shim" />
<PackageVersion Include="Microsoft.Orleans.Serialization" Version="9.1.2.53-granville-shim" />
<PackageVersion Include="Microsoft.Orleans.Serialization.Abstractions" Version="9.1.2.53-granville-shim" />

<!-- Official Microsoft Orleans packages for unmodified assemblies -->
<PackageVersion Include="Microsoft.Orleans.Server" Version="9.1.2" />
<PackageVersion Include="Microsoft.Orleans.Client" Version="9.1.2" />
<PackageVersion Include="Microsoft.Orleans.Persistence.Memory" Version="9.1.2" />
<PackageVersion Include="Microsoft.Orleans.Reminders" Version="9.1.2" />
<PackageVersion Include="Microsoft.Orleans.Serialization.SystemTextJson" Version="9.1.2" />
<PackageVersion Include="Microsoft.Orleans.Sdk" Version="9.1.2" />
<PackageVersion Include="Microsoft.Orleans.CodeGenerator" Version="9.1.2" />
<PackageVersion Include="Microsoft.Orleans.Analyzers" Version="9.1.2" />

<!-- Granville Orleans packages ONLY for the 5 modified assemblies -->
<PackageVersion Include="Granville.Orleans.Core" Version="9.1.2.53" />
<PackageVersion Include="Granville.Orleans.Core.Abstractions" Version="9.1.2.53" />
<PackageVersion Include="Granville.Orleans.Runtime" Version="9.1.2.53" />
<PackageVersion Include="Granville.Orleans.Serialization" Version="9.1.2.53" />
<PackageVersion Include="Granville.Orleans.Serialization.Abstractions" Version="9.1.2.53" />
```

### Project File References

In your .csproj files:

```xml
<!-- Reference official Microsoft Orleans packages normally -->
<PackageReference Include="Microsoft.Orleans.Server" />
<PackageReference Include="Microsoft.Orleans.Persistence.Memory" />

<!-- The shim packages will be used automatically for the 5 modified assemblies -->
<!-- due to Directory.Packages.props configuration -->

<!-- Explicitly reference Granville packages for the modified assemblies -->
<PackageReference Include="Granville.Orleans.Core" />
<PackageReference Include="Granville.Orleans.Runtime" />
<PackageReference Include="Granville.Orleans.Serialization" />
```

## How It Works

1. **Official packages** like Microsoft.Orleans.Server reference Microsoft.Orleans.Core
2. **Shim packages** intercept those references and forward types to Granville.Orleans.Core
3. **Granville packages** provide the actual implementation
4. **No assembly redirects needed** - type forwarding handles everything at compile time

## Benefits

- Cleaner solution without runtime assembly redirects
- Smaller footprint - only 5 shim packages needed instead of all Orleans packages
- Better compatibility with third-party packages
- Clear separation between modified and unmodified Orleans assemblies

## Troubleshooting

If you see "assembly redirect" errors:
1. Remove any AssemblyRedirectHelper initialization code
2. Ensure Directory.Packages.props has the correct versions
3. Clean and rebuild the solution
4. Check that shim packages are in your local NuGet feed (/Artifacts/Release/)