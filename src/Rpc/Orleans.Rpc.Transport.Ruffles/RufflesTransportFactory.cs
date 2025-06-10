using System;
using Microsoft.Extensions.DependencyInjection;

namespace Forkleans.Rpc.Transport.Ruffles
{
    /// <summary>
    /// Factory for creating Ruffles transport instances.
    /// </summary>
    public class RufflesTransportFactory : IRpcTransportFactory
    {
        public IRpcTransport CreateTransport(IServiceProvider serviceProvider)
        {
            return ActivatorUtilities.CreateInstance<RufflesTransport>(serviceProvider);
        }
    }
}