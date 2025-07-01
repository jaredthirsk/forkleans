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
            // Try to load from the current directory
            var assemblyPath = Path.Combine(AppContext.BaseDirectory, $"{granvilleName}.dll");
            if (File.Exists(assemblyPath))
            {
                return context.LoadFromAssemblyPath(assemblyPath);
            }
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

// Test 1: Verify Granville assembly is available
try
{
    Console.WriteLine("Test 1: Checking if Granville.Orleans.Core is available...");
    var granvilleAssembly = Assembly.Load("Granville.Orleans.Core");
    Console.WriteLine($"✓ Found Granville assembly: {granvilleAssembly.FullName}");
    Console.WriteLine($"  Location: {granvilleAssembly.Location}");
}
catch (Exception ex)
{
    Console.WriteLine($"✗ Granville assembly not found: {ex.Message}");
}

// Test 2: Direct type reference via redirect
try
{
    Console.WriteLine("\nTest 2: Loading Microsoft.Orleans.Core (should redirect to Granville)...");
    var orleansAssembly = Assembly.Load("Microsoft.Orleans.Core");
    Console.WriteLine($"✓ Successfully loaded: {orleansAssembly.FullName}");
    
    // Check if we got the Granville assembly
    if (orleansAssembly.FullName?.Contains("Granville") == true)
    {
        Console.WriteLine("✓ Redirect worked! Got Granville assembly when requesting Microsoft!");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✗ Failed to load Orleans assembly: {ex.Message}");
}

// Test 3: UFX SignalR Backplane reference
try
{
    Console.WriteLine("\nTest 3: Checking UFX.Orleans.SignalRBackplane dependencies...");
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

// Test 4: Try to use actual types
try
{
    Console.WriteLine("\nTest 4: Creating types from redirected assembly...");
    
    // This will trigger assembly resolution when UFX tries to use Orleans types
    var orleansAssembly = Assembly.Load("Microsoft.Orleans.Core");
    var grainInterfaceType = orleansAssembly.GetType("Orleans.IGrain");
    
    if (grainInterfaceType != null)
    {
        Console.WriteLine($"✓ Found IGrain interface: {grainInterfaceType.FullName}");
        Console.WriteLine($"  From assembly: {grainInterfaceType.Assembly.FullName}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✗ Failed to use types: {ex.Message}");
}

Console.WriteLine("\n=== Summary ===");
Console.WriteLine("The assembly redirect approach intercepts requests for Microsoft.Orleans.*");
Console.WriteLine("assemblies and redirects them to Granville.Orleans.* assemblies.");
Console.WriteLine("\nThis allows packages like UFX.Orleans.SignalRBackplane to work");
Console.WriteLine("with Granville Orleans without modification.");
Console.WriteLine("\nKey point: When UFX requests 'Microsoft.Orleans.Core', it gets");
Console.WriteLine("'Granville.Orleans.Core' transparently through our redirect handler.");