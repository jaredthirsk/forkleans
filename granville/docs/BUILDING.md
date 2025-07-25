# Building Granville Orleans

This guide covers how to build all components of the Granville Orleans fork, including the core assemblies, RPC implementation, and sample applications.

## Prerequisites

- .NET 8.0 SDK or later
- .NET 9.0 SDK (for Aspire samples)
- Windows, Linux, or macOS
- Git

## Build Organization

The Granville Orleans fork supports multiple build scenarios:

1. **Upstream Verification** - Validates that official Orleans builds correctly without modifications
2. **Granville Orleans** - Builds required Orleans assemblies as Granville.Orleans.* packages
3. **Type-Forwarding Shims** - Creates Microsoft.Orleans.* packages that forward to Granville
4. **Granville RPC** - Builds the RPC implementation as Granville.Rpc.* packages
5. **Sample Applications** - Demonstrates usage with the Shooter game sample

## Quick Start

To build everything with a single command:

```bash
# From repository root
./granville/scripts/build-all-granville.ps1

# This runs all build steps in order:
# 1. Builds Granville Orleans assemblies
# 2. Builds type-forwarding shims
# 3. Sets up local NuGet feed
# 4. Builds Shooter sample
```

## Build Scenarios

### 1. Verify Upstream Orleans (No Modifications)

To verify the upstream Orleans builds correctly:

```bash
./granville/scripts/build-upstream-verification.ps1
```

This builds `Orleans.sln` with default settings (BuildAsGranville=false), producing Orleans.* assemblies.

### 2. Build Granville Orleans Core Assemblies

To build Orleans assemblies renamed to Granville.Orleans.*:

```bash
./granville/scripts/build-granville-orleans.ps1
./granville/scripts/pack-granville-orleans-packages.ps1
```

This produces the following packages:
- `Granville.Orleans.Core` and `Core.Abstractions`
- `Granville.Orleans.Serialization` and `Serialization.Abstractions`
- `Granville.Orleans.CodeGenerator` and `Analyzers`
- `Granville.Orleans.Runtime`, `Server`, `Client`
- `Granville.Orleans.Sdk`

Output: `Artifacts/Release/Granville.Orleans.*.nupkg`

### 3. Build Type-Forwarding Shims

To build Microsoft.Orleans.* packages that forward types to Granville:

```bash
./granville/scripts/build-shims.ps1
```

This generates shim assemblies that enable third-party packages to compile against Microsoft.Orleans while using Granville.Orleans at runtime.

Output: `Artifacts/Release/Microsoft.Orleans.*-granville-shim.nupkg`

### 4. Build Granville RPC Packages

To build the RPC implementation:

```bash
./granville/scripts/build-granville-rpc.ps1
```

This produces:
- `Granville.Rpc.Core` - Core RPC functionality
- `Granville.Rpc.Client` - Client-side RPC implementation
- `Granville.Rpc.Runtime` - Server-side RPC runtime
- `Granville.Rpc.Sdk` - Meta-package for RPC development

Output: `Artifacts/Release/Granville.Rpc.*.nupkg`

### 5. Build Sample Applications

To build the Shooter sample:

```bash
./granville/scripts/build-shooter-sample.ps1

# Or to build and run immediately:
./granville/scripts/build-shooter-sample.ps1 -RunAfterBuild
```

This builds all Shooter components using packages from the local NuGet feed.

## BuildAsGranville Property

The build system uses the `BuildAsGranville` MSBuild property to control assembly naming:

- **BuildAsGranville=false** (default) - Builds as Orleans.* assemblies
- **BuildAsGranville=true** - Builds as Granville.Orleans.* assemblies

This property is set automatically by the build scripts but can be overridden:

```bash
# Build specific project as Granville
dotnet build src/Orleans.Core/Orleans.Core.csproj -p:BuildAsGranville=true

# Build as original Orleans (default)
dotnet build src/Orleans.Core/Orleans.Core.csproj
```

## Running the Shooter Sample

### Using Aspire (Recommended)

```bash
cd granville/samples/Rpc/Shooter.AppHost
dotnet run
```

This starts all components with orchestration. Access the Aspire dashboard at the URL shown in the console.

### Manual Startup

