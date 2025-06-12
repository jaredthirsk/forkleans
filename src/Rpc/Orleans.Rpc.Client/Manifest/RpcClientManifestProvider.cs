#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Forkleans.Configuration;
using Forkleans.Runtime.Utilities;
using Forkleans.Metadata;
using Forkleans.Runtime;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace Forkleans.Rpc.Hosting
{
    /// <summary>
    /// Simplified <see cref="IClusterManifestProvider"/> implementation for RPC clients.
    /// This version provides only local grain manifest without cluster synchronization.
    /// </summary>
    internal class RpcClientManifestProvider : IClusterManifestProvider
    {
        private ClusterManifest _manifest;
        private readonly AsyncEnumerable<ClusterManifest> _updates;
        private GrainManifest _localGrainManifest;
        private readonly ILocalClientDetails _localClientDetails;
        private readonly ILogger<RpcClientManifestProvider> _logger;
        private readonly object _lock = new object();

        public RpcClientManifestProvider(
            IEnumerable<IGrainInterfacePropertiesProvider> grainInterfacePropertiesProviders,
            IOptions<GrainTypeOptions> grainTypeOptions,
            GrainInterfaceTypeResolver interfaceTypeResolver,
            ILocalClientDetails localClientDetails,
            ILogger<RpcClientManifestProvider> logger)
        {
            _localClientDetails = localClientDetails;
            _logger = logger;
            
            // Create the local grain manifest with interface information
            var interfaces = CreateInterfaceManifest(grainInterfacePropertiesProviders, grainTypeOptions, interfaceTypeResolver);
            _localGrainManifest = new GrainManifest(
                ImmutableDictionary<GrainType, GrainProperties>.Empty, 
                interfaces);

            // Create a minimal cluster manifest containing only the local client
            var siloAddress = localClientDetails.ClientAddress;
            var silos = ImmutableDictionary.CreateBuilder<SiloAddress, GrainManifest>();
            silos.Add(siloAddress, _localGrainManifest);
            
            _manifest = new ClusterManifest(MajorMinorVersion.MinValue, silos.ToImmutable());

            // Create the updates stream with the initial manifest
            _updates = new AsyncEnumerable<ClusterManifest>(
                initialValue: _manifest,
                updateValidator: (previous, proposed) => false, // Never accept updates in RPC mode
                onPublished: _ => { });
        }

        /// <inheritdoc />
        public ClusterManifest Current
        {
            get
            {
                lock (_lock)
                {
                    return _manifest;
                }
            }
        }

        /// <inheritdoc />
        public IAsyncEnumerable<ClusterManifest> Updates => _updates;

        /// <inheritdoc />
        public GrainManifest LocalGrainManifest
        {
            get
            {
                lock (_lock)
                {
                    return _localGrainManifest;
                }
            }
        }

        /// <summary>
        /// Updates the manifest with grain information from the server.
        /// </summary>
        public void UpdateFromServer(Protocol.RpcGrainManifest serverManifest)
        {
            lock (_lock)
            {
                // Convert server manifest to GrainManifest
                var grainPropertiesBuilder = ImmutableDictionary.CreateBuilder<GrainType, GrainProperties>();
                var interfacePropertiesBuilder = ImmutableDictionary.CreateBuilder<GrainInterfaceType, GrainInterfaceProperties>();

                // Build grain types and properties
                foreach (var kvp in serverManifest.GrainProperties)
                {
                    var grainType = GrainType.Create(kvp.Key);
                    var properties = new GrainProperties(kvp.Value.ToImmutableDictionary());
                    grainPropertiesBuilder.Add(grainType, properties);
                }

                // Build interface types and properties
                foreach (var kvp in serverManifest.InterfaceProperties)
                {
                    var interfaceType = GrainInterfaceType.Create(kvp.Key);
                    var properties = new GrainInterfaceProperties(kvp.Value.ToImmutableDictionary());
                    interfacePropertiesBuilder.Add(interfaceType, properties);
                }

                // Create new grain manifest with server data
                _localGrainManifest = new GrainManifest(
                    grainPropertiesBuilder.ToImmutable(),
                    interfacePropertiesBuilder.ToImmutable());

                // Update cluster manifest
                var siloAddress = _localClientDetails.ClientAddress;
                var silos = ImmutableDictionary.CreateBuilder<SiloAddress, GrainManifest>();
                silos.Add(siloAddress, _localGrainManifest);
                
                _manifest = new ClusterManifest(MajorMinorVersion.Zero, silos.ToImmutable());
                
                _logger.LogInformation("Updated manifest from server with {GrainCount} grains and {InterfaceCount} interfaces",
                    grainPropertiesBuilder.Count, interfacePropertiesBuilder.Count);
                    
                // Log some debug info about the manifest
                foreach (var kvp in serverManifest.InterfaceToGrainMappings)
                {
                    _logger.LogDebug("Interface mapping: {Interface} -> {Grain}", kvp.Key, kvp.Value);
                }
            }
        }

        private static ImmutableDictionary<GrainInterfaceType, GrainInterfaceProperties> CreateInterfaceManifest(
            IEnumerable<IGrainInterfacePropertiesProvider> propertyProviders,
            IOptions<GrainTypeOptions> grainTypeOptions,
            GrainInterfaceTypeResolver interfaceTypeResolver)
        {
            var builder = ImmutableDictionary.CreateBuilder<GrainInterfaceType, GrainInterfaceProperties>();
            foreach (var grainInterface in grainTypeOptions.Value.Interfaces)
            {
                var interfaceId = interfaceTypeResolver.GetGrainInterfaceType(grainInterface);
                var properties = new Dictionary<string, string>();
                foreach (var provider in propertyProviders)
                {
                    provider.Populate(grainInterface, interfaceId, properties);
                }

                var result = new GrainInterfaceProperties(properties.ToImmutableDictionary());
                if (builder.TryGetValue(interfaceId, out var grainInterfaceProperty))
                {
                    throw new InvalidOperationException($"An entry with the key {interfaceId} is already present."
                        + $"\nExisting: {grainInterfaceProperty.ToDetailedString()}\nTrying to add: {result.ToDetailedString()}"
                        + "\nConsider using the [GrainInterfaceType(\"name\")] attribute to give these interfaces unique names.");
                }

                builder[interfaceId] = result;
            }

            return builder.ToImmutable();
        }
    }
}