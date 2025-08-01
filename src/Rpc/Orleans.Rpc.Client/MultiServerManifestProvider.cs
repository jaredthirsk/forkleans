using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Orleans.Metadata;
using Orleans.Runtime;
using Orleans.Runtime.Utilities;
using Granville.Rpc.Protocol;

namespace Granville.Rpc
{
    /// <summary>
    /// Manifest provider that maintains separate manifests for each connected server.
    /// </summary>
    internal class MultiServerManifestProvider : IClusterManifestProvider
    {
        private readonly ILogger<MultiServerManifestProvider> _logger;
        private readonly ConcurrentDictionary<string, ClusterManifest> _serverManifests = new();
        private readonly SemaphoreSlim _updateLock = new(1, 1);
        private readonly AsyncEnumerable<ClusterManifest> _updates;
        
        // Composite manifest that represents the union of all server manifests
        private ClusterManifest _compositeManifest;
        private GrainManifest _localGrainManifest;
        private int _versionCounter = 0;

        public MultiServerManifestProvider(ILogger<MultiServerManifestProvider> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Initialize with empty manifests
            _localGrainManifest = new GrainManifest(
                ImmutableDictionary<GrainType, GrainProperties>.Empty,
                ImmutableDictionary<GrainInterfaceType, GrainInterfaceProperties>.Empty);
                
            var silos = ImmutableDictionary.CreateBuilder<SiloAddress, GrainManifest>();
            silos.Add(SiloAddress.New(System.Net.IPEndPoint.Parse("127.0.0.1:11111"), 0), _localGrainManifest);
            
            _compositeManifest = new ClusterManifest(new MajorMinorVersion(++_versionCounter, 0), silos.ToImmutable());
            
            // Create the updates stream
            _updates = new AsyncEnumerable<ClusterManifest>(
                initialValue: _compositeManifest,
                updateValidator: (previous, proposed) => false, // Never accept updates
                onPublished: _ => { });
        }

        /// <summary>
        /// Updates the manifest for a specific server.
        /// </summary>
        public async Task UpdateFromServerAsync(string serverId, RpcGrainManifest grainManifest)
        {
            await _updateLock.WaitAsync();
            try
            {
                var clusterManifest = BuildClusterManifest(grainManifest);
                _serverManifests[serverId] = clusterManifest;
                
                // Rebuild composite manifest
                RebuildCompositeManifest();
                
                _logger.LogInformation("Updated manifest for server {ServerId}: {GrainCount} grains, {InterfaceCount} interfaces, {MappingCount} interface-to-grain mappings",
                    serverId, grainManifest.GrainProperties?.Count ?? 0, grainManifest.InterfaceProperties?.Count ?? 0,
                    grainManifest.InterfaceToGrainMappings?.Count ?? 0);
                    
                // Log the current state
                _logger.LogDebug("Composite manifest now has {GrainCount} grains and {InterfaceCount} interfaces",
                    _compositeManifest.AllGrainManifests.Sum(m => m.Grains.Count),
                    _compositeManifest.AllGrainManifests.Sum(m => m.Interfaces.Count));
                    
                // Force the resolver to rebuild its cache
                _logger.LogInformation("Manifest updated, version is now: {Version}", _compositeManifest.Version);
            }
            finally
            {
                _updateLock.Release();
            }
        }

        /// <summary>
        /// Removes the manifest for a disconnected server.
        /// </summary>
        public async Task RemoveServerManifestAsync(string serverId)
        {
            await _updateLock.WaitAsync();
            try
            {
                if (_serverManifests.TryRemove(serverId, out _))
                {
                    RebuildCompositeManifest();
                    _logger.LogInformation("Removed manifest for server {ServerId}", serverId);
                }
            }
            finally
            {
                _updateLock.Release();
            }
        }

        /// <summary>
        /// Gets the manifest for a specific server.
        /// </summary>
        public ClusterManifest GetServerManifest(string serverId)
        {
            return _serverManifests.GetValueOrDefault(serverId);
        }

        public ClusterManifest Current => _compositeManifest;

        public GrainManifest LocalGrainManifest => _localGrainManifest;

        public IAsyncEnumerable<ClusterManifest> Updates => _updates;

