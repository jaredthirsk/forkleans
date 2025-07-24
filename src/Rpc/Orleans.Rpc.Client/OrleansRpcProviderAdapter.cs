using System;
using Orleans.Metadata;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Serialization.TypeSystem;
using Orleans.Serialization.Configuration;
using Microsoft.Extensions.Options;

namespace Granville.Rpc
{
    /// <summary>
    /// Adapter that implements Orleans.GrainReferences.RpcProvider for compatibility.
    /// This allows Orleans' GrainReferenceActivatorProvider to work while we handle RPC-specific logic.
    /// </summary>
    internal class OrleansRpcProviderAdapter : Orleans.GrainReferences.RpcProvider
    {
        public OrleansRpcProviderAdapter(
            IOptions<TypeManifestOptions> config,
            GrainInterfaceTypeResolver resolver,
            TypeConverter typeConverter)
            : base(config, resolver, typeConverter)
        {
        }
    }
}