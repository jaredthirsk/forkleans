# Resolving Code Generator Conflicts

When using both Microsoft.Orleans and Granville packages in the same project, you may encounter duplicate code generation errors. This document describes several options for resolving these conflicts.

## The Problem

Both Microsoft.Orleans and Granville packages include source generators that create code for grain interfaces and serialization. When both are present in the same project, they generate duplicate types, causing compilation errors like:

```
error CS0101: The namespace 'OrleansCodeGen.ShooterShared' already contains a definition for 'IPlayerGrain_OrleansCodeGenSerializer'
```

## Solution Options

### Option 1: Use Only One Package Set (Recommended)

The simplest solution is to use either Microsoft.Orleans OR Granville.Orleans packages exclusively within a project.

#### Using Microsoft.Orleans with Excluded Analyzers

If you need to use Microsoft.Orleans packages but want Granville's code generation:

```xml
<ItemGroup>
  <!-- Microsoft.Orleans packages from nuget.org -->
  <PackageReference Include="Microsoft.Orleans.Core.Abstractions" />
  <PackageReference Include="Microsoft.Orleans.Serialization" />
  <PackageReference Include="Microsoft.Orleans.Serialization.Abstractions" />
  <!-- Exclude Orleans code generators to avoid conflicts -->
  <PackageReference Include="Microsoft.Orleans.CodeGenerator" ExcludeAssets="analyzers" />
  <PackageReference Include="Microsoft.Orleans.Sdk" ExcludeAssets="analyzers" />
  
  <!-- Granville RPC packages (include code generator from here) -->
  <PackageReference Include="Granville.Rpc.Sdk" />
  <PackageReference Include="Granville.Rpc.Abstractions" />
</ItemGroup>
```

#### Using Granville.Orleans Exclusively

For full Granville functionality without conflicts:

```xml
<ItemGroup>
  <!-- Use Granville Orleans packages exclusively -->
  <PackageReference Include="Granville.Orleans.Core.Abstractions" />
  <PackageReference Include="Granville.Orleans.Serialization" />
  <PackageReference Include="Granville.Orleans.Serialization.Abstractions" />
  <PackageReference Include="Granville.Orleans.CodeGenerator" />
  <PackageReference Include="Granville.Orleans.Sdk" />
  
  <!-- Granville RPC packages -->
  <PackageReference Include="Granville.Rpc.Sdk" />
  <PackageReference Include="Granville.Rpc.Abstractions" />
</ItemGroup>
```

### Option 2: MSBuild Property Control

Create a `Directory.Build.targets` file to control code generator inclusion via MSBuild properties:

```xml
<Project>
  <!-- Control code generator inclusion via MSBuild properties -->
  <PropertyGroup>
    <!-- Set to true to disable Microsoft.Orleans code generators -->
    <DisableMicrosoftOrleansCodeGen Condition="'$(DisableMicrosoftOrleansCodeGen)' == ''">false</DisableMicrosoftOrleansCodeGen>
    
    <!-- Set to true to disable Granville code generators -->
    <DisableGranvilleCodeGen Condition="'$(DisableGranvilleCodeGen)' == ''">false</DisableGranvilleCodeGen>
  </PropertyGroup>

  <ItemGroup Condition="'$(DisableMicrosoftOrleansCodeGen)' == 'true'">
    <!-- Remove Microsoft.Orleans code generators from compilation -->
    <PackageReference Update="Microsoft.Orleans.CodeGenerator" ExcludeAssets="analyzers" />
    <PackageReference Update="Microsoft.Orleans.Sdk" ExcludeAssets="analyzers" />
  </ItemGroup>

  <ItemGroup Condition="'$(DisableGranvilleCodeGen)' == 'true'">
    <!-- Remove Granville code generators from compilation -->
    <PackageReference Update="Granville.Orleans.CodeGenerator" ExcludeAssets="analyzers" />
    <PackageReference Update="Granville.Orleans.Sdk" ExcludeAssets="analyzers" />
    <PackageReference Update="Granville.Rpc.Sdk" ExcludeAssets="analyzers" />
  </ItemGroup>
</Project>
```

Then in your project file or command line:

