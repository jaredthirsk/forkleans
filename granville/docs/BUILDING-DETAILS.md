# Granville Orleans Build Details

This document provides detailed information about the Granville Orleans fork build process, package dependencies, and code generation strategies.

## Table of Contents
1. [Package Categories](#package-categories)
2. [Shimmed Packages and Their Dependencies](#shimmed-packages-and-their-dependencies)
3. [Code Generation Strategy](#code-generation-strategy)
4. [Build Types and Code Generation](#build-types-and-code-generation)
5. [Package Reference Requirements](#package-reference-requirements)
6. [Shooter Sample Build Strategy](#shooter-sample-build-strategy)
7. [Build Process Details](#build-process-details)
8. [Post-Pack Manipulations](#post-pack-manipulations)
9. [File Copying Operations](#file-copying-operations)

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
- **Microsoft.Orleans.Core.Abstractions** (*-granville-shim)
- **Microsoft.Orleans.Core** (*-granville-shim)
- **Microsoft.Orleans.Runtime** (*-granville-shim)
- **Microsoft.Orleans.Serialization.Abstractions** (*-granville-shim)
- **Microsoft.Orleans.Serialization** (*-granville-shim)

### 4. Code Generation Packages
- **Granville.Orleans.CodeGenerator** - Roslyn source generator for Orleans types (renamed from Orleans.CodeGenerator)
- **Granville.Orleans.Analyzers** - Code analyzers for Orleans best practices (renamed from Orleans.Analyzers)

**Important:** These packages are built as Orleans.* first, then repackaged as Granville.Orleans.* to ensure proper metadata and dependencies.

### 5. Convenience Packages (Use Microsoft.Orleans Versions)
Certain Orleans packages are not built as Granville versions. Instead, applications should reference the official Microsoft.Orleans packages:
- **Microsoft.Orleans.Persistence.Memory** - In-memory storage provider
- **Microsoft.Orleans.Reminders** - Reminder service
- **Microsoft.Orleans.Serialization.SystemTextJson** - JSON serialization

**Rationale:** 
- These packages don't require InternalsVisibleTo modifications
- Using Microsoft versions demonstrates compatibility
- Reduces maintenance burden while maintaining full functionality

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

### 1. Upstream Verification Build
```bash
./granville/scripts/build-upstream-verification.ps1
```
- **Code Generator**: Orleans.CodeGenerator (original)
- **Assembly Names**: Orleans.*.dll
- **Package Names**: Microsoft.Orleans.*
- **When to use**: Validating Orleans compatibility without modifications

### 2. Granville Orleans Build
```bash
./granville/scripts/build-granville-orleans.ps1
./granville/scripts/pack-granville-orleans-packages.ps1
```
- **Code Generator**: Granville.Orleans.CodeGenerator
- **Assembly Names**: Granville.Orleans.*.dll
- **Package Names**: Granville.Orleans.*
- **When to use**: Building core Granville Orleans packages

### 3. Type-Forwarding Shims Build
```bash
./granville/scripts/build-shims.ps1
```
- **Code Generator**: None (shims only forward types)
- **Assembly Names**: Orleans.*.dll (minimal forwarding assemblies)
- **Package Names**: Microsoft.Orleans.*-granville-shim
- **When to use**: Creating compatibility packages for third-party libraries

### 4. Granville RPC Build
```bash
./granville/scripts/build-granville-rpc.ps1
```
- **Code Generator**: Granville.Orleans.CodeGenerator (via package reference)
- **Assembly Names**: Granville.Rpc.*.dll
- **Package Names**: Granville.Rpc.*
- **References**: Granville.Orleans.* packages
- **When to use**: Building RPC functionality

### 5. Sample Applications Build
```bash
./granville/scripts/build-shooter-sample.ps1
```
- **Code Generator**: Granville.Orleans.CodeGenerator (via Granville.Orleans.Sdk)
- **References**: Granville.Orleans.* and Granville.Rpc.* packages
- **When to use**: Building and testing sample applications

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
<PackageReference Include="Granville.Orleans.Core" Version="9.1.2.*" />

<!-- Option 2: Exclude transitive Microsoft packages -->
<PackageReference Include="Microsoft.Orleans.Core" ExcludeAssets="All" />

<!-- Option 3: Use PackageReference Update -->
<PackageReference Update="Microsoft.Orleans.*" VersionOverride="9.1.2.*-granville-shim" />
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
   - Solution: Add Microsoft.Orleans.* shim packages (version *-granville-shim)

6. **CS0101 Duplicate definitions (code generation)**
   - Cause: Both Microsoft.Orleans.CodeGenerator and Granville.Orleans.CodeGenerator running
   - Solution: Set `Orleans_DesignTimeBuild=true` in Directory.Build.props

## Build Process Details

### Build Order and Dependencies

The build process follows a specific order to handle dependencies correctly:

1. **Core Assemblies** (built first with `build-granville-orleans.ps1`):
   - Orleans.Serialization.Abstractions
   - Orleans.Serialization
   - Orleans.Core.Abstractions
   - Orleans.Core
   - Orleans.CodeGenerator
   - Orleans.Analyzers
   - Orleans.Runtime

2. **Dependent Packages** (built via pack scripts):
   - Orleans.Server
   - Orleans.Client
   - Orleans.Sdk
   - Orleans.Streaming
   - Orleans.TestingHost

3. **Type-Forwarding Shims** (built with `package-minimal-shims.ps1`):
   - Microsoft.Orleans.* packages with -granville-shim suffix

4. **Granville RPC** (built separately):
   - Granville.Rpc.* packages

### Compatibility Copies Creation

During the build process, compatibility copies are created to support scenarios where both Orleans.dll and Granville.Orleans.dll names might be expected:

```powershell
# From build-granville-orleans.ps1 Create-CompatibilityLinks function
# After building Granville.Orleans.Core.dll, it creates:
Granville.Orleans.Core.dll → Orleans.Core.dll (copy)
```

**Purpose:** These compatibility copies ensure that code expecting Orleans.dll names can still find the assemblies during the build process. This is particularly important for:
- Code generation tools that might look for specific assembly names
- Build tools that have hardcoded references to Orleans.dll
- Transitional scenarios during the build process

**Note:** These copies are created in the bin directories during build but are NOT included in the final NuGet packages.

## Post-Pack Manipulations

Several scripts modify NuGet packages after they are created to ensure proper dependencies and naming:

### 1. Dependency Fixing for Granville.Orleans Packages

**Script:** `fix-granville-dependencies.ps1`

**Operations:**
1. Extracts each Granville.Orleans.*.nupkg
2. Modifies the .nuspec file to:
   - Replace `Microsoft.Orleans.Analyzers` → `Granville.Orleans.Analyzers`
   - Replace `Microsoft.Orleans.CodeGenerator` → `Granville.Orleans.CodeGenerator`
   - Add `-granville-shim` suffix to shimmed Microsoft.Orleans dependencies
3. Repackages the .nupkg file

**Example transformation:**
```xml
<!-- Before -->
<dependency id="Microsoft.Orleans.CodeGenerator" version="9.1.2.X" />
<dependency id="Microsoft.Orleans.Core" version="9.1.2.X" />

<!-- After -->
<dependency id="Granville.Orleans.CodeGenerator" version="9.1.2.X" />
<dependency id="Microsoft.Orleans.Core" version="9.1.2.X-granville-shim" />
```

### 2. SDK Package Target File Renaming

**Script:** `fix-sdk-package.ps1`

**Operations:**
1. Extracts Granville.Orleans.Sdk.nupkg
2. Renames MSBuild target files:
   - `Microsoft.Orleans.Sdk.targets` → `Granville.Orleans.Sdk.targets`
3. Updates references within the targets files:
   - `Microsoft.Orleans.Sdk` → `Granville.Orleans.Sdk`
   - `Microsoft.Orleans.CodeGenerator` → `Granville.Orleans.CodeGenerator`
   - `Microsoft.Orleans.Analyzers` → `Granville.Orleans.Analyzers`
4. Updates the .nuspec to reference the renamed files
5. Repackages the .nupkg

### 3. Analyzer Package Fixing

**Script:** `fix-analyzer-packages.ps1`

**Operations:**
1. Processes both CodeGenerator and Analyzers packages
2. Renames any Microsoft.Orleans.* files to Granville.Orleans.*
3. Updates internal references
4. Repackages with corrected metadata

### 4. RPC Package Dependency Fixing

**Script:** `fix-rpc-package-dependencies.ps1`

**Operations:**
1. Ensures all Granville.Rpc packages reference Granville.Orleans (not Microsoft.Orleans)
2. Updates version references to match current Granville version

## File Copying Operations

The build process involves several file copying operations:

### 1. Compatibility Copies During Build

**Location:** `build-granville-orleans.ps1`

**What:** Creates Orleans.dll copies of Granville.Orleans.dll files

**When:** After each project builds successfully

**Example:**
```
bin/Release/net8.0/
├── Granville.Orleans.Core.dll (original)
└── Orleans.Core.dll (compatibility copy)
```

### 2. Shim DLL Compilation

**Location:** `compile-all-shims.ps1`

**What:** Compiles type-forwarding shim assemblies from generated C# source

**Process:**
1. Type-forwarding generator creates C# source files
2. Scripts compile these into minimal DLLs that forward to Granville assemblies
3. DLLs are packaged into Microsoft.Orleans.* NuGet packages

### 3. Analyzer DLL Handling

**Location:** Various analyzer packaging scripts

**What:** Copies analyzer DLLs into correct package structure

**Structure:**
```
analyzers/dotnet/cs/
├── Granville.Orleans.CodeGenerator.dll
└── Granville.Orleans.Analyzers.dll
```

## Build Scripts and Automation

### Key Build Scripts

1. **build-granville-orleans.ps1**
   - Main build orchestrator for Orleans assemblies
   - Creates compatibility copies
   - Builds in dependency order

2. **pack-granville-orleans-packages.ps1**
   - Packs Granville.Orleans packages
   - Reads version from Directory.Build.props
   - No hardcoded version defaults

3. **package-minimal-shims.ps1**
   - Creates Microsoft.Orleans shim packages with type forwarding
   - Adds -granville-shim suffix to version

4. **fix-granville-dependencies.ps1**
   - Post-pack manipulation of Granville.Orleans packages
   - Fixes analyzer/codegen dependencies
   - Adds -granville-shim suffix to Microsoft.Orleans dependencies

5. **fix-sdk-package.ps1**
   - Post-pack manipulation of SDK package
   - Renames target files from Microsoft.Orleans to Granville.Orleans

6. **fix-rpc-package-dependencies.ps1**
   - Post-pack manipulation of Granville.Rpc packages
   - Ensures Granville.Orleans dependencies

### Version Management

**Version Source:**
- Primary: `Directory.Build.props` contains `<GranvilleRevision>` property
- Scripts read version dynamically - no hardcoded defaults
- Full version format: {VersionPrefix}.{GranvilleRevision}

**Shim Package Versions:**
- Microsoft.Orleans shim packages use version suffix: -granville-shim
- Example: Microsoft.Orleans.Core {version}-granville-shim

**Important:** Always clear NuGet caches when changing versions:
```bash
dotnet nuget locals all --clear
```

## Summary

The Granville Orleans fork maintains compatibility while adding InternalsVisibleTo support for Granville.Rpc. The build system:

1. **Modifies only necessary packages** - Core packages with InternalsVisibleTo modifications
2. **Creates compatibility copies** - Orleans.dll copies during build for tooling compatibility
3. **Performs post-pack manipulations** - Fixes dependencies, renames files, updates metadata
4. **Provides type-forwarding shims** - For compatibility with third-party packages
5. **Uses Granville code generator** - For all Granville-based projects
6. **Supports multiple build scenarios** - From Orleans verification to production Granville apps

The build process is fully automated through PowerShell scripts that handle:
- Dependency ordering
- Compatibility copy creation
- Package manipulation
- Version management
- Shim generation

For production use, always use the provided build scripts rather than manual builds to ensure all manipulations and fixes are applied correctly.