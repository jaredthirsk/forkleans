using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Granville.Benchmarks.Core.Metrics;

namespace Granville.Benchmarks.Core.Workloads
{
    public interface IWorkload
    {
        string Name { get; }
        string Description { get; }
        
        Task InitializeAsync(WorkloadConfiguration configuration);
        Task<WorkloadResult> RunAsync(MetricsCollector metricsCollector, CancellationToken cancellationToken);
        Task CleanupAsync();
    }
    
    public class WorkloadConfiguration
    {
        public int ClientCount { get; set; } = 100;
        public int MessageSize { get; set; } = 256;
        public int MessagesPerSecond { get; set; } = 60;
        public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(1);
        public bool UseReliableTransport { get; set; } = true;
        public string TransportType { get; set; } = "LiteNetLib";
        public Dictionary<string, object> CustomSettings { get; set; } = new();
        
        /// <summary>
        /// When true, use actual network calls instead of simulation delays
        /// </summary>
        public bool UseRawTransport { get; set; } = false;
        
        /// <summary>
        /// Server host for raw transport benchmarks
        /// </summary>
        public string ServerHost { get; set; } = "127.0.0.1";
        
        /// <summary>
        /// Server port for raw transport benchmarks
        /// </summary>
        public int ServerPort { get; set; } = 12345;
        
        /// <summary>
        /// When true, use actual network transport implementations instead of simulation
        /// </summary>
        public bool UseActualTransport { get; set; } = false;
    }
    
    public class WorkloadResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public BenchmarkMetrics Metrics { get; set; } = null!;
    }
}