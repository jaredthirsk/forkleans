# Granville Orleans Build Details

This document provides detailed information about the Granville Orleans fork build process, package dependencies, and code generation strategies.

## Table of Contents
1. [Package Categories](#package-categories)
2. [Shimmed Packages and Their Dependencies](#shimmed-packages-and-their-dependencies)
3. [Code Generation Strategy](#code-generation-strategy)
4. [Build Types and Code Generation](#build-types-and-code-generation)
5. [Package Reference Requirements](#package-reference-requirements)
6. [Shooter Sample Build Strategy](#shooter-sample-build-strategy)

## Package Categories

### 1. Modified with InternalsVisibleTo (Core Modified Packages)
These packages have been modified to expose internals to Granville.Rpc assemblies:
- **Granville.Orleans.Core.Abstractions** - Modified for RPC access to grain interfaces
- **Granville.Orleans.Core** - Modified for RPC access to grain runtime
- **Granville.Orleans.Runtime** - Modified for RPC access to silo runtime (also added InternalsVisibleTo for Granville.Orleans.Streaming and Granville.Orleans.TestingHost)
- **Granville.Orleans.Serialization.Abstractions** - Modified for RPC serialization
- **Granville.Orleans.Serialization** - Modified for RPC serialization implementation
- **Granville.Orleans.Transactions** - Modified to add InternalsVisibleTo for Granville.Orleans.Transactions.TestKit.Base

### 2. Dependencies of Modified Packages
These are built because they depend on our modified packages and need to reference Granville versions:
- **Granville.Orleans.Server** - Depends on Granville.Orleans.Runtime
- **Granville.Orleans.Client** - Depends on Granville.Orleans.Core
- **Granville.Orleans.Sdk** - Meta-package that brings in analyzers and code generators
- **Granville.Orleans.Streaming** - Depends on Granville.Orleans.Runtime (needs InternalsVisibleTo)
- **Granville.Orleans.TestingHost** - Depends on Granville.Orleans.Runtime (needs InternalsVisibleTo)

### 3. Type-Forwarding Shim Packages
These packages forward types from Orleans.* to Granville.Orleans.*:
- **Microsoft.Orleans.Core.Abstractions** (9.1.2.65-granville-shim)
- **Microsoft.Orleans.Core** (9.1.2.65-granville-shim)
- **Microsoft.Orleans.Runtime** (9.1.2.65-granville-shim)
- **Microsoft.Orleans.Serialization.Abstractions** (9.1.2.65-granville-shim)
- **Microsoft.Orleans.Serialization** (9.1.2.65-granville-shim)

### 4. Code Generation Packages
- **Granville.Orleans.CodeGenerator** - Roslyn source generator for Orleans types (renamed from Orleans.CodeGenerator)
- **Granville.Orleans.Analyzers** - Code analyzers for Orleans best practices (renamed from Orleans.Analyzers)

**Important:** These packages are built as Orleans.* first, then repackaged as Granville.Orleans.* to ensure proper metadata and dependencies.

### 5. Convenience Packages (Not Modified)
These are built for convenience but not modified:
- **Granville.Orleans.Persistence.Memory** - In-memory storage provider
- **Granville.Orleans.Reminders** - Reminder service
- **Granville.Orleans.Serialization.SystemTextJson** - JSON serialization

**Why build unmodified packages?** 
- Ensures version consistency across the stack
- Prevents mixing Granville and Microsoft assemblies
- Simplifies dependency management for consumers

## Shimmed Packages and Their Dependencies

### Orleans.Core.Abstractions
**Direct Dependencies:**
- Microsoft.Extensions.Logging.Abstractions (8.0.2) - NOT shimmed
- System.Collections.Immutable (8.0.0) - NOT shimmed
- System.Memory (4.5.5) - NOT shimmed
- System.Threading.Tasks.Extensions (4.5.4) - NOT shimmed

**Analyzers:** References Orleans.Analyzers for compile-time checks

### Orleans.Core
**Direct Dependencies:**
- Orleans.Core.Abstractions
- Orleans.Serialization.Abstractions
- Microsoft.Extensions.DependencyInjection (8.0.1) - NOT shimmed
- Microsoft.Extensions.Logging (8.0.1) - NOT shimmed
- Microsoft.Extensions.Options (8.0.2) - NOT shimmed
- System.Diagnostics.DiagnosticSource (8.0.1) - NOT shimmed

**Analyzers:** References Orleans.Analyzers and Orleans.CodeGenerator

### Orleans.Runtime
**Direct Dependencies:**
- Orleans.Core
- Orleans.Serialization
- Microsoft.AspNetCore.Connections.Abstractions (8.0.11) - NOT shimmed
- Microsoft.Extensions.Configuration (8.0.0) - NOT shimmed
- Microsoft.Extensions.Hosting.Abstractions (8.0.1) - NOT shimmed

**Analyzers:** References Orleans.Analyzers and Orleans.CodeGenerator

### Orleans.Serialization.Abstractions
**Direct Dependencies:**
- System.Collections.Immutable (8.0.0) - NOT shimmed
- System.Memory (4.5.5) - NOT shimmed

**Analyzers:** None directly, but used by Orleans.CodeGenerator

### Orleans.Serialization
**Direct Dependencies:**
- Orleans.Serialization.Abstractions
- Microsoft.Extensions.DependencyInjection.Abstractions (8.0.2) - NOT shimmed
- System.IO.Pipelines (8.0.0) - NOT shimmed

**Analyzers:** References Orleans.Analyzers and Orleans.CodeGenerator

## Code Generation Strategy

### When Orleans.CodeGenerator is Used
- **Never in Granville fork** - We always use Granville.Orleans.CodeGenerator
- Only used when consuming official Microsoft.Orleans packages
- **Exception**: When third-party packages (like UFX.Orleans.SignalRBackplane) bring in Microsoft.Orleans.Sdk

### When Granville.Orleans.CodeGenerator is Used
- **Standard Orleans Build**: When `Orleans_DesignTimeBuild=false` (default)
- **Granville Build**: When `BuildAsGranville=true` and `Orleans_DesignTimeBuild=false`
- **Generates**: OrleansCodeGen types in target assemblies
- **Important**: The generated code may reference Orleans.Core.Abstractions (not Granville.Orleans.Core.Abstractions) due to assembly-qualified type names

### Code Generation Control Properties
```xml
<!-- Disable all code generation -->
<Orleans_DesignTimeBuild>true</Orleans_DesignTimeBuild>

<!-- Enable Granville code generation (default when using SDK) -->
<Orleans_DesignTimeBuild>false</Orleans_DesignTimeBuild>

<!-- Control which generator runs (obsolete - for debugging) -->
<Granville_DesignTimeBuild>true</Granville_DesignTimeBuild>
```

### Handling Duplicate Code Generation
When using third-party packages that depend on Microsoft.Orleans:
```xml
<!-- In Directory.Build.props for the sample -->
<PropertyGroup>
  <!-- Disable Orleans code generation to prevent duplicates -->
  <Orleans_DesignTimeBuild>true</Orleans_DesignTimeBuild>
</PropertyGroup>
```

## Build Types and Code Generation

### 1. Standard Orleans Build
```bash
dotnet build Orleans.sln
```
- **Code Generator**: Orleans.CodeGenerator (original)
- **Assembly Names**: Orleans.*.dll
- **Package Names**: Microsoft.Orleans.*
- **When to use**: Testing Orleans compatibility

### 2. Granville Orleans Build
```bash
dotnet build Orleans.sln -p:BuildAsGranville=true
```
- **Code Generator**: Granville.Orleans.CodeGenerator
- **Assembly Names**: Granville.Orleans.*.dll
- **Package Names**: Granville.Orleans.*
- **When to use**: Building Granville packages

### 3. Granville RPC Build
```bash
dotnet build Granville.Rpc.sln
```
- **Code Generator**: Granville.Orleans.CodeGenerator (via package reference)
- **References**: Granville.Orleans.* packages
- **When to use**: Building RPC functionality

### 4. Shooter Sample Build
```bash
cd granville/samples/Rpc
dotnet build
```
- **Code Generator**: Granville.Orleans.CodeGenerator (via Granville.Orleans.Sdk)
- **References**: Granville.Orleans.* and Granville.Rpc.* packages
- **When to use**: Building sample applications

## Package Reference Requirements

### When Explicit References are Needed

1. **Granville.Orleans.Sdk**
   - Required for: Any project using Orleans grains/interfaces
   - Brings in: Analyzers and code generators
   - Example: Shooter.Shared, Shooter.Silo

2. **Granville.Orleans.Core.Abstractions**
   - Required for: Grain interface definitions
   - Must reference when: Using IGrain, IGrainWithKey interfaces

3. **Granville.Orleans.Serialization.Abstractions**
   - Required for: Custom serializable types
   - Must reference when: Using [GenerateSerializer], [Id] attributes

### Forcing Version Resolution
When NuGet resolves to wrong versions (e.g., Microsoft.Orleans 9.2.0-preview1):

```xml
<!-- Option 1: Explicit version -->
<PackageReference Include="Granville.Orleans.Core" Version="9.1.2.65" />

<!-- Option 2: Exclude transitive Microsoft packages -->
<PackageReference Include="Microsoft.Orleans.Core" ExcludeAssets="All" />

<!-- Option 3: Use PackageReference Update -->
<PackageReference Update="Microsoft.Orleans.*" VersionOverride="9.1.2.65-granville-shim" />
```

## Shooter Sample Build Strategy

### Target Framework Requirements
All Shooter sample projects target **.NET 9.0** for consistency with Aspire 9.3.x requirements.

### Aspire Version
The Shooter sample uses **Aspire 9.3.1** which provides:
- Improved service orchestration with `WaitFor()` support
- Better service discovery
- Enhanced observability features

### Microsoft.Extensions Package Requirements
When using Aspire 9.3.x, ensure Microsoft.Extensions packages are updated to version 9.0.4 to avoid version conflicts:
- Microsoft.Extensions.Hosting: 9.0.4
- Microsoft.Extensions.Configuration: 9.0.4
- Microsoft.Extensions.Logging.Abstractions: 9.0.4
- Microsoft.Extensions.Configuration.CommandLine: 9.0.4

### Which CodeGenerator to Use
**Answer: Granville.Orleans.CodeGenerator**

The Shooter sample should use Granville's code generator because:
1. It references Granville.Orleans assemblies
2. It needs code generation for grain interfaces and serializable types
3. It uses Granville.Rpc which expects Granville.Orleans types

### Handling Assembly Reference Issues
Due to the code generator emitting assembly-qualified type names, you may need Microsoft.Orleans shim packages:
```xml
<ItemGroup>
  <!-- Granville Orleans packages -->
  <PackageReference Include="Granville.Orleans.Sdk" />
  
  <!-- Microsoft Orleans shims for compatibility -->
  <PackageReference Include="Microsoft.Orleans.Core.Abstractions" />
  <PackageReference Include="Microsoft.Orleans.Core" />
</ItemGroup>
```

### Build Configuration for Shooter
```xml
<PropertyGroup>
  <!-- Use default Orleans_DesignTimeBuild=false to enable code generation -->
  <!-- Do NOT set Orleans_DesignTimeBuild=true unless debugging -->
</PropertyGroup>

<ItemGroup>
  <!-- Core Granville packages -->
  <PackageReference Include="Granville.Orleans.Sdk" />
  <PackageReference Include="Granville.Orleans.Server" /> <!-- For Silo -->
  <PackageReference Include="Granville.Orleans.Client" /> <!-- For Client -->
  
  <!-- Granville RPC -->
  <PackageReference Include="Granville.Rpc.Sdk" />
</ItemGroup>
```

### Common Issues and Solutions

1. **CS0433 Duplicate Type Errors**
   - Cause: Both Orleans.dll and Granville.Orleans.dll loaded
   - Solution: Ensure only Granville packages referenced
   - Check: `dotnet list package --include-transitive`

2. **Missing OrleansCodeGen Types**
   - Cause: Code generation disabled
   - Solution: Remove `Orleans_DesignTimeBuild=true`

3. **Version Conflicts**
   - Cause: NuGet resolving Microsoft.Orleans from nuget.org
   - Solution: Use local feed priority in NuGet.config

4. **CS0012 Type 'IGrainWithStringKey' not found**
   - Cause: Generated code references Orleans.Core.Abstractions
   - Solution: Add Microsoft.Orleans shim packages
   - Alternative: Ensure shim packages are in local feed

5. **FileNotFoundException for Orleans.*.dll at runtime**
   - Cause: Missing shim packages
   - Solution: Add Microsoft.Orleans.* shim packages (version 9.1.2.XX-granville-shim)

6. **CS0101 Duplicate definitions (code generation)**
   - Cause: Both Microsoft.Orleans.CodeGenerator and Granville.Orleans.CodeGenerator running
   - Solution: Set `Orleans_DesignTimeBuild=true` in Directory.Build.props

## Build Scripts and Automation

### Key Build Scripts

1. **build-granville-orleans-packages.ps1**
   - Builds all Granville.Orleans packages
   - Reads version from `granville/current-revision.txt`
   - Cleans up any Microsoft.Orleans packages without -granville-shim suffix

2. **package-minimal-shims.ps1**
   - Creates Microsoft.Orleans shim packages with type forwarding
   - Adds -granville-shim suffix to version

3. **fix-granville-dependencies.ps1**
   - Fixes dependencies in Granville.Orleans packages
   - Replaces Microsoft.Orleans.Analyzers/CodeGenerator with Granville versions
   - Only fixes dependencies for packages we have shims for

4. **fix-rpc-package-dependencies.ps1**
   - Fixes dependencies in Granville.Rpc packages
   - Ensures they reference Granville.Orleans instead of Microsoft.Orleans

5. **repackage-analyzer-packages.ps1**
   - Creates Granville versions of analyzer packages
   - Fixes metadata and dependencies

### Version Management

**Current Revision Tracking:**
- Primary: `granville/current-revision.txt` (e.g., contains "65")
- Fallback: `Directory.Build.props` `<GranvilleRevision>` property
- Full version: 9.1.2.{revision} (e.g., 9.1.2.65)

**Shim Package Versions:**
- Microsoft.Orleans shim packages use version suffix: -granville-shim
- Example: Microsoft.Orleans.Core 9.1.2.65-granville-shim
- Directory.Build.targets in samples must be updated when revision changes

**Important:** Always clear NuGet caches when changing versions:
```bash
dotnet nuget locals all --clear
```

## Summary

The Granville Orleans fork maintains compatibility while adding InternalsVisibleTo support for Granville.Rpc. The build system:

1. **Modifies only necessary packages** - 6 core packages with InternalsVisibleTo
2. **Builds dependent packages** - To maintain version consistency
3. **Provides shims** - For compatibility with third-party packages and generated code
4. **Uses Granville code generator** - For all Granville-based projects
5. **Supports multiple build scenarios** - From Orleans testing to production Granville apps
6. **Handles analyzer packages specially** - Repackaged as Granville versions to avoid Microsoft package creation

For the Shooter sample and other Granville applications, always use Granville.Orleans packages with the Granville.Orleans.CodeGenerator for consistent behavior and proper RPC integration. Include Microsoft.Orleans shim packages when encountering assembly reference issues from generated code.