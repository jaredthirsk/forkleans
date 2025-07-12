using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Serialization;
using Orleans.Serialization.Configuration;
using Orleans.Serialization.Serializers;

class TestResponseCopierDiscovery
{
    static void Main(string[] args)
    {
        Console.WriteLine("=== Response<T> Copier Discovery Test ===\n");

        // Step 1: Load the Granville.Orleans.Serialization assembly
        var assemblyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Granville.Orleans.Serialization.dll");
        Console.WriteLine($"Loading from path: {assemblyPath}");
        var serializationAssembly = Assembly.LoadFrom(assemblyPath);
        Console.WriteLine($"Loaded assembly: {serializationAssembly.FullName}");
        Console.WriteLine($"Assembly location: {serializationAssembly.Location}");

        // Step 2: Check for TypeManifestProvider attribute
        var manifestProviderAttrs = serializationAssembly.GetCustomAttributes<TypeManifestProviderAttribute>();
        Console.WriteLine($"\nTypeManifestProvider attributes found: {manifestProviderAttrs.Count()}");
        
        foreach (var attr in manifestProviderAttrs)
        {
            Console.WriteLine($"  Provider Type: {attr.ProviderType.FullName}");
            
            // Step 3: Try to instantiate the provider
            try
            {
                var provider = Activator.CreateInstance(attr.ProviderType) as IConfigureOptions<TypeManifestOptions>;
                if (provider != null)
                {
                    Console.WriteLine($"  Successfully created provider instance");
                    
                    // Step 4: Apply configuration
                    var options = new TypeManifestOptions();
                    provider.Configure(options);
                    
                    Console.WriteLine($"\n  Registered Copiers: {options.Copiers.Count}");
                    
                    // Look for Response copiers
                    var responseCopiers = options.Copiers
                        .Where(t => t.Name.Contains("Response") && t.Name.Contains("Copier"))
                        .ToList();
                    
                    Console.WriteLine($"  Response-related copiers: {responseCopiers.Count}");
                    foreach (var copier in responseCopiers)
                    {
                        Console.WriteLine($"    - {copier.FullName}");
                    }
                    
                    // Check specifically for PooledResponseCopier<>
                    var pooledResponseCopier = options.Copiers
                        .FirstOrDefault(t => t.Name == "PooledResponseCopier`1");
                    
                    if (pooledResponseCopier != null)
                    {
                        Console.WriteLine($"\n  ✓ Found PooledResponseCopier<T>: {pooledResponseCopier.FullName}");
                        Console.WriteLine($"    Assembly: {pooledResponseCopier.Assembly.FullName}");
                        Console.WriteLine($"    IsGenericTypeDefinition: {pooledResponseCopier.IsGenericTypeDefinition}");
                    }
                    else
                    {
                        Console.WriteLine($"\n  ✗ PooledResponseCopier<T> NOT FOUND in copiers!");
                    }
                    
                    // Also check serializers
                    var pooledResponseCodec = options.Serializers
                        .FirstOrDefault(t => t.Name == "PooledResponseCodec`1");
                    
                    if (pooledResponseCodec != null)
                    {
                        Console.WriteLine($"\n  ✓ Found PooledResponseCodec<T>: {pooledResponseCodec.FullName}");
                    }
                    else
                    {
                        Console.WriteLine($"\n  ✗ PooledResponseCodec<T> NOT FOUND in serializers!");
                    }
                }
                else
                {
                    Console.WriteLine($"  ERROR: Provider does not implement IConfigureOptions<TypeManifestOptions>");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ERROR creating provider: {ex.Message}");
            }
        }
        
        // Step 5: Test with a full serializer builder
        Console.WriteLine("\n\n=== Testing with SerializerBuilder ===");
        try
        {
            var services = new ServiceCollection();
            services.AddSerializer(builder =>
            {
                // Add the assembly
                builder.AddAssembly(serializationAssembly);
            });
            
            var serviceProvider = services.BuildServiceProvider();
            
            // Get the configured options
            var optionsMonitor = serviceProvider.GetRequiredService<IOptions<TypeManifestOptions>>();
            var manifestOptions = optionsMonitor.Value;
            
            Console.WriteLine($"\nConfigured Copiers: {manifestOptions.Copiers.Count}");
            
            var responseCopiers = manifestOptions.Copiers
                .Where(t => t.Name.Contains("Response"))
                .ToList();
            
            Console.WriteLine($"Response-related copiers after configuration: {responseCopiers.Count}");
            foreach (var copier in responseCopiers)
            {
                Console.WriteLine($"  - {copier.FullName}");
            }
            
            // Try to get the codec provider and check for Response<string> copier
            var codecProvider = serviceProvider.GetRequiredService<CodecProvider>();
            Console.WriteLine($"\nCodecProvider created successfully");
            
            // Test if we can get a copier for Response<string>
            var responseType = Type.GetType("Orleans.Serialization.Invocation.Response`1[[System.String, System.Private.CoreLib]], Granville.Orleans.Serialization");
            if (responseType != null)
            {
                Console.WriteLine($"\nTesting copier for: {responseType.FullName}");
                try
                {
                    var copier = codecProvider.TryGetDeepCopier(responseType);
                    if (copier != null)
                    {
                        Console.WriteLine($"  ✓ Successfully got copier: {copier.GetType().FullName}");
                    }
                    else
                    {
                        Console.WriteLine($"  ✗ TryGetDeepCopier returned null");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ✗ Error getting copier: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"\nCould not construct Response<string> type");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR in SerializerBuilder test: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}