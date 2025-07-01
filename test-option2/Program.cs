using System.Reflection;
using System.Runtime.Loader;

// Assembly redirect for Granville Orleans compatibility (Option 2)
// This demonstrates how to make UFX.Orleans.SignalRBackplane work with Granville Orleans
AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
{
    Console.WriteLine($"Assembly requested: {assemblyName.FullName}");
    
    if (assemblyName.Name?.StartsWith("Microsoft.Orleans") == true)
    {
        var granvilleName = assemblyName.Name.Replace("Microsoft.Orleans", "Granville.Orleans");
        try
        {
            Console.WriteLine($"Attempting to redirect {assemblyName.Name} to {granvilleName}");
            
            // For this demo, we'll simulate loading Granville assemblies
            // In a real scenario, these would be loaded from disk or NuGet
            return context.LoadFromAssemblyName(new AssemblyName(granvilleName));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to redirect {assemblyName.Name}: {ex.Message}");
        }
    }
    return null;
};

Console.WriteLine("=== Testing Assembly Redirect Approach (Option 2) ===");
Console.WriteLine();

// Test 1: Direct type reference
try
{
    Console.WriteLine("Test 1: Attempting to load Orleans types...");
    var orleansAssembly = Assembly.Load("Microsoft.Orleans.Core");
    Console.WriteLine($"✓ Loaded assembly: {orleansAssembly.FullName}");
}
catch (Exception ex)
{
    Console.WriteLine($"✗ Failed to load Orleans assembly: {ex.Message}");
}

// Test 2: UFX SignalR Backplane reference
try
{
    Console.WriteLine("\nTest 2: Checking UFX.Orleans.SignalRBackplane dependencies...");
    var ufxAssembly = Assembly.Load("UFX.Orleans.SignalRBackplane");
    Console.WriteLine($"✓ Loaded UFX assembly: {ufxAssembly.FullName}");
    
    // Get referenced assemblies
    var refs = ufxAssembly.GetReferencedAssemblies();
    foreach (var refAssembly in refs.Where(r => r.Name?.Contains("Orleans") == true))
    {
        Console.WriteLine($"  - UFX references: {refAssembly.FullName}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✗ Failed to load UFX assembly: {ex.Message}");
}

Console.WriteLine("\n=== Summary ===");
Console.WriteLine("The assembly redirect approach intercepts requests for Microsoft.Orleans.*");
Console.WriteLine("assemblies and redirects them to Granville.Orleans.* assemblies.");
Console.WriteLine("\nThis allows packages like UFX.Orleans.SignalRBackplane to work");
Console.WriteLine("with Granville Orleans without modification.");