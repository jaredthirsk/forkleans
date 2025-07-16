using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Granville.Benchmarks.Runner.Services;

namespace Granville.Benchmarks.Core.Transport
{
    /// <summary>
    /// Factory for creating transport instances
    /// </summary>
    public static class TransportFactory
    {
        public static IRawTransport CreateTransport(RawTransportConfig config, IServiceProvider serviceProvider, bool useActualTransport = false, NetworkEmulator networkEmulator = null)
        {
            IRawTransport transport;
            
            if (!useActualTransport)
            {
                // Use simulation transport for backward compatibility
                transport = new SimulationTransport();
            }
            else
            {
                transport = config.TransportType switch
                {
                    "LiteNetLib" => CreateLiteNetLibTransport(serviceProvider),
                    "Ruffles" => CreateRufflesTransport(serviceProvider),
                    "PureLiteNetLib" => CreatePureLiteNetLibTransport(serviceProvider),
                    "PureRuffles" => CreatePureRufflesTransport(serviceProvider),
                    "Orleans.TCP" => throw new NotImplementedException("Orleans.TCP raw transport not yet implemented"),
                    _ => throw new ArgumentException($"Unsupported transport type: {config.TransportType}")
                };
            }
            
            // Wrap with network emulator if provided
            if (networkEmulator?.GetCurrentCondition() != null)
            {
                var logger = serviceProvider.GetRequiredService<ILogger<NetworkAwareTransportWrapper>>();
                transport = new NetworkAwareTransportWrapper(transport, networkEmulator, logger);
            }
            
            return transport;
        }
        
        public static IRawTransport CreateSimulationTransport()
        {
            return new SimulationTransport();
        }
        
        public static IRawTransport CreateLiteNetLibTransport(IServiceProvider serviceProvider)
        {
            var logger = serviceProvider.GetRequiredService<ILogger<LiteNetLibBypassTransport>>();
            return new LiteNetLibBypassTransport(logger);
        }
        
        public static IRawTransport CreateRufflesTransport(IServiceProvider serviceProvider)
        {
            var logger = serviceProvider.GetRequiredService<ILogger<RufflesBypassTransport>>();
            return new RufflesBypassTransport(logger);
        }
        
        public static IRawTransport CreatePureLiteNetLibTransport(IServiceProvider serviceProvider)
        {
            var logger = serviceProvider.GetRequiredService<ILogger<PureLiteNetLibTransport>>();
            return new PureLiteNetLibTransport(logger);
        }
        
        public static IRawTransport CreatePureRufflesTransport(IServiceProvider serviceProvider)
        {
            var logger = serviceProvider.GetRequiredService<ILogger<PureRufflesTransport>>();
            return new PureRufflesTransport(logger);
        }
        
        // Future: Orleans.TCP raw transport implementation
        public static IRawTransport CreateOrleansTcpTransport(IServiceProvider serviceProvider)
        {
            throw new NotImplementedException("Orleans.TCP raw transport not yet implemented");
        }
    }
}