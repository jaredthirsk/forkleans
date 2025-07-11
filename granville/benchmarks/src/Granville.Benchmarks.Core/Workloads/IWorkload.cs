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
    }
    
    public class WorkloadResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public BenchmarkMetrics Metrics { get; set; } = null!;
    }
}