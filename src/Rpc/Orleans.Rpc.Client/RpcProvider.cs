using System;
using Orleans.Metadata;
using Orleans.Runtime;

namespace Granville.Rpc
{
    /// <summary>
    /// Provides information about RPC proxy types.
    /// This delegates to the Orleans RpcProvider to use Orleans-generated proxies.
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
            // Delegate to the Orleans RpcProvider which knows about generated proxies
            return _orleansProvider.TryGet(interfaceType, out proxyType);
        }
    }
}