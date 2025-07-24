using System;
using Orleans.Metadata;
using Orleans.Runtime;

namespace Granville.Rpc
{
    /// <summary>
    /// Provides information about RPC proxy types.
    /// This delegates to the Orleans RpcProvider to use Granville-generated proxies.
    /// </summary>
    internal sealed class RpcProxyProvider
    {
        private readonly Orleans.GrainReferences.RpcProvider _orleansProvider;

        public RpcProxyProvider(Orleans.GrainReferences.RpcProvider orleansProvider)
        {
            _orleansProvider = orleansProvider ?? throw new ArgumentNullException(nameof(orleansProvider));
        }

        /// <summary>
        /// Try to get the proxy type for a given interface.
        /// </summary>
        /// <param name="interfaceType">The grain interface type.</param>
        /// <param name="proxyType">The proxy type if found.</param>
        /// <returns>True if a proxy type was found, false otherwise.</returns>
        public bool TryGet(GrainInterfaceType interfaceType, out Type proxyType)
        {
            // For now, always return false to force use of RpcGrainReference
            // The Orleans-generated proxies exist but aren't properly registered
            // when Orleans code generation is disabled in the project
            proxyType = null;
            return false;
        }
    }
}