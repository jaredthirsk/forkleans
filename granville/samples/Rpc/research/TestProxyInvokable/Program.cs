using System;
using System.Reflection;
using Orleans;
using Orleans.Runtime;
using Orleans.Serialization.Invocation;

namespace TestProxyInvokable
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Testing Orleans Proxy IInvokable argument handling...");
            
            // Find the generated proxy type
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            Type? proxyType = null;
            
            foreach (var assembly in assemblies)
            {
                try
                {
                    var types = assembly.GetTypes();
                    foreach (var type in types)
                    {
                        if (type.Name.Contains("Proxy_IGameRpcGrain"))
                        {
                            proxyType = type;
                            Console.WriteLine($"Found proxy type: {type.FullName} in assembly {assembly.GetName().Name}");
                            break;
                        }
                    }
                    if (proxyType != null) break;
                }
                catch (Exception ex)
                {
                    // Skip assemblies we can't load
                    Console.WriteLine($"Skipping assembly {assembly.GetName().Name}: {ex.Message}");
                }
            }
            
            if (proxyType == null)
            {
                Console.WriteLine("ERROR: Could not find Proxy_IGameRpcGrain type");
                Console.WriteLine("\nSearched assemblies:");
                foreach (var assembly in assemblies)
                {
                    Console.WriteLine($"  - {assembly.GetName().Name}");
                }
                return;
            }
            
            // Examine the proxy's methods
            Console.WriteLine($"\nMethods on {proxyType.Name}:");
            foreach (var method in proxyType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                Console.WriteLine($"  - {method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})");
            }
            
            // Try to find how it creates invokables
            var connectPlayerMethod = proxyType.GetMethod("ConnectPlayer");
            if (connectPlayerMethod != null)
            {
                Console.WriteLine($"\nConnectPlayer method found: {connectPlayerMethod}");
                
                // Look for invokable creation in the method body
                try
                {
                    var methodBody = connectPlayerMethod.GetMethodBody();
                    if (methodBody != null)
                    {
                        var ilBytes = methodBody.GetILAsByteArray();
                        Console.WriteLine($"Method IL size: {ilBytes?.Length ?? 0} bytes");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not examine method body: {ex.Message}");
                }
            }
            
            // Check for nested invokable types
            Console.WriteLine($"\nNested types in {proxyType.Name}:");
            foreach (var nestedType in proxyType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                Console.WriteLine($"  - {nestedType.Name}");
                if (nestedType.GetInterfaces().Contains(typeof(IInvokable)))
                {
                    Console.WriteLine($"    ^ This implements IInvokable!");
                    
                    // Check if it has fields for arguments
                    Console.WriteLine($"    Fields:");
                    foreach (var field in nestedType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        Console.WriteLine($"      - {field.Name}: {field.FieldType.Name}");
                    }
                }
            }
        }
    }
}