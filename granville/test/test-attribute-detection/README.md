# ApplicationPart Attribute Detection Investigation

## Summary

The investigation revealed that the ApplicationPart attribute detection was failing because:

1. **Wrong Attribute**: Granville.Orleans assemblies are marked with `[FrameworkPart]` attribute, not `[ApplicationPart]` attribute
2. **By Design**: Orleans framework assemblies under `/src/` are automatically marked with `FrameworkPartAttribute` via `/src/Directory.Build.props`
3. **Namespace**: The FrameworkPartAttribute is in the `Orleans.Metadata` namespace, not the `Orleans` namespace

## Test Results

Running the test program shows:
- Granville.Orleans.Serialization.dll has `Orleans.Metadata.FrameworkPartAttribute`
- It does NOT have `Orleans.ApplicationPartAttribute`
- The ApplicationPartAttribute type exists in Granville.Orleans.Serialization.Abstractions

## Solution

The `AddGranvilleAssemblies` method should add ALL Granville.Orleans assemblies to the serializer without checking for ApplicationPartAttribute, because:

1. These are framework assemblies (correctly marked with FrameworkPart)
2. The serializer needs to know about types in these assemblies
3. The attribute check was incorrectly filtering them out

## Fixed Code

Remove the attribute check and unconditionally add all successfully loaded Granville.Orleans assemblies:

```csharp
// Add to serializer (removed incorrect attribute check)
serializerBuilder.AddAssembly(assembly);
Console.WriteLine($"  ✓ Added to serializer");
```

## Running the Test

```bash
cd /mnt/g/forks/orleans/granville/test/test-attribute-detection
dotnet-win build
dotnet-win run
```

This will show all attributes on the assembly and demonstrate the various detection methods.