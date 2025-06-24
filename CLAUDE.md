# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository Overview

This is **Forkleans**, an experimental fork of Microsoft Orleans focused on adding a feature codenamed "RPC", which provides a one client to many servers mode over extensible transports (with a focus on reliable UDP) for use in games.  RPC servers may also be RPC clients to other RPC servers, and RPC servers may also be Orleans clients.  Orleans is a cross-platform framework for building robust, scalable distributed applications using the Virtual Actor Model.

Key differences from upstream Orleans:
- All namespaces renamed from `Orleans` to `Forkleans`
- Package prefix changed to `Forkleans.`
- DLL name prefix for all original Orleans DLLs changed to `Forkleans.`
- Version suffix includes `-fork`
- Original filenames preserved for easier merging

## Essential Commands

### Building
```bash
# Windows
./Build.cmd              # Build in Debug mode
./build.ps1 -c Release   # Build in Release mode

# Cross-platform
dotnet build             # Build entire solution
dotnet build -c Release  # Build in Release mode
```

### Testing
```bash
# Run Basic Verification Tests (BVT)
./Test.cmd

# Run all tests (BVT + Functional)
./TestAll.cmd

# Run a specific test
dotnet test --filter "FullyQualifiedName~TestName" --no-build -c Debug

# Run tests with specific category
dotnet test --filter "Category=BVT" --no-build -c Debug

# Run tests in parallel (custom script)
./Parallel-Tests.ps1
```

### Fork Maintenance
```bash
# Convert namespaces after upstream merge
./Convert-OrleansNamespace.ps1

# Update from upstream Orleans
./Update-Fork.ps1

# Fix fork after updates
./Fix-Fork.ps1
```

## Architecture Overview

### Core Components
- **Grains**: Virtual actors with stable identity, behavior, and state
- **Silos**: Host processes that run grains
- **Clients**: Connect to silos to invoke grain methods
- **Clusters**: Groups of silos working together

### Project Structure
```
/fork/                                    # Fork-related documentation and scripts should go here, so we can avoid polluting and intermixing Orleans files with our own.

/samples/Rpc/                         # This is the root of the primary sample project and integration test called "Shooter" that is the primary driver of the new "RPC" functionality we are creating in this repo.
  docs/                                    # Contains further docs and status and roadmap items, and is very relevant for this repo's purpose of being able to support the "Shooter" integration test/sample.

/src/
  Rpc/                                    # This is where the new "RPC" functionality lives, which is original to this fork repo, and this is the only tree within /src/ that we are trying to make changes to (leaving the rest of Orleans as untouched as possible.) 
  Orleans.Core.Abstractions/    # Core interfaces and abstractions
  Orleans.Core/                 # Core functionality
  Orleans.Runtime/              # Silo runtime implementation
  Orleans.Client/               # Client library
  Orleans.Server/               # Server hosting
  Orleans.Serialization/        # Serialization framework

  # Provider implementations
  AWS/                          # AWS-based clustering, persistence, streaming
  Azure/                        # Azure-based providers
  AdoNet/                       # SQL-based providers
  Redis/                        # Redis-based providers

/test/
  DefaultCluster.Tests/         # Basic functionality tests
  Tester/                       # Integration tests
  TesterInternal/               # Internal integration tests
  Orleans.Serialization.UnitTests/

```

### Key Patterns

1. **Provider Model**: Extensible architecture for clustering, persistence, streaming, and reminders
2. **Code Generation**: Uses source generators for grain proxies and serializers
3. **Streaming**: Persistent streams with various provider implementations
4. **Transactions**: Distributed transaction support with ACID guarantees

### Development Guidelines

1. **Namespace Handling**:
   - Production code uses `Forkleans` namespace
   - Test projects use alias: `<Using Include="Orleans" Alias="Forkleans" />`
   - Never manually change Orleans to Forkleans - use the conversion script

2. **Testing**:
   - Tests categorized as BVT (Basic Verification), SlowBVT, or Functional
   - Default timeout: 10 minutes per test
   - Tests run with xUnit v3 framework

