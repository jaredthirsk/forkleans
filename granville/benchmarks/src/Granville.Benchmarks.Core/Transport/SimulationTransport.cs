using System;
using System.Threading;
using System.Threading.Tasks;

namespace Granville.Benchmarks.Core.Transport
{
    /// <summary>
    /// Simulation transport that mimics network behavior with delays
    /// </summary>
    public class SimulationTransport : IRawTransport
    {
        private readonly Random _random = new();
        private RawTransportConfig _config = null!;
        private bool _disposed = false;
        
        public async Task InitializeAsync(RawTransportConfig config)
        {
            _config = config;
            // Simulate connection setup time
            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }
        
        public async Task<RawTransportResult> SendAsync(byte[] data, bool reliable, CancellationToken cancellationToken)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SimulationTransport));
                
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            try
            {
                // Simulate network latency based on transport type
                var baseLatency = _config.TransportType switch
                {
                    "LiteNetLib" => reliable ? 2.0 : 1.5,
                    "Ruffles" => reliable ? 2.2 : 1.8,
                    "Orleans.TCP" => 3.0,
                    _ => 2.0
                };
                
                // Add random jitter (0-2ms)
                var jitter = _random.NextDouble() * 2.0;
                var totalLatency = baseLatency + jitter;
                
                // Simulate packet loss (higher for unreliable)
                var lossRate = reliable ? 0.001 : 0.01; // 0.1% vs 1%
                if (_random.NextDouble() < lossRate)
                {
                    return new RawTransportResult
                    {
                        Success = false,
                        ErrorMessage = "Simulated packet loss",
                        LatencyMicroseconds = stopwatch.Elapsed.TotalMicroseconds,
                        BytesSent = data.Length,
                        BytesReceived = 0
                    };
                }
                
                // Simulate the network delay
                await Task.Delay(TimeSpan.FromMilliseconds(totalLatency), cancellationToken);
                
                // Simulate response size (ACK or response data)
                var responseSize = reliable ? 64 : 32;
                
                return new RawTransportResult
                {
                    Success = true,
                    LatencyMicroseconds = stopwatch.Elapsed.TotalMicroseconds,
                    BytesSent = data.Length,
                    BytesReceived = responseSize
                };
            }
            catch (TaskCanceledException)
            {
                return new RawTransportResult
                {
                    Success = false,
                    ErrorMessage = "Operation cancelled",
                    LatencyMicroseconds = stopwatch.Elapsed.TotalMicroseconds,
                    BytesSent = data.Length,
                    BytesReceived = 0
                };
            }
            catch (Exception ex)
            {
                return new RawTransportResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    LatencyMicroseconds = stopwatch.Elapsed.TotalMicroseconds,
                    BytesSent = data.Length,
                    BytesReceived = 0
                };
            }
        }
        
        public async Task CloseAsync()
        {
            // Simulate connection teardown time
            await Task.Delay(TimeSpan.FromMilliseconds(5));
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                // Cleanup resources
            }
        }
    }
}