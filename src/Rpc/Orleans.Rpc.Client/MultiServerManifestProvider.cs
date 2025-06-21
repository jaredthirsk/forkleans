using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Forkleans.Metadata;
using Forkleans.Runtime;
using Forkleans.Runtime.Utilities;
using Forkleans.Rpc.Protocol;

namespace Forkleans.Rpc
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

        public MultiServerManifestProvider(ILogger<MultiServerManifestProvider> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Initialize with empty manifests
            _localGrainManifest = new GrainManifest(
                ImmutableDictionary<GrainType, GrainProperties>.Empty,
                ImmutableDictionary<GrainInterfaceType, GrainInterfaceProperties>.Empty);
                
            var silos = ImmutableDictionary.CreateBuilder<SiloAddress, GrainManifest>();
            silos.Add(SiloAddress.New(System.Net.IPEndPoint.Parse("127.0.0.1:11111"), 0), _localGrainManifest);
            
            _compositeManifest = new ClusterManifest(MajorMinorVersion.Zero, silos.ToImmutable());
            
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
                
                _logger.LogInformation("Updated manifest for server {ServerId}: {GrainCount} grains, {InterfaceCount} interfaces",
                    serverId, grainManifest.GrainProperties?.Count ?? 0, grainManifest.InterfaceProperties?.Count ?? 0);
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
                        grainsBuilder[kvp.Key] = kvp.Value;
                    }
                    
                    foreach (var kvp in silo.Interfaces)
                    {
                        interfacesBuilder[kvp.Key] = kvp.Value;
                    }
                }
            }

            _localGrainManifest = new GrainManifest(
                grainsBuilder.ToImmutable(),
                interfacesBuilder.ToImmutable());
                
            var silos = ImmutableDictionary.CreateBuilder<SiloAddress, GrainManifest>();
            silos.Add(SiloAddress.New(System.Net.IPEndPoint.Parse("127.0.0.1:11111"), 0), _localGrainManifest);
            
            _compositeManifest = new ClusterManifest(MajorMinorVersion.Zero, silos.ToImmutable());
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
                foreach (var mapping in grainManifest.InterfaceToGrainMappings)
                {
                    var grainType = GrainType.Create(mapping.Value);
                    GrainProperties existingProps;
                    if (grainsBuilder.TryGetValue(grainType, out existingProps))
                    {
                        var newProps = existingProps.Properties.Add($"interface.{mapping.Key}", mapping.Key);
                        grainsBuilder[grainType] = new GrainProperties(newProps);
                    }
                    else
                    {
                        var props = ImmutableDictionary<string, string>.Empty.Add($"interface.{mapping.Key}", mapping.Key);
                        grainsBuilder[grainType] = new GrainProperties(props);
                    }
                }
            }

            var grainManifestBuilt = new GrainManifest(
                grainsBuilder.ToImmutable(),
                interfacesBuilder.ToImmutable());
                
            var silos = ImmutableDictionary.CreateBuilder<SiloAddress, GrainManifest>();
            silos.Add(SiloAddress.New(System.Net.IPEndPoint.Parse("127.0.0.1:11111"), 0), grainManifestBuilt);
            
            return new ClusterManifest(MajorMinorVersion.Zero, silos.ToImmutable());
        }
    }
}