        private void RebuildCompositeManifest()
        {
            // Create composite manifest from all server manifests
            var grainsBuilder = ImmutableDictionary.CreateBuilder<GrainType, GrainProperties>();
            var interfacesBuilder = ImmutableDictionary.CreateBuilder<GrainInterfaceType, GrainInterfaceProperties>();

            foreach (var manifest in _serverManifests.Values)
            {
                // Add all grains from this server
                foreach (var silo in manifest.AllGrainManifests)
                {
                    foreach (var kvp in silo.Grains)
                    {
                        // Only add if not already present to avoid duplicate interface mappings
                        if (!grainsBuilder.ContainsKey(kvp.Key))
                        {
                            grainsBuilder[kvp.Key] = kvp.Value;
                        }
                    }
                    
                    foreach (var kvp in silo.Interfaces)
                    {
                        // Only add if not already present
                        if (!interfacesBuilder.ContainsKey(kvp.Key))
                        {
                            interfacesBuilder[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }

            _localGrainManifest = new GrainManifest(
                grainsBuilder.ToImmutable(),
                interfacesBuilder.ToImmutable());
                
            var silos = ImmutableDictionary.CreateBuilder<SiloAddress, GrainManifest>();
            silos.Add(SiloAddress.New(System.Net.IPEndPoint.Parse("127.0.0.1:11111"), 0), _localGrainManifest);
            
            // Only increment version if we have actual content
            if (grainsBuilder.Count > 0 || interfacesBuilder.Count > 0)
            {
                _compositeManifest = new ClusterManifest(new MajorMinorVersion(++_versionCounter, 0), silos.ToImmutable());
            }
            else
            {
                // Keep the same version to avoid triggering cache rebuilds with empty data
                var currentVersion = _compositeManifest?.Version ?? new MajorMinorVersion(0, 0);
                _compositeManifest = new ClusterManifest(currentVersion, silos.ToImmutable());
            }
        }

        private ClusterManifest BuildClusterManifest(RpcGrainManifest grainManifest)
        {
            var grainsBuilder = ImmutableDictionary.CreateBuilder<GrainType, GrainProperties>();
            var interfacesBuilder = ImmutableDictionary.CreateBuilder<GrainInterfaceType, GrainInterfaceProperties>();

            // Convert grain properties
            if (grainManifest.GrainProperties != null)
            {
                foreach (var kvp in grainManifest.GrainProperties)
                {
                    var grainType = GrainType.Create(kvp.Key);
                    var properties = kvp.Value.ToImmutableDictionary();
                    
                    // Ensure TypeName and FullTypeName are set if not already present
                    if (!properties.ContainsKey("type-name") && kvp.Key.Contains('.'))
                    {
                        var lastDot = kvp.Key.LastIndexOf('.');
                        var typeName = kvp.Key.Substring(lastDot + 1);
                        properties = properties.Add("type-name", typeName);
                    }
                    if (!properties.ContainsKey("full-type-name"))
                    {
                        properties = properties.Add("full-type-name", kvp.Key);
                    }
                    
                    grainsBuilder[grainType] = new GrainProperties(properties);
                }
            }

            // Convert interface properties
            if (grainManifest.InterfaceProperties != null)
            {
                foreach (var kvp in grainManifest.InterfaceProperties)
                {
                    var interfaceType = GrainInterfaceType.Create(kvp.Key);
                    var properties = kvp.Value.ToImmutableDictionary();
                    interfacesBuilder[interfaceType] = new GrainInterfaceProperties(properties);
                }
            }

            // Add interface-to-grain mappings as grain properties
            if (grainManifest.InterfaceToGrainMappings != null)
            {
                // Group interfaces by grain type to assign sequential indices
                var grainInterfaces = new Dictionary<string, List<string>>();
                foreach (var mapping in grainManifest.InterfaceToGrainMappings)
                {
                    if (!grainInterfaces.ContainsKey(mapping.Value))
                    {
                        grainInterfaces[mapping.Value] = new List<string>();
                    }
                    grainInterfaces[mapping.Value].Add(mapping.Key);
                }
                
                // Now add interface properties with numeric indices
                foreach (var grainGroup in grainInterfaces)
                {
                    var grainType = GrainType.Create(grainGroup.Key);
                    
                    // Get existing properties or create new
                    GrainProperties existingProps;
                    ImmutableDictionary<string, string> props;
                    if (grainsBuilder.TryGetValue(grainType, out existingProps))
                    {
                        props = existingProps.Properties;
                    }
                    else
                    {
                        props = ImmutableDictionary<string, string>.Empty;
                    }
                    
                    // Determine the starting counter by checking existing interface properties
                    var counter = 0;
                    while (props.ContainsKey($"{WellKnownGrainTypeProperties.ImplementedInterfacePrefix}{counter}"))
                    {
                        counter++;
                    }
                    
                    // Add each interface with a numeric index
                    foreach (var interfaceTypeStr in grainGroup.Value)
                    {
                        var interfaceType = GrainInterfaceType.Create(interfaceTypeStr);
                        var key = $"{WellKnownGrainTypeProperties.ImplementedInterfacePrefix}{counter}";
                        // Check if this key already exists to avoid duplicates
                        if (!props.ContainsKey(key))
                        {
                            props = props.Add(key, interfaceType.ToString());
                        }
                        counter++;
                    }
                    
                    grainsBuilder[grainType] = new GrainProperties(props);
                }
            }

            var grainManifestBuilt = new GrainManifest(
                grainsBuilder.ToImmutable(),
                interfacesBuilder.ToImmutable());
                
            var silos = ImmutableDictionary.CreateBuilder<SiloAddress, GrainManifest>();
            silos.Add(SiloAddress.New(System.Net.IPEndPoint.Parse("127.0.0.1:11111"), 0), grainManifestBuilt);
            
            // Note: This is a per-server manifest, not the composite one, so we can use Zero version
            return new ClusterManifest(MajorMinorVersion.Zero, silos.ToImmutable());
        }
    }
}