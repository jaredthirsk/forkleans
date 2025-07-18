namespace Granville.Rpc.Telemetry;

/// <summary>
/// Interface for tracking network statistics in RPC transports.
/// </summary>
public interface INetworkStatisticsTracker
{
    /// <summary>
    /// Records that a packet was sent.
    /// </summary>
    /// <param name="bytes">The number of bytes sent.</param>
    void RecordPacketSent(int bytes);
    
    /// <summary>
    /// Records that a packet was received.
    /// </summary>
    /// <param name="bytes">The number of bytes received.</param>
    void RecordPacketReceived(int bytes);
    
    /// <summary>
    /// Records network latency.
    /// </summary>
    /// <param name="latencyMs">The latency in milliseconds.</param>
    void RecordLatency(double latencyMs);
}