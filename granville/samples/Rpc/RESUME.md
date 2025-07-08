# Resume Instructions - Implementing Alternative 2, Approach 1

## Current State Summary

### What We Discovered
1. Only 5 Orleans assemblies were modified in the Granville fork (Core, Core.Abstractions, Runtime, Serialization, Serialization.Abstractions)
2. However, many other Orleans packages (Server, Client, etc.) have transitive dependencies on these 5
3. This causes type conflicts when both official and Granville versions are loaded

### Current Solution (Working but Verbose)
- Using shim packages for ALL Orleans assemblies that depend on the modified ones
- This works but requires many more shim packages than the minimal 5

### Desired Solution
- Use MSBuild PackageReference overrides to force transitive dependencies to use shim versions
- Only need shims for the 5 modified assemblies + CodeGenerator
- Use official Microsoft.Orleans packages for everything else

## Steps to Implement Alternative 2, Approach 1

### Step 1: Revert Directory.Packages.props
Change back to minimal shims approach:

```xml
<!-- Only these need shims -->
<PackageVersion Include="Microsoft.Orleans.Core" Version="9.1.2.53-granville-shim" />
<PackageVersion Include="Microsoft.Orleans.Core.Abstractions" Version="9.1.2.53-granville-shim" />
<PackageVersion Include="Microsoft.Orleans.Runtime" Version="9.1.2.53-granville-shim" />
<PackageVersion Include="Microsoft.Orleans.Serialization" Version="9.1.2.53-granville-shim" />
<PackageVersion Include="Microsoft.Orleans.Serialization.Abstractions" Version="9.1.2.53-granville-shim" />
<PackageVersion Include="Microsoft.Orleans.CodeGenerator" Version="9.1.2.53-granville-shim" />

<!-- Everything else uses official -->
<PackageVersion Include="Microsoft.Orleans.Server" Version="9.1.2" />
<PackageVersion Include="Microsoft.Orleans.Client" Version="9.1.2" />
<PackageVersion Include="Microsoft.Orleans.Persistence.Memory" Version="9.1.2" />
<PackageVersion Include="Microsoft.Orleans.Reminders" Version="9.1.2" />
<PackageVersion Include="Microsoft.Orleans.Serialization.SystemTextJson" Version="9.1.2" />
<PackageVersion Include="Microsoft.Orleans.Sdk" Version="9.1.2" />
<PackageVersion Include="Microsoft.Orleans.Analyzers" Version="9.1.2" />
```

### Step 2: Update Project Files
Remove the extra Granville package references that were added:

In `Shooter.Silo.csproj`, remove:
- `<PackageReference Include="Granville.Orleans.Server" />`
- `<PackageReference Include="Granville.Orleans.Persistence.Memory" />`
- `<PackageReference Include="Granville.Orleans.Reminders" />`
- `<PackageReference Include="Granville.Orleans.Serialization.SystemTextJson" />`

Keep only:
- `<PackageReference Include="Granville.Orleans.Core" />`
- `<PackageReference Include="Granville.Orleans.Runtime" />`
- `<PackageReference Include="Granville.Orleans.Serialization" />`

Similar changes for `Shooter.ActionServer.csproj`.

### Step 3: Verify Central Package Management
Ensure the solution is using Central Package Management correctly:
1. Check that `ManagePackageVersionsCentrally` is set to `true` in Directory.Packages.props
2. Verify no version numbers in individual .csproj files

### Step 4: Test the Configuration

```bash
# Clean everything
dotnet clean
dotnet nuget locals all --clear

# Restore packages
dotnet restore

# Check what versions were resolved
dotnet list package --include-transitive | grep Orleans

# Build
dotnet build
```

### Step 5: If Step 4 Fails - Add Directory.Build.targets

If transitive dependencies still resolve to official versions, create `/granville/samples/Rpc/Directory.Build.targets`:

```xml
<Project>
  <ItemGroup>
    <!-- Force transitive references to use shim versions -->
    <PackageReference Update="Microsoft.Orleans.Core" Version="9.1.2.53-granville-shim" />
    <PackageReference Update="Microsoft.Orleans.Core.Abstractions" Version="9.1.2.53-granville-shim" />
    <PackageReference Update="Microsoft.Orleans.Runtime" Version="9.1.2.53-granville-shim" />
    <PackageReference Update="Microsoft.Orleans.Serialization" Version="9.1.2.53-granville-shim" />
    <PackageReference Update="Microsoft.Orleans.Serialization.Abstractions" Version="9.1.2.53-granville-shim" />
  </ItemGroup>
</Project>
```

## Expected Outcome

If successful:
1. `dotnet list package --include-transitive` should show:
   - Shim versions (9.1.2.53-granville-shim) for the 5 core assemblies
   - Official versions (9.1.2) for Server, Client, etc.
2. Build should succeed without type conflicts
3. Fewer shim packages needed overall

## Rollback Plan

If the approach doesn't work:
1. Revert Directory.Packages.props to the current working state (with all shim packages)
2. Re-add the Granville package references to project files
3. The current setup in git history is working and can be restored

## Alternative Next Steps

If Approach 1 doesn't work:
1. Try Alternative 3, Option A: Create shims with standard version numbers (9.1.2 instead of 9.1.2.53-granville-shim)
2. Investigate why CPM isn't overriding transitive dependencies
3. Consider using package source mapping in NuGet.config

## Notes

- The key insight is that MSBuild's Central Package Management should be able to override transitive dependency versions
- If it's not working, it might be due to how Orleans packages specify their dependencies (exact versions vs ranges)
- See `/granville/docs/PACKAGE-OVERRIDE-OPTIONS.md` for all documented approaches