1. Start the Orleans Silo:
```bash
cd granville/samples/Rpc/Shooter.Silo
dotnet run
```

2. Start ActionServers (in separate terminals):
```bash
cd granville/samples/Rpc/Shooter.ActionServer
dotnet run --urls http://localhost:7072
dotnet run --urls http://localhost:7073  # Second instance
```

3. Start the Client:
```bash
cd granville/samples/Rpc/Shooter.Client
dotnet run
```

## Assembly Compatibility

Granville Orleans provides two approaches for compatibility with packages expecting Microsoft.Orleans:

### 1. Assembly Redirects (Runtime)
Runtime redirection of assembly loads. See `/granville/compatibility-tools/ASSEMBLY-REDIRECT-GUIDE.md`.

### 2. Type-Forwarding Shims (Compile-time) 
Microsoft.Orleans.* packages that forward types to Granville.Orleans assemblies. Build using:

```bash
./granville/scripts/build-shims.ps1
```

Third-party packages can then reference the shim packages:
```xml
<PackageReference Include="Microsoft.Orleans.Core" Version="9.1.2.*-granville-shim" />
<PackageReference Include="UFX.Orleans.SignalRBackplane" Version="8.2.2" />
```

## Local NuGet Feed

Packages are output to `Artifacts/Release/` which serves as the local NuGet feed.

To use the local feed in your projects, add a `NuGet.config` file:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-granville" value="../../Artifacts/Release" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

The Shooter sample already includes this configuration.

## Troubleshooting

### Build Failures

1. **Missing dependencies**: Use the build scripts which handle dependency order
2. **MSBuild errors**: Ensure you have the correct .NET SDK versions  
3. **Permission errors**: On Linux/Mac, make scripts executable: `chmod +x *.ps1`
4. **Wrong assembly names**: Ensure BuildAsGranville property is set correctly

### Assembly Reference Issues

The `Directory.Build.targets` conditionally renames assemblies based on BuildAsGranville:
1. Default builds (BuildAsGranville=false) produce Orleans.* assemblies
2. Granville builds (BuildAsGranville=true) produce Granville.Orleans.* assemblies
3. Use the appropriate build script for your scenario

### Shooter Sample Issues

- **Port conflicts**: Check ports 7071-7073, 5000, 11111, 30000
- **Health check errors**: Fixed in latest version (uses `/healthz` instead of `/health`)
- **Aspire not starting services**: Ensure all projects are built first

### Type-Forwarding Shim Issues

- **Empty shim assemblies**: Ensure Granville Orleans assemblies are built first
- **Missing dependencies**: Build packages in correct order (Orleans → RPC → Shims)
- **Type not found errors**: Verify both Microsoft.Orleans.* shims and Granville.Orleans.* packages are available
- **Generator errors**: The type-forwarding generator is in `/granville/compatibility-tools/type-forwarding-generator/`
- **Package restore failures**: Clear NuGet cache with `dotnet nuget locals all --clear`

## Advanced Usage

### Version Management

Version numbers are managed in `Directory.Build.props`:
- `<VersionPrefix>` - Orleans version (e.g., 9.1.2)
- `<GranvilleRevision>` - Fork revision number

To update the revision:
```bash
./granville/scripts/bump-granville-version.ps1
```

### Clean Build

To clean all build artifacts:

```bash
./granville/scripts/clean-build-artifacts.ps1
```

### Build Script Reference

Core build scripts:

```bash
# Build Granville Orleans assemblies
./granville/scripts/build-granville-orleans.ps1

# Pack Granville Orleans packages
./granville/scripts/pack-granville-orleans-packages.ps1

# Build type-forwarding shims
./granville/scripts/build-shims.ps1

# Build Granville RPC packages
./granville/scripts/build-granville-rpc.ps1

# Build sample applications
./granville/scripts/build-shooter-sample.ps1
```

For detailed build information, see `/granville/docs/BUILDING-DETAILS.md`.

## Next Steps

- See `/granville/REPO-ORGANIZATION.md` for repository structure
- See `/granville/docs/BUILDING-DETAILS.md` for detailed build process information
- See `/granville/compatibility-tools/README.md` for compatibility strategies
- See `/granville/samples/Rpc/README.md` for sample application documentation