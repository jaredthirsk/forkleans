using Shooter.Shared.RpcInterfaces;
using System.Diagnostics;

namespace Shooter.ActionServer.Services;

/// <summary>
/// Tracks network statistics for the server-side RPC connection.
/// </summary>
public class NetworkStatisticsTracker
{
    private readonly string _serverId;
    private long _packetsSent;
    private long _packetsReceived;
    private long _bytesSent;
    private long _bytesReceived;
    private readonly Queue<double> _latencyHistory = new();
    private readonly object _lock = new();
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    
    public NetworkStatisticsTracker(string serverId)
    {
        _serverId = serverId;
        
        // Simulate some initial activity for demo purposes
        // TODO: Remove this when we integrate with actual RPC transport
        SimulateNetworkActivity();
    }
    
    private async void SimulateNetworkActivity()
    {
        var random = new Random();
        while (true)
        {
            await Task.Delay(100);
            
            // Simulate packet activity
            if (random.Next(100) < 80) // 80% chance of activity
            {
                RecordPacketSent(random.Next(50, 200));
                if (random.Next(100) < 90) // 90% chance of response
                {
                    RecordPacketReceived(random.Next(100, 500));
                    RecordLatency(random.Next(5, 50));
                }
            }
        }
    }
    
    public void RecordPacketSent(int bytes)
    {
        lock (_lock)
        {
            _packetsSent++;
            _bytesSent += bytes;
        }
    }
    
    public void RecordPacketReceived(int bytes)
    {
        lock (_lock)
        {
            _packetsReceived++;
            _bytesReceived += bytes;
        }
    }
    
    public void RecordLatency(double latencyMs)
    {
        lock (_lock)
        {
            _latencyHistory.Enqueue(latencyMs);
            
            // Keep only last 100 measurements
            while (_latencyHistory.Count > 100)
            {
                _latencyHistory.Dequeue();
            }
        }
    }
    
    public NetworkStatistics GetStats()
    {
        lock (_lock)
        {
            var averageLatency = _latencyHistory.Count > 0 
                ? _latencyHistory.Average() 
                : 0.0;
            
            return new NetworkStatistics
            {
                ServerId = _serverId,
                PacketsSent = _packetsSent,
                PacketsReceived = _packetsReceived,
                BytesSent = _bytesSent,
                BytesReceived = _bytesReceived,
                AverageLatency = averageLatency,
                Timestamp = DateTime.UtcNow
            };
        }
    }
    
    public void Reset()
    {
        lock (_lock)
        {
            _packetsSent = 0;
            _packetsReceived = 0;
            _bytesSent = 0;
            _bytesReceived = 0;
            _latencyHistory.Clear();
        }
    }
}