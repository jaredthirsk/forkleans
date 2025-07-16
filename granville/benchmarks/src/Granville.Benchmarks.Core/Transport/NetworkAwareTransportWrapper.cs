using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Granville.Benchmarks.Runner.Services;
using Microsoft.Extensions.Logging;

namespace Granville.Benchmarks.Core.Transport
{
    /// <summary>
    /// Wraps any IRawTransport to apply network conditions at the application level.
    /// This allows testing network conditions without requiring system-level tools.
    /// </summary>
    public class NetworkAwareTransportWrapper : IRawTransport
    {
        private readonly IRawTransport _innerTransport;
        private readonly NetworkEmulator _networkEmulator;
        private readonly ILogger<NetworkAwareTransportWrapper> _logger;
        private readonly ConcurrentDictionary<string, BandwidthTracker> _bandwidthTrackers = new();
        
        public string TransportName => $"{_innerTransport.TransportName} (Network-Aware)";
        public bool IsReliable => _innerTransport.IsReliable;
        
        public NetworkAwareTransportWrapper(
            IRawTransport innerTransport, 
            NetworkEmulator networkEmulator,
            ILogger<NetworkAwareTransportWrapper> logger)
        {
            _innerTransport = innerTransport;
            _networkEmulator = networkEmulator;
            _logger = logger;
            
            // Subscribe to inner transport events
            _innerTransport.OnMessageReceived += HandleIncomingMessage;
        }
        
        public event Action<byte[], string>? OnMessageReceived;
        
        public async Task ConnectAsync(RawTransportConfig config)
        {
            await _innerTransport.ConnectAsync(config);
        }
        
        public async Task<RawTransportResult> SendAsync(byte[] data, string targetId)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                // Check packet loss
                if (_networkEmulator.ShouldDropPacket())
                {
                    _logger.LogDebug("Packet dropped due to simulated packet loss");
                    return new RawTransportResult
                    {
                        Success = false,
                        LatencyMs = 0,
                        Error = "Packet dropped (simulated)"
                    };
                }
                
                // Check bandwidth limit
                var tracker = _bandwidthTrackers.GetOrAdd(targetId, _ => new BandwidthTracker());
                if (!CheckBandwidthLimit(data.Length, tracker))
                {
                    _logger.LogDebug("Packet dropped due to bandwidth limit");
                    return new RawTransportResult
                    {
                        Success = false,
                        LatencyMs = 0,
                        Error = "Bandwidth limit exceeded"
                    };
                }
                
                // Apply latency
                var latencyMs = _networkEmulator.GetSimulatedLatencyMs();
                if (latencyMs > 0)
                {
                    await Task.Delay(latencyMs);
                }
                
                // Send through inner transport
                var result = await _innerTransport.SendAsync(data, targetId);
                
                // Update result with total latency
                if (result.Success)
                {
                    result.LatencyMs = stopwatch.ElapsedMilliseconds;
                    tracker.RecordBytes(data.Length);
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in network-aware send");
                return new RawTransportResult
                {
                    Success = false,
                    LatencyMs = stopwatch.ElapsedMilliseconds,
                    Error = ex.Message
                };
            }
        }
        
        public RawTransportStats GetStats()
        {
            var innerStats = _innerTransport.GetStats();
            var condition = _networkEmulator.GetCurrentCondition();
            
            if (condition != null)
            {
                // Enhance stats with network condition info
                innerStats.CustomMetrics["NetworkProfile"] = condition.Name;
                innerStats.CustomMetrics["SimulatedLatencyMs"] = condition.LatencyMs;
                innerStats.CustomMetrics["SimulatedJitterMs"] = condition.JitterMs;
                innerStats.CustomMetrics["SimulatedPacketLoss"] = condition.PacketLoss;
                innerStats.CustomMetrics["SimulatedBandwidth"] = condition.Bandwidth;
            }
            
            return innerStats;
        }
        
        public void Dispose()
        {
            _innerTransport.OnMessageReceived -= HandleIncomingMessage;
            _innerTransport.Dispose();
        }
        
        private void HandleIncomingMessage(byte[] data, string senderId)
        {
            // Apply network conditions to incoming messages too
            Task.Run(async () =>
            {
                try
                {
                    // Check packet loss for incoming
                    if (_networkEmulator.ShouldDropPacket())
                    {
                        _logger.LogDebug("Incoming packet dropped due to simulated packet loss");
                        return;
                    }
                    
                    // Apply latency to incoming
                    var latencyMs = _networkEmulator.GetSimulatedLatencyMs();
                    if (latencyMs > 0)
                    {
                        await Task.Delay(latencyMs);
                    }
                    
                    // Forward to subscribers
                    OnMessageReceived?.Invoke(data, senderId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling incoming message with network conditions");
                }
            });
        }
        
        private bool CheckBandwidthLimit(int bytes, BandwidthTracker tracker)
        {
            var condition = _networkEmulator.GetCurrentCondition();
            if (condition?.Bandwidth <= 0)
                return true; // No limit
                
            return tracker.CanSend(bytes, condition.Bandwidth);
        }
        
        /// <summary>
        /// Tracks bandwidth usage using a sliding window.
        /// </summary>
        private class BandwidthTracker
        {
            private readonly object _lock = new();
            private readonly Queue<(DateTime time, int bytes)> _sentData = new();
            private readonly TimeSpan _windowSize = TimeSpan.FromSeconds(1);
            
            public bool CanSend(int bytes, long bandwidthBitsPerSec)
            {
                lock (_lock)
                {
                    var now = DateTime.UtcNow;
                    var windowStart = now - _windowSize;
                    
                    // Remove old entries
                    while (_sentData.Count > 0 && _sentData.Peek().time < windowStart)
                    {
                        _sentData.Dequeue();
                    }
                    
                    // Calculate current usage
                    var currentBytes = 0;
                    foreach (var (_, b) in _sentData)
                    {
                        currentBytes += b;
                    }
                    
                    // Check if we can send
                    var maxBytesPerWindow = (bandwidthBitsPerSec / 8) * _windowSize.TotalSeconds;
                    return currentBytes + bytes <= maxBytesPerWindow;
                }
            }
            
            public void RecordBytes(int bytes)
            {
                lock (_lock)
                {
                    _sentData.Enqueue((DateTime.UtcNow, bytes));
                }
            }
        }
    }
}