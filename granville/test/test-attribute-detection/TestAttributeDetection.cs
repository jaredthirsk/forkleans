using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

class TestAttributeDetection
{
    static void Main2(string[] args)
    {
        Console.WriteLine("=== ApplicationPart Attribute Detection Test ===\n");
        
        // Test with Granville.Orleans.Serialization.dll
        var assemblyPath = args.Length > 0 ? args[0] : "Granville.Orleans.Serialization.dll";
        
        try
        {
            // Method 1: Load from file path
            Console.WriteLine($"Loading assembly from: {assemblyPath}");
            Assembly assembly;
            
            if (System.IO.File.Exists(assemblyPath))
            {
                assembly = Assembly.LoadFrom(assemblyPath);
            }
            else
            {
                // Try loading by name
                assembly = Assembly.Load("Granville.Orleans.Serialization");
            }
            
            Console.WriteLine($"Successfully loaded: {assembly.FullName}");
            Console.WriteLine($"Location: {assembly.Location}");
            Console.WriteLine($"CodeBase: {assembly.CodeBase}");
            Console.WriteLine();
            
            // List ALL custom attributes with their full details
            Console.WriteLine("=== All Custom Attributes ===");
            var allAttributes = assembly.GetCustomAttributes();
            int attrCount = 0;
            foreach (var attr in allAttributes)
            {
                attrCount++;
                Console.WriteLine($"{attrCount}. Type: {attr.GetType().FullName}");
                Console.WriteLine($"   Assembly: {attr.GetType().Assembly.FullName}");
                Console.WriteLine($"   Module: {attr.GetType().Module.Name}");
                
                // If it looks like ApplicationPart, show properties
                if (attr.GetType().Name.Contains("ApplicationPart"))
                {
                    Console.WriteLine("   >>> This looks like ApplicationPartAttribute!");
                    var props = attr.GetType().GetProperties();
                    foreach (var prop in props)
                    {
                        try
                        {
                            var value = prop.GetValue(attr);
                            Console.WriteLine($"   Property '{prop.Name}': {value}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"   Property '{prop.Name}': <error: {ex.Message}>");
                        }
                    }
                }
                Console.WriteLine();
            }
            
            if (attrCount == 0)
            {
                Console.WriteLine("No custom attributes found!");
            }
            
            Console.WriteLine("\n=== Different Detection Methods ===");
            
            // Method 1: Direct type comparison with FullName
            var hasAppPart1 = assembly.GetCustomAttributes()
                .Any(attr => attr.GetType().FullName == "Orleans.ApplicationPartAttribute");
            Console.WriteLine($"1. FullName == 'Orleans.ApplicationPartAttribute': {hasAppPart1}");
            
            // Method 2: Name comparison (less strict)
            var hasAppPart2 = assembly.GetCustomAttributes()
                .Any(attr => attr.GetType().Name == "ApplicationPartAttribute");
            Console.WriteLine($"2. Name == 'ApplicationPartAttribute': {hasAppPart2}");
            
            // Method 3: Contains check
            var hasAppPart3 = assembly.GetCustomAttributes()
                .Any(attr => attr.GetType().FullName.Contains("ApplicationPart"));
            Console.WriteLine($"3. FullName.Contains('ApplicationPart'): {hasAppPart3}");
            
            // Check for FrameworkPartAttribute
            var hasFrameworkPart = assembly.GetCustomAttributes()
                .Any(attr => attr.GetType().FullName == "Orleans.Metadata.FrameworkPartAttribute");
            Console.WriteLine($"4. Has FrameworkPartAttribute: {hasFrameworkPart}");
            
            // Check if NOT a framework part (which means it should be treated as an application part)
            var isNotFrameworkPart = !hasFrameworkPart;
            Console.WriteLine($"5. Is NOT a FrameworkPart (should be treated as ApplicationPart): {isNotFrameworkPart}");
            
            // Method 4: Try to load the attribute type directly
            Console.WriteLine("\n=== Attempting to load ApplicationPartAttribute type ===");
            try
            {
                // Try from Orleans.Serialization.Abstractions
                var orleansSerAbstractions = Assembly.Load("Orleans.Serialization.Abstractions");
                Console.WriteLine($"Loaded Orleans.Serialization.Abstractions: {orleansSerAbstractions.FullName}");
                
                var appPartType = orleansSerAbstractions.GetType("Orleans.ApplicationPartAttribute");
                if (appPartType != null)
                {
                    Console.WriteLine($"Found ApplicationPartAttribute in Orleans.Serialization.Abstractions");
                    Console.WriteLine($"Type: {appPartType.FullName}");
                    
                    // Check if assembly has this specific type
                    var hasSpecificType = assembly.GetCustomAttributes(appPartType, false).Any();
                    Console.WriteLine($"Has attribute of this specific type: {hasSpecificType}");
                }
                else
                {
                    Console.WriteLine("ApplicationPartAttribute NOT found in Orleans.Serialization.Abstractions");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading Orleans.Serialization.Abstractions: {ex.Message}");
            }
            
            // Try from Granville.Orleans.Serialization.Abstractions
            try
            {
                var granvilleSerAbstractions = Assembly.Load("Granville.Orleans.Serialization.Abstractions");
                Console.WriteLine($"\nLoaded Granville.Orleans.Serialization.Abstractions: {granvilleSerAbstractions.FullName}");
                
                var appPartType = granvilleSerAbstractions.GetType("Orleans.ApplicationPartAttribute");
                if (appPartType != null)
                {
                    Console.WriteLine($"Found ApplicationPartAttribute in Granville.Orleans.Serialization.Abstractions");
                    Console.WriteLine($"Type: {appPartType.FullName}");
                    
                    // Check if assembly has this specific type
                    var hasSpecificType = assembly.GetCustomAttributes(appPartType, false).Any();
                    Console.WriteLine($"Has attribute of this specific type: {hasSpecificType}");
                }
                else
                {
                    Console.WriteLine("ApplicationPartAttribute NOT found in Granville.Orleans.Serialization.Abstractions");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading Granville.Orleans.Serialization.Abstractions: {ex.Message}");
            }
            
            // Method 5: Check using GetCustomAttributesData (metadata-only)
            Console.WriteLine("\n=== Using GetCustomAttributesData (metadata) ===");
            var customAttrData = assembly.GetCustomAttributesData();
            foreach (var data in customAttrData)
            {
                Console.WriteLine($"Attribute: {data.AttributeType.FullName}");
                if (data.AttributeType.Name.Contains("ApplicationPart"))
                {
                    Console.WriteLine("  >>> Found ApplicationPart in metadata!");
                    if (data.ConstructorArguments.Count > 0)
                    {
                        Console.WriteLine($"  Constructor args: {string.Join(", ", data.ConstructorArguments.Select(a => a.Value))}");
                    }
                }
            }
            
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}