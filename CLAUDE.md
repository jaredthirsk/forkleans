# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository Overview

This is **Forkleans**, an experimental fork of Microsoft Orleans focused on adding a one to one client server mode over extensible transports (with a focus on reliable UDP) for use in games. Orleans is a cross-platform framework for building robust, scalable distributed applications using the Virtual Actor Model.

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
/src/
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
- `Orleans.sln`: Main solution file
- `fork-maintenance-guide.md`: Detailed fork maintenance instructions
