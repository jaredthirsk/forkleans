# Package Override Options for Orleans Shim Dependencies

This document outlines different approaches to handle transitive dependencies when using Orleans shim packages.

## The Problem

When using official Microsoft.Orleans packages (like Server, Client) that depend on the 5 modified assemblies (Core, Runtime, etc.), we need those dependencies to resolve to our shim versions instead of the official versions.

Example:
- Microsoft.Orleans.Server 9.1.2 → depends on → Orleans.Runtime 9.1.2 (official)
- We need it to use → Orleans.Runtime 9.1.2.53-granville-shim instead

## Alternative 2: MSBuild PackageReference Overrides

### Approach 1: Central Package Management Override
Use Directory.Packages.props to specify versions for all packages, including transitive ones.

```xml
<!-- In Directory.Packages.props -->
<!-- Force specific versions for the 5 modified assemblies -->
<PackageVersion Include="Microsoft.Orleans.Core" Version="9.1.2.53-granville-shim" />
<PackageVersion Include="Microsoft.Orleans.Core.Abstractions" Version="9.1.2.53-granville-shim" />
<PackageVersion Include="Microsoft.Orleans.Runtime" Version="9.1.2.53-granville-shim" />
<PackageVersion Include="Microsoft.Orleans.Serialization" Version="9.1.2.53-granville-shim" />
<PackageVersion Include="Microsoft.Orleans.Serialization.Abstractions" Version="9.1.2.53-granville-shim" />
<PackageVersion Include="Microsoft.Orleans.CodeGenerator" Version="9.1.2.53-granville-shim" />

<!-- Use official versions for other packages -->
<PackageVersion Include="Microsoft.Orleans.Server" Version="9.1.2" />
<PackageVersion Include="Microsoft.Orleans.Client" Version="9.1.2" />
```

**Pros:**
- Simple configuration in one place
- Works with Central Package Management
- Clear version specifications

**Cons:**
- May not override transitive dependencies in all cases
- Requires listing all packages explicitly

### Approach 2: Directory.Build.targets Override
Use MSBuild targets to force package versions.

```xml
<!-- In Directory.Build.targets -->
<Project>
  <ItemGroup>
    <!-- Force all references to these packages to use shim versions -->
    <PackageReference Update="Microsoft.Orleans.Core" Version="9.1.2.53-granville-shim" />
    <PackageReference Update="Microsoft.Orleans.Core.Abstractions" Version="9.1.2.53-granville-shim" />
    <PackageReference Update="Microsoft.Orleans.Runtime" Version="9.1.2.53-granville-shim" />
    <PackageReference Update="Microsoft.Orleans.Serialization" Version="9.1.2.53-granville-shim" />
    <PackageReference Update="Microsoft.Orleans.Serialization.Abstractions" Version="9.1.2.53-granville-shim" />
  </ItemGroup>
</Project>
```

**Pros:**
- Forcefully overrides all references
- Works for transitive dependencies
- Can be scoped to specific projects

**Cons:**
- Requires Directory.Build.targets file
- May conflict with other build customizations

### Approach 3: ExcludeAssets with Explicit References
Exclude transitive dependencies and add them explicitly.

```xml
<!-- In .csproj file -->
<ItemGroup>
  <!-- Exclude transitive dependencies -->
  <PackageReference Include="Microsoft.Orleans.Server" Version="9.1.2">
    <ExcludeAssets>dependencies</ExcludeAssets>
  </PackageReference>
  
  <!-- Explicitly add the dependencies we want -->
  <PackageReference Include="Microsoft.Orleans.Runtime" Version="9.1.2.53-granville-shim" />
  <PackageReference Include="Microsoft.Orleans.Core" Version="9.1.2.53-granville-shim" />
  <!-- ... other dependencies ... -->
</ItemGroup>
```

**Pros:**
- Full control over dependencies
- No ambiguity about versions

**Cons:**
- Verbose - must list all dependencies
- Maintenance burden when Orleans updates

## Alternative 3: Modified Shim Design

### Option A: Shims with Standard Version Numbers
Create shim packages with the same version as official packages.

```
Microsoft.Orleans.Runtime 9.1.2 (our shim, not 9.1.2.53-granville-shim)
- Place in local NuGet feed
- Configure feed priority
```

**Implementation:**
1. Build shims with version 9.1.2
2. Host in local feed (e.g., /Artifacts/Release)
3. Configure NuGet.config to check local feed first

**Pros:**
- Seamless replacement
- No version conflicts
- Works with all dependency scenarios

**Cons:**
- Version confusion (which 9.1.2 is it?)
- Requires careful feed management
- Could accidentally use wrong version

### Option B: Repackaged Official Assemblies
Create modified versions of official packages.

```
Microsoft.Orleans.Server 9.1.2 (our package)
├── Orleans.Server.dll (official binary)
└── .nuspec with dependencies pointing to shim versions
```

**Implementation:**
1. Download official nupkg
2. Extract and modify .nuspec dependencies
3. Repack with same version

**Pros:**
- Uses official binaries
- Controls dependency resolution
- Transparent to consumers

**Cons:**
- Repackaging overhead
- Must update when Orleans releases
- Licensing considerations

### Option C: Build-time Assembly Aliasing
Use MSBuild props to redirect assemblies at compile time.

```xml
<!-- In shim package's build/Microsoft.Orleans.Runtime.props -->
<Project>
  <ItemGroup>
    <Reference Remove="Orleans.Runtime" />
    <Reference Include="$(MSBuildThisFileDirectory)../lib/net8.0/Orleans.Runtime.dll">
      <Aliases>global,OrleansRuntimeShim</Aliases>
    </Reference>
  </ItemGroup>
</Project>
```

**Pros:**
- No runtime conflicts
- Build-time resolution
- Can coexist with official packages

**Cons:**
- Complex MSBuild logic
- May not work with all project types
- Requires deep MSBuild knowledge

### Option D: Empty Packages with Dependencies
Create packages with no assemblies, just dependencies.

```
Microsoft.Orleans.Runtime 9.1.2.1 (higher than official)
├── (no assemblies)
└── .nuspec with dependency on Granville.Orleans.Runtime
```

**Pros:**
- Simple approach
- Forces Granville usage
- No assembly conflicts

**Cons:**
- Requires higher version number
- May confuse package resolution
- Breaks if official releases 9.1.2.1

## Recommendation

**For immediate implementation:** Alternative 2, Approach 1 (Central Package Management)
- Simplest to implement
- Easy to understand and maintain
- Can be enhanced with Approach 2 if needed

**For long-term solution:** Alternative 3, Option A (Standard version shims)
- Most seamless experience
- No version suffix confusion
- Works naturally with all tooling

## Testing Strategy

After implementing any approach:
1. Clean all packages: `dotnet clean && dotnet nuget locals all --clear`
2. Restore: `dotnet restore`
3. Check resolved versions: `dotnet list package --include-transitive`
4. Build and verify no type conflicts
5. Run the application to ensure runtime behavior