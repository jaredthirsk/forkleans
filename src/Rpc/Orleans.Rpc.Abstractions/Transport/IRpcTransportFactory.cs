using System;

namespace Granville.Rpc.Transport
{
    /// <summary>
    /// Factory for creating RPC transport instances.
    /// </summary>
    public interface IRpcTransportFactory
    {
        /// <summary>
        /// Creates a new RPC transport instance.
        /// </summary>
        IRpcTransport CreateTransport(IServiceProvider serviceProvider);
    }
}