using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Granville.Benchmarks.Core.Transport
{
    /// <summary>
    /// Factory for creating transport instances
    /// </summary>
    public static class TransportFactory
    {
        public static IRawTransport CreateTransport(RawTransportConfig config, IServiceProvider serviceProvider, bool useActualTransport = false)
        {
            if (!useActualTransport)
            {
                // Use simulation transport for backward compatibility
                return new SimulationTransport();
            }
            
            return config.TransportType switch
            {
                "LiteNetLib" => CreateLiteNetLibTransport(serviceProvider),
                "Ruffles" => CreateRufflesTransport(serviceProvider),
                "Orleans.TCP" => throw new NotImplementedException("Orleans.TCP raw transport not yet implemented"),
                _ => throw new ArgumentException($"Unsupported transport type: {config.TransportType}")
            };
        }
        
        public static IRawTransport CreateSimulationTransport()
        {
            return new SimulationTransport();
        }
        
        public static IRawTransport CreateLiteNetLibTransport(IServiceProvider serviceProvider)
        {
            var logger = serviceProvider.GetRequiredService<ILogger<LiteNetLibRawTransport>>();
            return new LiteNetLibRawTransport(logger);
        }
        
        public static IRawTransport CreateRufflesTransport(IServiceProvider serviceProvider)
        {
            throw new NotImplementedException("Ruffles raw transport not yet implemented");
        }
        
        // Future: Orleans.TCP raw transport implementation
        public static IRawTransport CreateOrleansTcpTransport(IServiceProvider serviceProvider)
        {
            throw new NotImplementedException("Orleans.TCP raw transport not yet implemented");
        }
    }
}