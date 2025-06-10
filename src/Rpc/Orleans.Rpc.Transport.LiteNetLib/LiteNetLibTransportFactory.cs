using System;
using Microsoft.Extensions.DependencyInjection;

namespace Forkleans.Rpc.Transport.LiteNetLib
{
    /// <summary>
    /// Factory for creating LiteNetLib transport instances.
    /// </summary>
    public class LiteNetLibTransportFactory : IRpcTransportFactory
    {
        private readonly bool _isServer;

        public LiteNetLibTransportFactory(bool isServer = true)
        {
            _isServer = isServer;
        }

        public IRpcTransport CreateTransport(IServiceProvider serviceProvider)
        {
            if (_isServer)
            {
                return ActivatorUtilities.CreateInstance<LiteNetLibTransport>(serviceProvider);
            }
            else
            {
                return ActivatorUtilities.CreateInstance<LiteNetLibClientTransport>(serviceProvider);
            }
        }
    }
}