```bash
# Build with only Granville code generators
dotnet build -p:DisableMicrosoftOrleansCodeGen=true

# Build with only Microsoft code generators
dotnet build -p:DisableGranvilleCodeGen=true
```

### Option 3: Separate Project Files

Create alternate project files for different scenarios. For example:

**Shooter.Shared.csproj** (uses Microsoft.Orleans):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Orleans.Sdk" />
  </ItemGroup>
</Project>
```

**Shooter.Shared.Granville.csproj** (uses Granville.Orleans):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Granville.Orleans.Sdk" />
    <PackageReference Include="Granville.Rpc.Sdk" />
  </ItemGroup>
</Project>
```

Then reference the appropriate project file in your solution.

### Option 4: Build Configuration Based Selection

Create a `Directory.Build.props` file to switch between implementations:

```xml
<Project>
  <!-- Define which Orleans implementation to use -->
  <!-- Values: Microsoft, Granville, or Hybrid -->
  <PropertyGroup>
    <OrleansImplementation Condition="'$(OrleansImplementation)' == ''">Granville</OrleansImplementation>
  </PropertyGroup>

  <!-- Microsoft Orleans configuration -->
  <ItemGroup Condition="'$(OrleansImplementation)' == 'Microsoft'">
    <GlobalPackageReference Include="Microsoft.Orleans.Core.Abstractions" />
    <GlobalPackageReference Include="Microsoft.Orleans.Sdk" />
    <GlobalPackageReference Include="Microsoft.Orleans.CodeGenerator" />
  </ItemGroup>

  <!-- Granville Orleans configuration -->
  <ItemGroup Condition="'$(OrleansImplementation)' == 'Granville'">
    <GlobalPackageReference Include="Granville.Orleans.Core.Abstractions" />
    <GlobalPackageReference Include="Granville.Orleans.Sdk" />
    <GlobalPackageReference Include="Granville.Orleans.CodeGenerator" />
  </ItemGroup>

  <!-- Hybrid configuration (requires assembly redirects) -->
  <ItemGroup Condition="'$(OrleansImplementation)' == 'Hybrid'">
    <!-- Use Microsoft packages but exclude code generators -->
    <GlobalPackageReference Include="Microsoft.Orleans.Core.Abstractions" />
    <GlobalPackageReference Include="Microsoft.Orleans.Sdk" ExcludeAssets="analyzers" />
    <!-- Use Granville code generator only -->
    <GlobalPackageReference Include="Granville.Orleans.CodeGenerator" />
  </ItemGroup>
</Project>
```

Use different configurations:

```bash
# Build with Granville implementation
dotnet build -p:OrleansImplementation=Granville

# Build with Microsoft implementation  
dotnet build -p:OrleansImplementation=Microsoft

# Build with Hybrid approach
dotnet build -p:OrleansImplementation=Hybrid
```

## Troubleshooting

### Verifying Active Code Generators

To see which code generators are active in your build:

```bash
dotnet build -v:d | grep -i "analyzer"
```

### Clean Build

Always perform a clean build when switching between configurations:

```bash
# Clean all build artifacts
find . -type d -name bin -o -name obj | xargs rm -rf

# Or using dotnet
dotnet clean
```

### Assembly Redirect Approach

If using the hybrid approach with assembly redirects, ensure your application startup includes the redirect logic:

```csharp
AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
{
    if (assemblyName.Name?.StartsWith("Microsoft.Orleans") == true)
    {
        var granvilleName = assemblyName.Name.Replace("Microsoft.Orleans", "Granville.Orleans");
        try
        {
            return context.LoadFromAssemblyName(new AssemblyName(granvilleName));
        }
        catch { }
    }
    return null;
};
```

## Recommendations

1. **For new projects**: Use Granville.Orleans packages exclusively (Option 1)
2. **For existing Microsoft.Orleans projects**: Use the MSBuild property approach (Option 2)
3. **For complex scenarios**: Use build configuration based selection (Option 4)
4. **For testing different approaches**: Use separate project files (Option 3)

## Related Documentation

- [Assembly Redirect Guide](/granville/compatibility-tools/ASSEMBLY-REDIRECT-GUIDE.md)
- [Repository Organization](/granville/REPO-ORGANIZATION.md)
- [Shooter Sample Guide](/granville/samples/Rpc/CLAUDE.md)