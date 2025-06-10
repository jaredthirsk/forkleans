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

namespace Forkleans.Rpc.Hosting
{
    /// <summary>
    /// Simplified <see cref="IClusterManifestProvider"/> implementation for RPC clients.
    /// This version provides only local grain manifest without cluster synchronization.
    /// </summary>
    internal class RpcClientManifestProvider : IClusterManifestProvider
    {
        private readonly ClusterManifest _manifest;
        private readonly AsyncEnumerable<ClusterManifest> _updates;
        private readonly GrainManifest _localGrainManifest;

        public RpcClientManifestProvider(
            IEnumerable<IGrainInterfacePropertiesProvider> grainInterfacePropertiesProviders,
            IOptions<GrainTypeOptions> grainTypeOptions,
            GrainInterfaceTypeResolver interfaceTypeResolver,
            ILocalClientDetails localClientDetails)
        {
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
        public ClusterManifest Current => _manifest;

        /// <inheritdoc />
        public IAsyncEnumerable<ClusterManifest> Updates => _updates;

        /// <inheritdoc />
        public GrainManifest LocalGrainManifest => _localGrainManifest;

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