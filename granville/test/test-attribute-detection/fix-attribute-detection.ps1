#!/usr/bin/env pwsh
# Script to demonstrate the fix for ApplicationPart attribute detection issue

Write-Host "=== ApplicationPart Attribute Detection Fix ===" -ForegroundColor Green
Write-Host ""

Write-Host "PROBLEM:" -ForegroundColor Yellow
Write-Host "The AddGranvilleAssemblies method is checking for ApplicationPartAttribute"
Write-Host "but Granville.Orleans assemblies have FrameworkPartAttribute instead."
Write-Host ""

Write-Host "ROOT CAUSE:" -ForegroundColor Yellow  
Write-Host "- Orleans marks its framework assemblies with [FrameworkPart] attribute"
Write-Host "- The /src/Directory.Build.props file adds this attribute to all /src/ projects"
Write-Host "- ApplicationPartAttribute is for user assemblies, not framework assemblies"
Write-Host ""

Write-Host "SOLUTION:" -ForegroundColor Green
Write-Host "The AddGranvilleAssemblies method should add ALL Granville.Orleans assemblies"
Write-Host "regardless of attributes, because:"
Write-Host "1. They are framework assemblies (marked with FrameworkPart)"
Write-Host "2. The serializer needs to know about types in these assemblies"
Write-Host "3. The attribute check was incorrectly filtering them out"
Write-Host ""

Write-Host "RECOMMENDED FIX:" -ForegroundColor Cyan
Write-Host @'
public static ISerializerBuilder AddGranvilleAssemblies(this ISerializerBuilder serializerBuilder)
{
    Console.WriteLine("================== AddGranvilleAssemblies START ==================");
    
    var loadedAssemblies = new List<Assembly>();
    var failedAssemblies = new List<(string name, string error)>();
    
    // List of all Granville.Orleans assemblies we want to load
    var granvilleAssemblyNames = new[]
    {
        "Granville.Orleans.Serialization",
        "Granville.Orleans.Serialization.Abstractions",
        "Granville.Orleans.Core",
        "Granville.Orleans.Core.Abstractions",
        "Granville.Orleans.Runtime",
        "Granville.Orleans.Client",
        "Granville.Orleans.Server",
        "Granville.Orleans.Persistence.Memory",
        "Granville.Orleans.Reminders"
    };
    
    // Try to load each assembly
    foreach (var assemblyName in granvilleAssemblyNames)
    {
        try
        {
            var assembly = Assembly.Load(assemblyName);
            loadedAssemblies.Add(assembly);
            Console.WriteLine($"✓ Loaded: {assembly.FullName}");
            
            // REMOVED: Incorrect attribute check that was filtering out framework assemblies
            // These are framework assemblies marked with [FrameworkPart], not [ApplicationPart]
            // We need to add them ALL to the serializer
            
            // Add to serializer
            serializerBuilder.AddAssembly(assembly);
            Console.WriteLine($"  ✓ Added to serializer");
        }
        catch (FileNotFoundException)
        {
            // This is expected for some assemblies that may not be used
            failedAssemblies.Add((assemblyName, "Not found (may not be needed)"));
            Console.WriteLine($"- Skipped: {assemblyName} (not found, may not be needed)");
        }
        catch (Exception ex)
        {
            failedAssemblies.Add((assemblyName, ex.Message));
            Console.WriteLine($"✗ Failed: {assemblyName} - {ex.Message}");
        }
    }
    
    Console.WriteLine($"\n--- Summary ---");
    Console.WriteLine($"Successfully loaded: {loadedAssemblies.Count} assemblies");
    Console.WriteLine($"Failed to load: {failedAssemblies.Count} assemblies");
    
    Console.WriteLine("================== AddGranvilleAssemblies END ==================\n");
    return serializerBuilder;
}
'@

Write-Host ""
Write-Host "ADDITIONAL NOTES:" -ForegroundColor Magenta
Write-Host "- The 'external tools' might be showing assembly references, not attributes"
Write-Host "- FrameworkPartAttribute is in Orleans.Metadata namespace, not Orleans namespace"
Write-Host "- This is consistent with how Orleans marks its own framework assemblies"