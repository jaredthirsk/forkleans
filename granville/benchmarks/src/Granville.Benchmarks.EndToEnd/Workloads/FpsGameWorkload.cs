using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Granville.Benchmarks.Core.Metrics;
using Granville.Benchmarks.Core.Workloads;
using Microsoft.Extensions.Logging;

namespace Granville.Benchmarks.EndToEnd.Workloads
{
    public class FpsGameWorkload : GameWorkloadBase
    {
        public override string Name => "FPS Game Simulation";
        public override string Description => "Simulates an FPS game with high-frequency position updates and low-latency requirements";
        
        private readonly Random _random = new();
        
        public FpsGameWorkload(ILogger<FpsGameWorkload> logger) : base(logger)
        {
        }
        
        protected override async Task RunClientAsync(int clientId, MetricsCollector metricsCollector, CancellationToken cancellationToken)
        {
            var updateInterval = TimeSpan.FromMilliseconds(1000.0 / _configuration.MessagesPerSecond);
            var position = new Vector3(
                _random.NextSingle() * 1000,
                _random.NextSingle() * 100,
                _random.NextSingle() * 1000
            );
            var velocity = new Vector3(
                (_random.NextSingle() - 0.5f) * 10,
                0,
                (_random.NextSingle() - 0.5f) * 10
            );
            
            _logger.LogDebug("Client {ClientId} starting at position {Position}", clientId, position);
            
            while (!cancellationToken.IsCancellationRequested)
            {
                var sw = Stopwatch.StartNew();
                
                try
                {
                    // Update position
                    position = position.Add(velocity.Scale((float)updateInterval.TotalSeconds));
                    
                    // Simulate sending position update
                    var payload = SerializePositionUpdate(clientId, position, velocity);
                    metricsCollector.RecordBytesSent(payload.Length);
                    metricsCollector.RecordPacketSent();
                    
                    // TODO: Actual RPC call here
                    await SimulateNetworkCall(cancellationToken);
                    
                    metricsCollector.RecordSuccess();
                    metricsCollector.RecordBytesReceived(64); // Simulated ACK
                    metricsCollector.RecordPacketReceived();
                    
                    var latencyMicros = sw.Elapsed.TotalMicroseconds;
                    metricsCollector.RecordLatency(latencyMicros);
                    
                    // Occasionally change direction
                    if (_random.NextDouble() < 0.1)
                    {
                        velocity = new Vector3(
                            (_random.NextSingle() - 0.5f) * 10,
                            0,
                            (_random.NextSingle() - 0.5f) * 10
                        );
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (TimeoutException)
                {
                    metricsCollector.RecordTimeout();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Client {ClientId} encountered error", clientId);
                    metricsCollector.RecordFailure();
                }
                
                // Wait for next update
                var elapsed = sw.Elapsed;
                if (elapsed < updateInterval)
                {
                    await Task.Delay(updateInterval - elapsed, cancellationToken);
                }
            }
            
            _logger.LogDebug("Client {ClientId} stopped", clientId);
        }
        
        private async Task SimulateNetworkCall(CancellationToken cancellationToken)
        {
            // TODO: Replace with actual RPC call
            var latency = _random.NextDouble() * 5 + 1; // 1-6ms latency
            await Task.Delay(TimeSpan.FromMilliseconds(latency), cancellationToken);
            
            // Simulate packet loss
            if (_random.NextDouble() < 0.01) // 1% packet loss
            {
                throw new TimeoutException("Simulated packet loss");
            }
        }
        
        private byte[] SerializePositionUpdate(int clientId, Vector3 position, Vector3 velocity)
        {
            // Simple serialization: 4 bytes clientId + 12 bytes position + 12 bytes velocity = 28 bytes
            var buffer = new byte[28];
            BitConverter.TryWriteBytes(buffer.AsSpan(0, 4), clientId);
            BitConverter.TryWriteBytes(buffer.AsSpan(4, 4), position.X);
            BitConverter.TryWriteBytes(buffer.AsSpan(8, 4), position.Y);
            BitConverter.TryWriteBytes(buffer.AsSpan(12, 4), position.Z);
            BitConverter.TryWriteBytes(buffer.AsSpan(16, 4), velocity.X);
            BitConverter.TryWriteBytes(buffer.AsSpan(20, 4), velocity.Y);
            BitConverter.TryWriteBytes(buffer.AsSpan(24, 4), velocity.Z);
            return buffer;
        }
        
        private struct Vector3
        {
            public float X { get; }
            public float Y { get; }
            public float Z { get; }
            
            public Vector3(float x, float y, float z)
            {
                X = x;
                Y = y;
                Z = z;
            }
            
            public Vector3 Add(Vector3 other) => new(X + other.X, Y + other.Y, Z + other.Z);
            public Vector3 Scale(float scalar) => new(X * scalar, Y * scalar, Z * scalar);
            public override string ToString() => $"({X:F1}, {Y:F1}, {Z:F1})";
        }
    }
}