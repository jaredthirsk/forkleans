using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Forkleans.Configuration;
using Forkleans.Metadata;
using Forkleans.Runtime;
using Forkleans.Serialization.TypeSystem;

namespace Forkleans.Rpc
{
    /// <summary>
    /// Provides grain type metadata for RPC server.
    /// </summary>
    internal sealed class RpcManifestProvider : SiloManifestProvider, IClusterManifestProvider
    {
        public RpcManifestProvider(
            IEnumerable<IGrainPropertiesProvider> grainPropertiesProviders,
            IEnumerable<IGrainInterfacePropertiesProvider> grainInterfacePropertiesProviders,
            IOptions<GrainTypeOptions> grainTypeOptions,
            GrainTypeResolver typeProvider,
            GrainInterfaceTypeResolver interfaceIdProvider,
            TypeConverter typeConverter)
            : base(grainPropertiesProviders, grainInterfacePropertiesProviders, grainTypeOptions, typeProvider, interfaceIdProvider, typeConverter)
        {
        }
        
        // IClusterManifestProvider implementation
        public ClusterManifest Current => new ClusterManifest(
            MajorMinorVersion.Zero,
            ImmutableDictionary.CreateRange(new[] { new KeyValuePair<SiloAddress, GrainManifest>(SiloAddress.Zero, SiloManifest) }));
        
        public GrainManifest LocalGrainManifest => SiloManifest;
        
        public IAsyncEnumerable<ClusterManifest> Updates => EmptyUpdates();
        
        private async IAsyncEnumerable<ClusterManifest> EmptyUpdates([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // No updates for RPC server
            await System.Threading.Tasks.Task.CompletedTask;
            yield break;
        }
    }
}