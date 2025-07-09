using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Shooter.Shared
{
    /// <summary>
    /// Helper class to redirect Orleans.* assembly requests to Granville.Orleans.* assemblies
    /// when third-party packages like UFX.Orleans.SignalRBackplane request Orleans assemblies.
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
            // Check if this is an Orleans assembly request (redirect to Granville.Orleans)
            if (assemblyName.Name?.StartsWith("Orleans.") == true &&
                !assemblyName.Name.StartsWith("Orleans.Rpc.")) // Don't redirect Granville RPC assemblies
            {
                // Create the Granville assembly name
                var granvilleAssemblyName = $"Granville.{assemblyName.Name}";

                Console.WriteLine($"[AssemblyRedirect] Redirecting {assemblyName.Name} -> {granvilleAssemblyName}");

                try
                {
                    // Try to load the Granville.Orleans assembly
                    var targetAssemblyName = new AssemblyName(granvilleAssemblyName)
                    {
                        Version = assemblyName.Version,
                        CultureInfo = assemblyName.CultureInfo
                    };

                    // First try to load from the default context
                    var assembly = AssemblyLoadContext.Default.LoadFromAssemblyName(targetAssemblyName);
                    if (assembly != null)
                    {
                        Console.WriteLine($"[AssemblyRedirect] Successfully loaded {granvilleAssemblyName}");
                        return assembly;
                    }
                }
                catch (FileNotFoundException)
                {
                    // Try loading from the application directory
                    var appDir = AppDomain.CurrentDomain.BaseDirectory;
                    var possiblePaths = new[]
                    {
                        Path.Combine(appDir, $"{granvilleAssemblyName}.dll"),
                        Path.Combine(appDir, "bin", $"{granvilleAssemblyName}.dll"),
                        Path.Combine(appDir, "..", $"{granvilleAssemblyName}.dll")
                    };

                    foreach (var path in possiblePaths)
                    {
                        if (File.Exists(path))
                        {
                            Console.WriteLine($"[AssemblyRedirect] Loading from path: {path}");
                            return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
                        }
                    }

                    Console.WriteLine($"[AssemblyRedirect] Could not find {granvilleAssemblyName} in any search paths");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AssemblyRedirect] Error loading {granvilleAssemblyName}: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"[AssemblyRedirect] Ignoring {assemblyName.Name}");
            }
            return null;
        }

        /// <summary>
        /// Preload Granville.Orleans assemblies to ensure they're available for redirection
        /// </summary>
        public static void PreloadGranvilleAssemblies()
        {
            var assembliesToPreload = new[]
            {
                "Granville.Orleans.Core",
                "Granville.Orleans.Core.Abstractions",
                "Granville.Orleans.Runtime",
                "Granville.Orleans.Serialization",
                "Granville.Orleans.Serialization.Abstractions"
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