using System;

namespace Forkleans.Rpc.Transport
{
    /// <summary>
    /// Default implementation of IRpcTransportFactory that throws an exception.
    /// </summary>
    public class DefaultRpcTransportFactory : IRpcTransportFactory
    {
        public IRpcTransport CreateTransport(IServiceProvider serviceProvider)
        {
            throw new InvalidOperationException(
                "No RPC transport has been configured. Please configure a transport using UseLiteNetLib() or another transport extension method.");
        }
    }
}