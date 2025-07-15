using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Granville.Benchmarks.Core.Metrics;
using Granville.Benchmarks.Core.Workloads;

namespace Granville.Benchmarks.Runner.Services
{
    public class BenchmarkOrchestrator
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BenchmarkOrchestrator> _logger;
        private readonly BenchmarkOptions _options;
        private readonly ResultsExporter _resultsExporter;
        private readonly NetworkEmulator _networkEmulator;
        
        public BenchmarkOrchestrator(
            IServiceProvider serviceProvider,
            ILogger<BenchmarkOrchestrator> logger,
            IOptions<BenchmarkOptions> options,
            ResultsExporter resultsExporter,
            NetworkEmulator networkEmulator)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _options = options.Value;
            _resultsExporter = resultsExporter;
            _networkEmulator = networkEmulator;
        }
        
        public async Task RunBenchmarksAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting benchmark orchestration");
            
            var allResults = new List<BenchmarkResult>();
            var workloads = _serviceProvider.GetServices<IWorkload>().ToList();
            
            _logger.LogInformation("Found {Count} workloads to run", workloads.Count);
            
            foreach (var workload in workloads)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                    
                try
                {
                    var results = await RunWorkloadBenchmarksAsync(workload, cancellationToken);
                    allResults.AddRange(results);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to run workload {Workload}", workload.Name);
                }
            }
            
            // Export results
            await _resultsExporter.ExportResultsAsync(allResults, cancellationToken);
            
            _logger.LogInformation("Benchmark orchestration completed. Total results: {Count}", allResults.Count);
        }
        
        private async Task<List<BenchmarkResult>> RunWorkloadBenchmarksAsync(IWorkload workload, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Running benchmarks for workload: {Workload}", workload.Name);
            
            var results = new List<BenchmarkResult>();
            
            foreach (var transportConfig in _options.Transports)
            {
                foreach (var networkCondition in _options.NetworkConditions)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                        
                    var result = await RunSingleBenchmarkAsync(
                        workload, 
                        transportConfig, 
                        networkCondition, 
                        cancellationToken);
                        
                    if (result != null)
                    {
                        results.Add(result);
                    }
                }
            }
            
            return results;
        }
        
        private async Task<BenchmarkResult?> RunSingleBenchmarkAsync(
            IWorkload workload,
            TransportConfiguration transportConfig,
            NetworkCondition networkCondition,
            CancellationToken cancellationToken)
        {
            var testName = $"{workload.Name}_{transportConfig.Type}_{networkCondition.Name}";
            _logger.LogInformation("Starting benchmark: {TestName}", testName);
            
            try
            {
                // Apply network conditions
                if (networkCondition.LatencyMs > 0 || networkCondition.PacketLoss > 0)
                {
                    await _networkEmulator.ApplyConditionsAsync(networkCondition);
                }
                
                // Initialize workload
                var workloadConfig = new WorkloadConfiguration
                {
                    ClientCount = _options.ClientCount,
                    MessageSize = _options.MessageSize,
                    MessagesPerSecond = _options.MessagesPerSecond,
                    Duration = _options.TestDuration,
                    UseReliableTransport = transportConfig.Reliable,
                    TransportType = transportConfig.Type,
                    CustomSettings = transportConfig.Settings,
                    UseRawTransport = _options.UseRawTransport,
                    ServerHost = _options.ServerHost,
                    ServerPort = _options.ServerPort
                };
                
                await workload.InitializeAsync(workloadConfig);
                
                // Warmup
                _logger.LogInformation("Warming up for {Duration}", _options.WarmupDuration);
                await Task.Delay(_options.WarmupDuration, cancellationToken);
                
                // Run benchmark
                var metricsCollector = new MetricsCollector();
                var workloadResult = await workload.RunAsync(metricsCollector, cancellationToken);
                
                if (!workloadResult.Success)
                {
                    _logger.LogWarning("Workload failed: {Error}", workloadResult.ErrorMessage);
                    return null;
                }
                
                // Cooldown
                _logger.LogInformation("Cooling down for {Duration}", _options.CooldownDuration);
                await Task.Delay(_options.CooldownDuration, cancellationToken);
                
                // Cleanup
                await workload.CleanupAsync();
                
                // Clear network conditions
                if (networkCondition.LatencyMs > 0 || networkCondition.PacketLoss > 0)
                {
                    await _networkEmulator.ClearConditionsAsync();
                }
                
                var result = new BenchmarkResult
                {
                    TestName = testName,
                    WorkloadName = workload.Name,
                    TransportType = transportConfig.Type,
                    IsReliable = transportConfig.Reliable,
                    NetworkCondition = networkCondition.Name,
                    Metrics = workloadResult.Metrics,
                    Timestamp = DateTime.UtcNow
                };
                
                _logger.LogInformation("Completed benchmark: {TestName}. Messages: {Messages}, Latency: {Latency:F2}ms", 
                    testName, result.Metrics.TotalMessages, result.Metrics.AverageLatency / 1000);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Benchmark failed: {TestName}", testName);
                return null;
            }
        }
    }
    
    public class BenchmarkOptions
    {
        public int ClientCount { get; set; } = 100;
        public int MessageSize { get; set; } = 256;
        public int MessagesPerSecond { get; set; } = 60;
        public TimeSpan WarmupDuration { get; set; } = TimeSpan.FromSeconds(10);
        public TimeSpan TestDuration { get; set; } = TimeSpan.FromMinutes(1);
        public TimeSpan CooldownDuration { get; set; } = TimeSpan.FromSeconds(5);
        public List<TransportConfiguration> Transports { get; set; } = new();
        public List<NetworkCondition> NetworkConditions { get; set; } = new();
        
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
    }
    
    public class TransportConfiguration
    {
        public string Type { get; set; } = "";
        public bool Reliable { get; set; }
        public Dictionary<string, object> Settings { get; set; } = new();
    }
    
    public class NetworkCondition
    {
        public string Name { get; set; } = "default";
        public int LatencyMs { get; set; }
        public int JitterMs { get; set; }
        public double PacketLoss { get; set; }
        public long Bandwidth { get; set; }
    }
    
    public class BenchmarkResult
    {
        public string TestName { get; set; } = "";
        public string WorkloadName { get; set; } = "";
        public string TransportType { get; set; } = "";
        public bool IsReliable { get; set; }
        public string NetworkCondition { get; set; } = "";
        public BenchmarkMetrics Metrics { get; set; } = null!;
        public DateTime Timestamp { get; set; }
    }
}