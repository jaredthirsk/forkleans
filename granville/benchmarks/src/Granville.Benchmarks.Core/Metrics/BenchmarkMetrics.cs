using System;
using System.Collections.Generic;

namespace Granville.Benchmarks.Core.Metrics
{
    public class BenchmarkMetrics
    {
        public string TestName { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
        
        // Latency metrics (in microseconds)
        public double AverageLatency { get; set; }
        public double MedianLatency { get; set; }
        public double P95Latency { get; set; }
        public double P99Latency { get; set; }
        public double MinLatency { get; set; }
        public double MaxLatency { get; set; }
        
        // Throughput metrics
        public long TotalMessages { get; set; }
        public double MessagesPerSecond { get; set; }
        public double BytesPerSecond { get; set; }
        
        // Resource usage
        public double AverageCpuUsage { get; set; }
        public long PeakMemoryUsage { get; set; }
        public long TotalAllocations { get; set; }
        public int Gen0Collections { get; set; }
        public int Gen1Collections { get; set; }
        public int Gen2Collections { get; set; }
        
        // Network metrics
        public long PacketsSent { get; set; }
        public long PacketsReceived { get; set; }
        public long PacketsLost { get; set; }
        public double PacketLossRate => PacketsSent > 0 ? (double)PacketsLost / PacketsSent : 0;
        public long BytesSent { get; set; }
        public long BytesReceived { get; set; }
        
        // Error metrics
        public long SuccessfulCalls { get; set; }
        public long FailedCalls { get; set; }
        public long TimeoutCalls { get; set; }
        public double ErrorRate => TotalMessages > 0 ? (double)FailedCalls / TotalMessages : 0;
        
        // Configuration
        public string TransportType { get; set; } = string.Empty;
        public bool IsReliable { get; set; }
        public int MessageSize { get; set; }
        public int ConcurrentClients { get; set; }
        public Dictionary<string, object> CustomMetrics { get; set; } = new();
    }
}