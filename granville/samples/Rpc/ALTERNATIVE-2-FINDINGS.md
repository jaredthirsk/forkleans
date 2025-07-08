# Alternative 2 Implementation Findings

## Summary
Alternative 2 (MSBuild PackageReference Overrides) did not work as expected. Central Package Management does not override transitive dependencies brought in by official Microsoft.Orleans packages.

## What We Tried

### Attempt 1: Central Package Management in Directory.Packages.props
- Updated Directory.Packages.props to specify shim versions only for the 5 modified assemblies
- Used official versions for Server, Client, etc.
- **Result**: Transitive dependencies still resolved to official versions

### Attempt 2: Directory.Build.targets with Update
```xml
<PackageReference Update="Microsoft.Orleans.Runtime" Version="9.1.2.53-granville-shim" />
```
- **Result**: Error - CPM doesn't allow version in Update

### Attempt 3: Directory.Build.targets with Property Override
```xml
<PackageVersion_Microsoft_Orleans_Runtime>9.1.2.53-granville-shim</PackageVersion_Microsoft_Orleans_Runtime>
```
- **Result**: No effect - transitive dependencies still resolved to official versions

## Root Cause
When Microsoft.Orleans.Server (9.1.2) has a dependency on Orleans.Runtime (>= 9.1.2):
- NuGet resolves this to Orleans.Runtime 9.1.2 from nuget.org
- Our shim is versioned 9.1.2.53-granville-shim, which doesn't satisfy ">= 9.1.2" according to NuGet's version comparison
- Central Package Management doesn't override transitive dependencies that come from external packages

## Evidence
Running `dotnet list package --include-transitive` showed:
```
Microsoft.Orleans.Runtime                    9.1.2
```
Instead of the expected:
```
Microsoft.Orleans.Runtime                    9.1.2.53-granville-shim
```

## Conclusion
Alternative 2 doesn't work because:
1. NuGet's dependency resolution happens before MSBuild property overrides
2. CPM only controls direct package references, not transitive ones from external packages
3. The version suffix "-granville-shim" makes our packages appear as pre-release versions

## Next Steps
Consider Alternative 3 options:
1. Create shims with exact version match (9.1.2 instead of 9.1.2.53-granville-shim)
2. Use the cascading shim approach (what we had working before)
3. Explore assembly binding redirects at runtime
4. Consider creating a custom NuGet resolver