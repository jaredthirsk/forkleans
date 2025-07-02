# Building Granville Orleans

This guide covers how to build all components of the Granville Orleans fork, including the core assemblies, RPC implementation, and sample applications.

## Prerequisites

- .NET 8.0 SDK or later
- .NET 9.0 SDK (for Aspire samples)
- Windows, Linux, or macOS
- Git

## Quick Start

To build everything with a single command:

```bash
# From repository root
./granville/scripts/build-all.sh

# Or on Windows
./granville/scripts/build-all.ps1
```

## Building Components Individually

### 1. Build Granville Orleans Core Assemblies

The core Orleans assemblies are renamed from `Microsoft.Orleans.*` to `Granville.Orleans.*`:

```bash
# From repository root
./granville/scripts/build-granville.sh

# Or on Windows
./granville/scripts/build-granville.ps1
```

This builds:
- `Granville.Orleans.Core`
- `Granville.Orleans.Core.Abstractions`
- `Granville.Orleans.Serialization`
- `Granville.Orleans.Serialization.Abstractions`
- `Granville.Orleans.CodeGenerator`
- `Granville.Orleans.Analyzers`
- `Granville.Orleans.Runtime`

Output: `src/Orleans.*/bin/Release/net8.0/Granville.Orleans.*.dll`

### 2. Build Granville RPC

The RPC implementation adds high-performance UDP communication:

```bash
# From repository root
./granville/scripts/build-granville-rpc.ps1
```

This builds:
- `Granville.Rpc.Abstractions`
- `Granville.Rpc.Client`
- `Granville.Rpc.Server`
- `Granville.Rpc.Sdk`
- Transport implementations (LiteNetLib, Ruffles)

### 3. Build and Package Everything

To create all NuGet packages (Orleans, RPC, and compatibility shims):

```bash
# 1. Build and package Granville Orleans assemblies
./granville/scripts/build-granville.ps1
./granville/scripts/pack-granville-orleans-packages.ps1

# 2. Build and package Granville RPC
./granville/scripts/build-granville-rpc-packages.ps1

# 3. Generate type-forwarding compatibility shims
cd granville/compatibility-tools
./generate-individual-shims.ps1
./package-shims-direct.ps1
cd ../..
```

All packages are output to: `Artifacts/Release/`

This creates:
- **Granville.Orleans.*** - Core Orleans implementation packages
- **Granville.Rpc.*** - RPC functionality packages  
- **Microsoft.Orleans.***-granville-shim** - Type-forwarding compatibility packages

### 4. Build the Shooter Sample

The Shooter sample demonstrates Orleans + RPC + Aspire:

```bash
cd granville/samples/Rpc
dotnet build GranvilleSamples.sln
```

Or build individual components:

```bash
cd granville/samples/Rpc
dotnet build Shooter.Silo
dotnet build Shooter.ActionServer
dotnet build Shooter.Client
dotnet build Shooter.AppHost
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

## Assembly Compatibility and Type-Forwarding Shims

Granville Orleans provides two approaches for compatibility with packages expecting Microsoft.Orleans:

### 1. Assembly Redirects (Runtime)
Runtime redirection of assembly loads (implemented in Shooter.Silo). See `/granville/compatibility-tools/ASSEMBLY-REDIRECT-GUIDE.md`.

### 2. Type-Forwarding Shims (Compile-time)
Create Microsoft.Orleans.* packages that forward types to Granville.Orleans assemblies. This enables third-party packages to compile against Microsoft.Orleans interfaces while using Granville.Orleans implementations at runtime.

#### Building Type-Forwarding Shim Packages

1. **Build Granville Orleans assemblies first:**
```bash
./granville/scripts/build-granville.ps1
```

2. **Package Granville Orleans assemblies:**
```bash
./granville/scripts/pack-granville-orleans-packages.ps1
```

3. **Generate type-forwarding shim assemblies:**
```bash
cd granville/compatibility-tools
./generate-individual-shims.ps1
```

4. **Package the shim assemblies:**
```bash
./package-shims-direct.ps1
```

#### Output
All packages are created in `Artifacts/Release/`:
- **Granville.Orleans.* packages**: Implementation assemblies (from step 2)
- **Microsoft.Orleans.* packages**: Type-forwarding shim packages (from step 4)

#### Usage
Third-party packages reference Microsoft.Orleans.* shims, which forward types to Granville.Orleans implementations:
```xml
<PackageReference Include="Microsoft.Orleans.Core.Abstractions" Version="9.1.2.51-granville-shim" />
<PackageReference Include="UFX.Orleans.SignalRBackplane" Version="8.2.2" />
```

At runtime, types resolve to Granville.Orleans assemblies automatically.

## Local NuGet Feed

To test packages locally:

```bash
./granville/scripts/setup-local-feed.ps1
```

This creates a local feed at `~/local-nuget-feed/`.

## Troubleshooting

### Build Failures

1. **Missing dependencies**: Run the build script which handles dependency order
2. **MSBuild errors**: Ensure you have the correct .NET SDK versions
3. **Permission errors**: On Linux/Mac, make scripts executable: `chmod +x *.sh`

### Assembly Reference Issues

The `Directory.Build.targets` automatically renames assemblies. If you get reference errors:
1. Check that compatibility copies are being created (Orleans.*.dll alongside Granville.Orleans.*.dll)
2. Verify the build order - some assemblies depend on others

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

## Version Management

To update version numbers:

```bash
./granville/scripts/bump-granville-version.ps1 -NewVersion "9.1.3-granville"
```

## Clean Build

To clean all build artifacts:

```bash
./granville/scripts/clean-build-artifacts.ps1
```

## Next Steps

- See `/granville/REPO-ORGANIZATION.md` for repository structure
- See `/granville/compatibility-tools/README.md` for compatibility strategies
- See `/granville/samples/Rpc/CLAUDE.md` for sample-specific guidance