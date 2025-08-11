using Shooter.Shared.RpcInterfaces;
using System.Diagnostics;
using Granville.Rpc.Telemetry;

namespace Shooter.Client.Common;

/// <summary>
/// Tracks network statistics for the client-side RPC connection.
/// </summary>
public class NetworkStatisticsTracker : Granville.Rpc.Telemetry.INetworkStatisticsTracker
{
    private readonly string _clientId;
    private long _packetsSent;
    private long _packetsReceived;
    private long _bytesSent;
    private long _bytesReceived;
    private readonly Queue<double> _latencyHistory = new();
    private readonly object _lock = new();
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    
    public NetworkStatisticsTracker(string clientId)
    {
        _clientId = clientId;
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
                ServerId = _clientId,
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