# Fork Maintenance Issues and Solutions

This document captures issues discovered during fork maintenance and their solutions.

## Issues Found After Namespace Conversion

### 1. Orleans-Prefixed Types Incorrectly Converted

**Problem**: The namespace conversion was too aggressive and converted type names like `OrleansException` to `ForkleansException`.

**Types affected**:
- `OrleansException` → `ForkleansException` 
- `OrleansConfigurationException` → `ForkleansConfigurationException`
- `OrleansTransactionAbortedException` → `ForkleansTransactionAbortedException`
- `OrleansJsonSerializer` → `ForkleansJsonSerializer`
- `OrleansJsonSerializerOptions` → `ForkleansJsonSerializerOptions`
- `OrleansGrainStorageSerializer` → `ForkleansGrainStorageSerializer`

**Solution**: These type names should be preserved as-is. Only namespace declarations and using statements should change.

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

**Solution**: Project reference paths should remain unchanged. Only namespaces in code should change.

### 4. SDK Build File Names

**Problem**: Microsoft.Orleans.Sdk.targets was renamed to Microsoft.Forkleans.Sdk.targets, but Directory.Build.targets still looked for the Orleans version.

**Solution**: These build infrastructure files should keep their original names.

### 5. Build Property Names

**Problem**: Some build properties like `OrleansBuildTimeCodeGen` were being converted to `ForkleansBuildTimeCodeGen`.

**Solution**: MSBuild property names should remain unchanged.

## Recommended Script Improvements

### 1. Update Fix-Fork.ps1

Add these steps after namespace conversion:
1. Fix Orleans-prefixed type names
2. Ensure analyzer references are correct
3. Fix any project reference paths that were incorrectly changed

### 2. Improve Convert-OrleansNamespace.ps1

The script should:
1. Only convert namespace declarations and using statements
2. Preserve Orleans-prefixed type names
3. Not modify project reference paths
4. Not modify MSBuild property names
5. Not modify build file names

### 3. Add Validation Step

After running Fix-Fork.ps1, add a validation that:
1. Runs `dotnet restore`
2. Runs `dotnet build` 
3. Reports any errors
4. Suggests manual fixes if needed

## Testing Recommendations

1. **Before merging upstream**: Create a test branch and run the scripts
2. **Common test scenarios**:
   - Build the solution
   - Run a few unit tests
   - Check that analyzer/code generation still works
   - Verify package names are correct

## Manual Review Checklist

After running Fix-Fork.ps1, manually review:
- [ ] Directory.Build.targets has correct analyzer references
- [ ] Orleans-prefixed exception types are preserved
- [ ] Project builds successfully
- [ ] No Windows-specific paths in project.assets.json files