using System;

namespace Granville.Benchmarks.Core.Transport
{
    /// <summary>
    /// Factory for creating transport instances
    /// </summary>
    public static class TransportFactory
    {
        public static IRawTransport CreateTransport(RawTransportConfig config)
        {
            // For now, always return simulation transport
            // TODO: Add actual transport implementations for LiteNetLib, Ruffles, Orleans.TCP
            return new SimulationTransport();
        }
        
        public static IRawTransport CreateSimulationTransport()
        {
            return new SimulationTransport();
        }
        
        // TODO: Add these methods when implementing actual transports
        /*
        public static IRawTransport CreateLiteNetLibTransport()
        {
            return new LiteNetLibTransport();
        }
        
        public static IRawTransport CreateRufflesTransport()
        {
            return new RufflesTransport();
        }
        
        public static IRawTransport CreateOrleansTcpTransport()
        {
            return new OrleansTcpTransport();
        }
        */
    }
}