using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Orleans.Metadata;
using Orleans.Runtime;

namespace Granville.Rpc
{
    /// <summary>
    /// Provides information about Granville-generated RPC proxy types.
    /// This looks for proxy types in the GranvilleCodeGen namespace.
    /// </summary>
    internal sealed class GranvilleRpcProvider
    {
        private readonly ILogger<GranvilleRpcProvider> _logger;
        private readonly ConcurrentDictionary<GrainInterfaceType, Type> _proxyTypeCache = new();
        private volatile bool _initialized = false;
        private readonly object _initLock = new();

        public GranvilleRpcProvider(ILogger<GranvilleRpcProvider> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Try to get the proxy type for a given interface.
        /// </summary>
        /// <param name="interfaceType">The grain interface type.</param>
        /// <param name="proxyType">The proxy type if found.</param>
        /// <returns>True if a proxy type was found, false otherwise.</returns>
        public bool TryGet(GrainInterfaceType interfaceType, out Type proxyType)
        {
            EnsureInitialized();

            if (_proxyTypeCache.TryGetValue(interfaceType, out proxyType))
            {
                _logger.LogDebug("Found cached Granville proxy type {ProxyType} for interface {InterfaceType}", 
                    proxyType.FullName, interfaceType);
                return true;
            }

            proxyType = null;
            return false;
        }

        private void EnsureInitialized()
        {
            if (_initialized) return;

            lock (_initLock)
            {
                if (_initialized) return;

                _logger.LogInformation("Initializing GranvilleRpcProvider - scanning for Granville-generated proxy types");

                var assembliesScanned = 0;
                var assembliesWithProxies = 0;

                // Scan all loaded assemblies for Granville-generated proxy types
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        var assemblyName = assembly.FullName ?? "Unknown";
                        
                        // Skip system assemblies for performance
                        if (assembly.FullName.StartsWith("System.") || 
                            assembly.FullName.StartsWith("Microsoft.") ||
                            assembly.IsDynamic)
                        {
                            continue;
                        }

                        assembliesScanned++;
                        _logger.LogDebug("Scanning assembly: {Assembly}", assemblyName);

                        // Get all types from the assembly
                        var allTypes = assembly.GetTypes();
                        _logger.LogDebug("Assembly {Assembly} has {TypeCount} types", assemblyName, allTypes.Length);

                        // Look for types in GranvilleCodeGen namespace
                        var proxyTypes = allTypes
                            .Where(t => t.Namespace?.StartsWith("GranvilleCodeGen") == true &&
                                       t.Name.StartsWith("Proxy_") &&
                                       !t.IsAbstract &&
                                       t.IsClass)
                            .ToList();

                        if (proxyTypes.Count > 0)
                        {
                            assembliesWithProxies++;
                            _logger.LogInformation("Found {Count} proxy types in assembly {Assembly}", proxyTypes.Count, assemblyName);
                        }

                        foreach (var proxyType in proxyTypes)
                        {
                            _logger.LogDebug("Processing proxy type: {ProxyType}", proxyType.FullName);
                            
                            // Find the interface this proxy implements
                            var allInterfaces = proxyType.GetInterfaces();
                            _logger.LogDebug("Proxy {ProxyType} implements {Count} interfaces", proxyType.Name, allInterfaces.Length);
                            
                            var relevantInterfaces = allInterfaces
                                .Where(i => i.Namespace?.Contains("Rpc") == true || 
                                           i.Namespace?.Contains("Shooter") == true)
                                .ToList();

                            foreach (var interfaceTypeInfo in relevantInterfaces)
                            {
                                var interfaceTypeName = interfaceTypeInfo.FullName ?? interfaceTypeInfo.Name;
                                var grainInterfaceType = GrainInterfaceType.Create(interfaceTypeName);
                                
                                if (!_proxyTypeCache.ContainsKey(grainInterfaceType))
                                {
                                    _proxyTypeCache[grainInterfaceType] = proxyType;
                                    _logger.LogInformation("Registered Granville proxy {ProxyType} for interface {InterfaceType}", 
                                        proxyType.FullName, interfaceTypeName);
                                }
                            }
                        }
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        _logger.LogWarning(ex, "Failed to load types from assembly {Assembly}. LoaderExceptions: {LoaderExceptions}", 
                            assembly.FullName,
                            string.Join("; ", ex.LoaderExceptions?.Select(e => e?.Message) ?? Array.Empty<string>()));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error scanning assembly {Assembly} for Granville proxies", assembly.FullName);
                    }
                }

                _logger.LogInformation("GranvilleRpcProvider initialization complete: Scanned {ScannedCount} assemblies, found proxies in {ProxyAssemblyCount} assemblies, registered {ProxyCount} proxy type mappings", 
                    assembliesScanned, assembliesWithProxies, _proxyTypeCache.Count);
                
                if (_proxyTypeCache.Count == 0)
                {
                    _logger.LogWarning("No Granville-generated proxy types found. Make sure Granville code generation is enabled and assemblies are loaded.");
                    
                    // Log all namespaces we've seen to help debug
                    var allNamespaces = AppDomain.CurrentDomain.GetAssemblies()
                        .Where(a => !a.IsDynamic && !a.FullName.StartsWith("System.") && !a.FullName.StartsWith("Microsoft."))
                        .SelectMany(a => 
                        {
                            try { return a.GetTypes().Select(t => t.Namespace).Where(n => n != null).Distinct(); }
                            catch { return Array.Empty<string>(); }
                        })
                        .Distinct()
                        .OrderBy(n => n)
                        .ToList();
                    
                    _logger.LogDebug("All namespaces found: {Namespaces}", string.Join(", ", allNamespaces));
                }

                _initialized = true;
            }
        }
    }
}