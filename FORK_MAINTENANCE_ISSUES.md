# Fork Maintenance Issues and Solutions

This document captures issues discovered during fork maintenance and their solutions.

## Design Decision: Complete Type Renaming

After initial implementation, we decided to rename ALL Orleans-prefixed types to Forkleans-prefixed for consistency. This means:
- `OrleansException` → `ForkleansException`
- `OrleansJsonSerializer` → `ForkleansJsonSerializer`
- etc.

**Rationale**: 
- **Consistency**: Everything in the Forkleans namespace should be Forkleans-prefixed
- **Clarity**: No confusion about which fork you're using
- **Search/Replace**: Easier to find all Forkleans-specific code
- **Branding**: Clear that this is a fork, not original Orleans

## Issues Found and Fixed

### 1. Namespace Conversion Scope

**Problem**: The initial namespace conversion was both too aggressive and not aggressive enough in different areas.

**Solution**: The converter now:
- Converts all namespaces from Orleans to Forkleans
- Converts all Orleans-prefixed type names to Forkleans-prefixed
- Preserves file names (Orleans.*.csproj remains unchanged)
- Preserves MSBuild property names (OrleansBuildTimeCodeGen remains unchanged)

### 2. Analyzer Project References

**Problem**: Orleans.Analyzers and Orleans.CodeGenerator projects target netstandard2.0 but were being referenced as net8.0, causing build errors.

**Error**: 
```
error NETSDK1005: Assets file doesn't have a target for 'net8.0'
```

**Solution**: The Directory.Build.targets file needs proper analyzer references:
```xml
<ProjectReference 
  Include="$(MSBuildThisFileDirectory)src/Orleans.CodeGenerator/Orleans.CodeGenerator.csproj"
  OutputItemType="Analyzer"
  ReferenceOutputAssembly="false"
  PrivateAssets="All" />
```

### 3. Project Reference Paths

**Problem**: The namespace converter changed `Orleans.` in project reference paths, breaking references:
```xml
<!-- Before (correct) -->
<ProjectReference Include="..\..\src\Orleans.Core\Orleans.Core.csproj" />

<!-- After (incorrect) -->
<ProjectReference Include="..\..\src\Forkleans.Core\Forkleans.Core.csproj" />
```

**Solution**: Project reference paths remain unchanged. Only namespaces and type names in code change.

### 4. SDK Build File Names

**Problem**: Microsoft.Orleans.Sdk.targets was renamed to Microsoft.Forkleans.Sdk.targets, but Directory.Build.targets still looked for the Orleans version.

**Solution**: These build infrastructure files keep their original names.

### 5. Build Property Names

**Problem**: Some build properties like `OrleansBuildTimeCodeGen` were being converted to `ForkleansBuildTimeCodeGen`.

**Solution**: MSBuild property names remain unchanged.

## What Gets Converted

### ✅ CONVERT These:
1. **Namespace declarations**: `namespace Orleans` → `namespace Forkleans`
2. **Using statements**: `using Orleans` → `using Forkleans`
3. **Type names**: `OrleansException` → `ForkleansException`
4. **Interface names**: `IOrleansSerializer` → `IForkleansSerializer`
5. **Qualified names**: `Orleans.Runtime.Foo` → `Forkleans.Runtime.Foo`
6. **Assembly names**: Output DLLs use Forkleans prefix
7. **Package names**: NuGet packages use Forkleans prefix

### ❌ DO NOT Convert These:
1. **File names**: `Orleans.Core.csproj` remains unchanged
2. **Project references**: `<ProjectReference Include="Orleans.Core.csproj">`
3. **MSBuild properties**: `OrleansBuildTimeCodeGen`
4. **Build file names**: `Microsoft.Orleans.Sdk.targets`
5. **Package references**: `Microsoft.Orleans.*` NuGet packages

## Testing Recommendations

1. **Before merging upstream**: Create a test branch and run the scripts
2. **Common test scenarios**:
   - Build the solution
   - Run a few unit tests
   - Check that analyzer/code generation still works
   - Verify package names are correct
   - Ensure all Orleans types are now Forkleans types

## Manual Review Checklist

After running Fix-Fork.ps1, manually review:
- [ ] Directory.Build.targets has correct analyzer references
- [ ] All Orleans-prefixed types are now Forkleans-prefixed
- [ ] Project builds successfully
- [ ] No Windows-specific paths in project.assets.json files
- [ ] Test that exceptions can be caught with new names