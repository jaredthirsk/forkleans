using System;
using Microsoft.Extensions.DependencyInjection;

namespace Forkleans.Rpc.Transport.Ruffles
{
    /// <summary>
    /// Factory for creating Ruffles transport instances.
    /// </summary>
    public class RufflesTransportFactory : IRpcTransportFactory
    {
        private readonly bool _isServer;

        public RufflesTransportFactory(bool isServer)
        {
            _isServer = isServer;
        }

        public IRpcTransport CreateTransport(IServiceProvider serviceProvider)
        {
            return ActivatorUtilities.CreateInstance<RufflesTransport>(serviceProvider);
        }
    }
}