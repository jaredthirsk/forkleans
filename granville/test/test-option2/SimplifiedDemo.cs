using System;
using System.Reflection;
using System.Runtime.Loader;

Console.WriteLine("=== Assembly Redirect Demonstration ===\n");

// Set up the assembly redirect handler
AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
{
    Console.WriteLine($"[REDIRECT] Intercepted request for: {assemblyName.Name}");
    
    if (assemblyName.Name?.StartsWith("Microsoft.Orleans") == true)
    {
        var granvilleName = assemblyName.Name.Replace("Microsoft.Orleans", "Granville.Orleans");
        Console.WriteLine($"[REDIRECT] → Redirecting to: {granvilleName}");
        
        // In a real scenario, this would load the actual Granville assembly
        // For this demo, we just show that the redirect is working
        Console.WriteLine($"[REDIRECT] ✓ Redirect handler would load {granvilleName}.dll here");
        
        // Return null to show the concept (in reality, would return loaded assembly)
        return null;
    }
    return null;
};

Console.WriteLine("Scenario: UFX.Orleans.SignalRBackplane tries to use Orleans types\n");

// Simulate what happens when UFX package tries to load Orleans
try
{
    Console.WriteLine("1. UFX.Orleans.SignalRBackplane references Orleans types...");
    var ufx = Assembly.Load("UFX.Orleans.SignalRBackplane");
    Console.WriteLine($"   ✓ Loaded: {ufx.GetName().Name} v{ufx.GetName().Version}");
    
    Console.WriteLine("\n2. UFX's dependencies that would trigger redirects:");
    foreach (var reference in ufx.GetReferencedAssemblies())
    {
        if (reference.Name.Contains("Orleans"))
        {
            Console.WriteLine($"   - {reference.Name} v{reference.Version}");
        }
    }
    
    Console.WriteLine("\n3. When UFX tries to use Orleans.IGrain:");
    Console.WriteLine("   (This would trigger our redirect handler)");
    
    // Note: Actually triggering the redirect would require using types from UFX
    // which would then try to load Orleans assemblies
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}

Console.WriteLine("\n=== How It Works ===");
Console.WriteLine("1. UFX.Orleans.SignalRBackplane is compiled against Microsoft.Orleans.*");
Console.WriteLine("2. At runtime, when it tries to load those assemblies, our handler intercepts");
Console.WriteLine("3. The handler redirects to load Granville.Orleans.* instead");
Console.WriteLine("4. UFX continues working, unaware it's using Granville instead of Microsoft");

Console.WriteLine("\n=== Key Benefits ===");
Console.WriteLine("• No need to modify third-party packages");
Console.WriteLine("• Works transparently at runtime");
Console.WriteLine("• Can be toggled on/off easily");
Console.WriteLine("• Avoids legal issues with Microsoft naming");