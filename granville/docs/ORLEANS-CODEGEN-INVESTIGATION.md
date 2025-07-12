# Orleans Code Generation Investigation Report

## Summary

The investigation confirms that the Orleans-generated code **IS** being properly compiled into the final Granville.Orleans assemblies. The initial concern that generated files were not being included in compilation was incorrect.

## Key Findings

### 1. Generated Files Location
The Orleans.CodeGenerator creates source files in the standard Roslyn source generator location:
- `/obj/Release/net8.0/generated/Orleans.CodeGenerator/Orleans.CodeGenerator.OrleansSerializationSourceGenerator/Granville.Orleans.Serialization.orleans.g.cs`
- `/obj/Release/netstandard2.1/generated/Orleans.CodeGenerator/Orleans.CodeGenerator.OrleansSerializationSourceGenerator/Granville.Orleans.Serialization.orleans.g.cs`

### 2. Assembly Renaming Works Correctly
The generated code properly uses the renamed assembly name:
- Assembly attribute: `[assembly: global::Orleans.ApplicationPartAttribute("Granville.Orleans.Serialization")]`
- TypeManifestProvider references: `OrleansCodeGen.GranvilleOrleansSerialization.Metadata_GranvilleOrleansSerialization`

### 3. Generated Code Compilation Verified
Using ildasm to inspect the compiled assembly confirms:
- All generated codec classes (e.g., `Codec_ArrayListSurrogate`) are present
- The `TypeManifestProviderAttribute` correctly references the generated metadata class
- The metadata class `Metadata_GranvilleOrleansSerialization` is included
- All type references properly use `Granville.Orleans.*` assembly names

### 4. MSBuild Integration
The MSBuild integration works correctly:
1. `Directory.Build.targets` imports the Orleans CodeGenerator props and targets when `OrleansBuildTimeCodeGen=true`
2. The `Granville_FinalAssemblyName` property is passed to the code generator
3. The source generator runs during compilation and adds files to the Compile ItemGroup automatically
4. No manual intervention is needed to include generated files

## How Orleans CodeGenerator Integration Works

1. **Source Generator Registration**: Orleans.CodeGenerator is added as an analyzer via ProjectReference with `OutputItemType="Analyzer"`
2. **Property Passing**: MSBuild properties are made visible to the generator via `CompilerVisibleProperty` items
3. **Assembly Name Handling**: The `Granville_FinalAssemblyName` property ensures the generator knows the final assembly name
4. **Automatic Compilation**: Roslyn automatically includes source generator output in the compilation

## Conclusion

The Orleans code generation and compilation process is working correctly with the Granville fork's assembly renaming. No changes are needed to the build process for including generated files.

The assembly renaming in `Directory.Build.targets` does not interfere with source generation because:
1. Assembly renaming happens early in the build via PropertyGroup settings
2. The code generator receives the final assembly name via `Granville_FinalAssemblyName`
3. Generated files are automatically included by Roslyn's source generator infrastructure

## Additional Findings (July 2025)

### Duplicate Code Generation Issue
When using Orleans 9.1.2, projects that reference both Granville.Orleans and Microsoft.Orleans packages can experience duplicate code generation:
- Both package sets include code generators that produce identical type definitions
- This causes CS0101 compilation errors (duplicate type definitions)
- **Solution**: Add `<Orleans_DesignTimeBuild>true</Orleans_DesignTimeBuild>` to project files to disable Orleans code generation

### ApplicationPart Generation Fix
The Orleans code generator was modified to correctly generate ApplicationPart attributes for Granville assemblies:
- Added `Granville_FinalAssemblyName` CompilerVisibleProperty to pass the renamed assembly name
- Modified `OrleansSourceGenerator` to use this property when generating ApplicationPart attributes
- All Granville.Orleans assemblies now have correct ApplicationPart attributes

### Current Status
- Code generation works correctly with assembly renaming
- ApplicationPart attributes are properly generated
- Duplicate code generation can be prevented with Orleans_DesignTimeBuild property
- The Shooter sample successfully builds and runs with these fixes