using System;
using System.Linq;
using System.Reflection;
using System.IO;

class TestAssemblyMetadata
{
    static void Main4(string[] args)
    {
        Console.WriteLine("=== Assembly Metadata Test ===\n");

        // Load the assembly
        var assemblyPath = "bin/Debug/net8.0/Granville.Orleans.Serialization.dll";
        if (File.Exists(assemblyPath))
        {
            Console.WriteLine($"Loading from: {assemblyPath}");
            var fileInfo = new FileInfo(assemblyPath);
            Console.WriteLine($"File size: {fileInfo.Length} bytes");
            Console.WriteLine($"Last modified: {fileInfo.LastWriteTime}");
            
            var assembly = Assembly.LoadFrom(assemblyPath);
            Console.WriteLine($"Assembly loaded: {assembly.FullName}");
            
            // Get all types in the assembly
            var types = assembly.GetTypes();
            Console.WriteLine($"\nTotal types in assembly: {types.Length}");
            
            // Look for OrleansCodeGen namespace
            var orleansCodeGenTypes = types.Where(t => t.Namespace != null && t.Namespace.StartsWith("OrleansCodeGen")).ToList();
            Console.WriteLine($"\nTypes in OrleansCodeGen namespace: {orleansCodeGenTypes.Count}");
            
            if (orleansCodeGenTypes.Count > 0)
            {
                Console.WriteLine("\nOrleansCodeGen types:");
                foreach (var type in orleansCodeGenTypes.Take(10))
                {
                    Console.WriteLine($"  - {type.FullName}");
                }
                
                // Look for metadata provider
                var metadataProvider = orleansCodeGenTypes.FirstOrDefault(t => t.Name.Contains("Metadata"));
                if (metadataProvider != null)
                {
                    Console.WriteLine($"\nFound metadata provider: {metadataProvider.FullName}");
                    
                    // Check if it implements IConfigureOptions<TypeManifestOptions>
                    var interfaces = metadataProvider.GetInterfaces();
                    Console.WriteLine($"Interfaces implemented: {interfaces.Length}");
                    foreach (var iface in interfaces)
                    {
                        Console.WriteLine($"  - {iface.FullName}");
                    }
                }
            }
            else
            {
                Console.WriteLine("\nNo OrleansCodeGen types found!");
                
                // List all namespaces
                var namespaces = types.Select(t => t.Namespace).Where(n => n != null).Distinct().OrderBy(n => n).ToList();
                Console.WriteLine($"\nAll namespaces ({namespaces.Count}):");
                foreach (var ns in namespaces.Take(20))
                {
                    Console.WriteLine($"  - {ns}");
                }
            }
            
            // Check assembly attributes again
            Console.WriteLine("\n=== Assembly Attributes ===");
            var attrs = assembly.GetCustomAttributesData();
            Console.WriteLine($"Total attributes: {attrs.Count}");
            
            foreach (var attr in attrs)
            {
                Console.WriteLine($"  [{attr.AttributeType.Name}]");
                if (attr.AttributeType.Name == "TypeManifestProviderAttribute")
                {
                    Console.WriteLine($"    -> Constructor args: {string.Join(", ", attr.ConstructorArguments.Select(a => a.Value))}");
                }
            }
        }
        else
        {
            Console.WriteLine($"File not found: {assemblyPath}");
        }
    }
}