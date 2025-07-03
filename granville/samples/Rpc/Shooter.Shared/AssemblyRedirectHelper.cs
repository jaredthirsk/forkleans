using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Shooter.Shared
{
    /// <summary>
    /// Helper class to ensure Orleans assemblies from Granville packages are loaded
    /// when third-party packages like UFX.Orleans.SignalRBackplane request Microsoft.Orleans assemblies.
    /// Note: Granville packages contain assemblies named Orleans.* (not Granville.Orleans.*).
    /// </summary>
    public static class AssemblyRedirectHelper
    {
        private static bool _isInitialized = false;
        private static readonly object _lock = new object();

        /// <summary>
        /// Initializes the assembly redirect handler. Call this early in your application startup.
        /// </summary>
        public static void Initialize()
        {
            lock (_lock)
            {
                if (_isInitialized) return;
                
                Console.WriteLine("[AssemblyRedirect] Initializing assembly redirect handler...");
                
                // Hook into the assembly resolution process
                AssemblyLoadContext.Default.Resolving += OnAssemblyResolving;
                AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
                
                _isInitialized = true;
                Console.WriteLine("[AssemblyRedirect] Assembly redirect handler initialized.");
            }
        }

        private static Assembly? OnAssemblyResolving(AssemblyLoadContext context, AssemblyName assemblyName)
        {
            return TryLoadRedirectedAssembly(assemblyName);
        }

        private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
        {
            var assemblyName = new AssemblyName(args.Name);
            return TryLoadRedirectedAssembly(assemblyName);
        }

        private static Assembly? TryLoadRedirectedAssembly(AssemblyName assemblyName)
        {
            // Check if this is a Microsoft.Orleans assembly request
            if (assemblyName.Name?.StartsWith("Microsoft.Orleans.") == true)
            {
                // Extract the actual Orleans assembly name (remove Microsoft. prefix)
                var orleansAssemblyName = assemblyName.Name.Replace("Microsoft.", "");
                
                Console.WriteLine($"[AssemblyRedirect] Redirecting {assemblyName.Name} -> {orleansAssemblyName}");
                
                try
                {
                    // Try to load the Orleans assembly (from Granville packages)
                    var targetAssemblyName = new AssemblyName(orleansAssemblyName)
                    {
                        Version = assemblyName.Version,
                        CultureInfo = assemblyName.CultureInfo,
                        ProcessorArchitecture = assemblyName.ProcessorArchitecture
                    };
                    
                    // First try to load from the default context
                    var assembly = AssemblyLoadContext.Default.LoadFromAssemblyName(targetAssemblyName);
                    if (assembly != null)
                    {
                        Console.WriteLine($"[AssemblyRedirect] Successfully loaded {orleansAssemblyName}");
                        return assembly;
                    }
                }
                catch (FileNotFoundException)
                {
                    // Try loading from the application directory
                    var appDir = AppDomain.CurrentDomain.BaseDirectory;
                    var possiblePaths = new[]
                    {
                        Path.Combine(appDir, $"{orleansAssemblyName}.dll"),
                        Path.Combine(appDir, "bin", $"{orleansAssemblyName}.dll"),
                        Path.Combine(appDir, "..", $"{orleansAssemblyName}.dll")
                    };
                    
                    foreach (var path in possiblePaths)
                    {
                        if (File.Exists(path))
                        {
                            Console.WriteLine($"[AssemblyRedirect] Loading from path: {path}");
                            return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
                        }
                    }
                    
                    Console.WriteLine($"[AssemblyRedirect] Could not find {orleansAssemblyName} in any search paths");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AssemblyRedirect] Error loading {orleansAssemblyName}: {ex.Message}");
                }
            }
            
            return null;
        }

        /// <summary>
        /// Preload Orleans assemblies from Granville packages to ensure they're available for redirection
        /// </summary>
        public static void PreloadGranvilleAssemblies()
        {
            var assembliesToPreload = new[]
            {
                "Orleans.Core",
                "Orleans.Core.Abstractions",
                "Orleans.Runtime",
                "Orleans.Serialization",
                "Orleans.Serialization.Abstractions"
            };
            
            foreach (var assemblyName in assembliesToPreload)
            {
                try
                {
                    Assembly.Load(assemblyName);
                    Console.WriteLine($"[AssemblyRedirect] Preloaded {assemblyName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AssemblyRedirect] Warning: Could not preload {assemblyName}: {ex.Message}");
                }
            }
        }
    }
}