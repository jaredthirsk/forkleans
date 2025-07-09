using System;
using System.Linq;
using System.Reflection;
using Orleans;
using Orleans.Runtime;

namespace CodeGenDiscovery
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== Orleans CodeGen Type Discovery ===");
            
            // Use various Orleans types to trigger code generation
            // These are commonly used types that would trigger serializer generation
            _ = typeof(GrainId);
            _ = typeof(SiloAddress);
            _ = typeof(ActivationId);
            _ = typeof(GrainReference);
            _ = typeof(UniqueKey);
            _ = typeof(GrainType);
            _ = typeof(GrainInterfaceType);
            _ = typeof(IdSpan);
            _ = typeof(MembershipVersion);
            
            // Now scan the current assembly for generated types
            var assembly = Assembly.GetExecutingAssembly();
            var orleansCodeGenTypes = assembly.GetTypes()
                .Where(t => t.Namespace?.StartsWith("OrleansCodeGen.Orleans") == true)
                .OrderBy(t => t.FullName)
                .ToList();

            Console.WriteLine($"\nFound {orleansCodeGenTypes.Count} OrleansCodeGen types:");
            Console.WriteLine(new string('-', 80));

            foreach (var type in orleansCodeGenTypes)
            {
                Console.WriteLine($"Type: {type.FullName}");
                Console.WriteLine($"  Namespace: {type.Namespace}");
                Console.WriteLine($"  Name: {type.Name}");
                Console.WriteLine($"  Assembly: {type.Assembly.GetName().Name}");
                
                // Identify the pattern
                var pattern = type.Name switch
                {
                    string n when n.StartsWith("Codec_") => "Serialization Codec",
                    string n when n.StartsWith("Copier_") => "Deep Copier",
                    string n when n.StartsWith("Activator_") => "Type Activator",
                    string n when n.StartsWith("Proxy_") => "Grain Proxy",
                    string n when n.StartsWith("Invokable_") => "Method Invokable",
                    string n when n.StartsWith("Metadata_") => "Type Metadata",
                    _ => "Unknown Pattern"
                };
                Console.WriteLine($"  Pattern: {pattern}");
                Console.WriteLine();
            }

            // Also find the metadata class
            var metadataTypes = assembly.GetTypes()
                .Where(t => t.Namespace?.StartsWith("OrleansCodeGen") == true && 
                           t.Name.StartsWith("Metadata_"))
                .ToList();

            if (metadataTypes.Any())
            {
                Console.WriteLine($"\nMetadata classes:");
                foreach (var type in metadataTypes)
                {
                    Console.WriteLine($"  {type.FullName}");
                }
            }

            // Export the list for use in type forwarding
            Console.WriteLine($"\nExporting type list to OrleansCodeGenTypes.txt...");
            var typeList = orleansCodeGenTypes.Select(t => t.FullName!).ToList();
            System.IO.File.WriteAllLines("OrleansCodeGenTypes.txt", typeList);
            Console.WriteLine("Done!");
        }
    }
}