3. **Build Configuration**:
   - Uses Central Package Management (Directory.Packages.props)
   - C# 12 with strict mode enabled
   - TreatWarningsAsErrors enabled
   - Custom analyzers in Orleans.Analyzers project

4. **Fork Sync Process**:
   - Merge upstream changes to `update-from-upstream` branch
   - Run `Update-Fork.ps1` to apply namespace conversions
   - Fix any conflicts manually
   - Create PR to main branch

### Important Files
- `global.json`: .NET SDK version (8.0.410)
- `Directory.Build.props`: Global build properties
- `Directory.Packages.props`: Central package versions
- `Orleans.sln`: Main solution file from upstream (untouched Orleans)
- `Orleans-with-Rpc.sln`: Main solution file for this fork repo (includes new RPC functionality)
- `fork-maintenance-guide.md`: Detailed fork maintenance instructions
- `forkleans-version-bump-guide.md`: Guide for bumping Forkleans NuGet package versions

### Bumping the version number

- We always want to bump the revision part of the version (because the rest of the version is staying in sync with upstream Orleans.)
- Also, you always seem to time out on `forkleans-version-bump.ps1 -Mode All` after 2 minutes. Is there a way you can use a longer timeout such as 5 minutes?
- And it seems better to do `-Mode All` so we rebuild all packages.  Maybe in some cases we only need to bump the RPC packages.

### Log Files

The Shooter sample projects write detailed logs to help with debugging:
- **ActionServer logs**: `samples/Rpc/Shooter.ActionServer/logs/*.log`
- **Client logs**: `samples/Rpc/Shooter.Client/logs/*.log`
- **Silo logs**: `samples/Rpc/Shooter.Silo/logs/*.log`
- **Bot logs**: `samples/Rpc/Shooter.Bot/logs/*.log`

These logs contain detailed information about zone transitions, entity updates, and RPC communications.

### RPC sample: Shooter

An elaborate sample of the RPC capability exists in `samples/Rpc`

- Aspire project easily launches all aspects, including replicas: `samples/Rpc/Shooter.AppHost`
- This sample references Forkleans including Orleans and RPC capabilities via Nuget packages, so keep this in mind when debugging the Shooter sample.

## Migration Notes

- **Orleans 7.0 Update**:
  * The ConfigureApplicationParts method has been removed in Orleans 7.0, as mentioned in the migration documentation.

## Notes about Keyed Services and Dependency Injection

Using [FromKeyedServices] Attribute:
You can inject keyed services into constructors or methods using the [FromKeyedServices] attribute, while unkeyed services are injected without it. For example:

``` C#
public class NotificationHandler
{
    private readonly INotificationService _defaultService;
    private readonly INotificationService _emailService;

    public NotificationHandler(
        INotificationService defaultService,
        [FromKeyedServices("email")] INotificationService emailService)
    {
        _defaultService = defaultService;
        _emailService = emailService;
    }

    public void SendNotifications(string message)
    {
        Console.WriteLine(_defaultService.Send(message)); // Uses unkeyed service
        Console.WriteLine(_emailService.Send(message));   // Uses keyed service
    }
}
```

However, when we want to avoid modifying code (we want to leave original Orleans code, not related to RPC, as untouched as possible), then we might have to resort to the other strategy of a factory method when adding a singleton to an IServicesCollection.  But hopefully we don't have to do this.

Alternative:
``` C#
services.TryAddKeyedSingleton<GrainInterfaceTypeToGrainTypeResolver>("rpc", (sp, key) => new GrainInterfaceTypeToGrainTypeResolver(
                sp.GetRequiredKeyedService<IClusterManifestProvider>("rpc")));
```

## Sub-area CLAUDE.md Files

Some directories contain their own CLAUDE.md files with specific guidance:
- `samples/Rpc/CLAUDE.md` - Guidance for working with the Shooter RPC sample, including debugging scripts and workflows

When working in these areas, consult both this root CLAUDE.md and the relevant sub-area CLAUDE.md files.
