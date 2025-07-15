using System;
using System.Threading;
using System.Threading.Tasks;

namespace Granville.Benchmarks.Core.Transport
{
    /// <summary>
    /// Interface for raw transport implementations used in benchmarks
    /// </summary>
    public interface IRawTransport : IDisposable
    {
        /// <summary>
        /// Initialize the transport with configuration
        /// </summary>
        Task InitializeAsync(RawTransportConfig config);
        
        /// <summary>
        /// Send a message and measure round-trip time
        /// </summary>
        Task<RawTransportResult> SendAsync(byte[] data, bool reliable, CancellationToken cancellationToken);
        
        /// <summary>
        /// Close the transport connection
        /// </summary>
        Task CloseAsync();
    }
    
    /// <summary>
    /// Configuration for raw transport
    /// </summary>
    public class RawTransportConfig
    {
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 12345;
        public string TransportType { get; set; } = "LiteNetLib";
        public bool UseReliableTransport { get; set; } = true;
        public int TimeoutMs { get; set; } = 5000;
    }
    
    /// <summary>
    /// Result of a raw transport operation
    /// </summary>
    public class RawTransportResult
    {
        public bool Success { get; set; }
        public double LatencyMicroseconds { get; set; }
        public int BytesSent { get; set; }
        public int BytesReceived { get; set; }
        public string? ErrorMessage { get; set; }
    }
}