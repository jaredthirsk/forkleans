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
using Granville.Benchmarks.Core.Transport;

namespace Granville.Benchmarks.Runner.Services
{
    public class BenchmarkOrchestrator
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BenchmarkOrchestrator> _logger;
        private readonly BenchmarkOptions _options;
        private readonly ResultsExporter _resultsExporter;
        private readonly NetworkEmulator _networkEmulator;
        private BenchmarkUdpServer? _benchmarkServer;
        
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
            
            try
            {
                // Start benchmark servers if using actual transport
                if (_options.UseRawTransport && _options.UseActualTransport)
                {
                    await StartBenchmarkServersAsync(cancellationToken);
                }
                
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
            finally
            {
                // Stop benchmark servers
                if (_benchmarkServer != null)
                {
                    await StopBenchmarkServersAsync();
                }
            }
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
                var serverPort = _options.UseActualTransport ? 
                    _options.ServerPort + GetTransportPortOffset(transportConfig.Type) : 
                    _options.ServerPort;
                    
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
                    UseActualTransport = _options.UseActualTransport,
                    ServerHost = _options.ServerHost,
                    ServerPort = serverPort
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
        
        private async Task StartBenchmarkServersAsync(CancellationToken cancellationToken)
        {
            var serverLogger = _serviceProvider.GetRequiredService<ILogger<BenchmarkUdpServer>>();
            _benchmarkServer = new BenchmarkUdpServer(serverLogger);
            
            // Start servers for each unique transport type
            var transportTypes = _options.Transports.Select(t => t.Type).Distinct();
            
            foreach (var transportType in transportTypes)
            {
                // Use a unique port for each transport type to avoid conflicts
                var port = _options.ServerPort + GetTransportPortOffset(transportType);
                
                try
                {
                    await _benchmarkServer.StartAsync(transportType, port, cancellationToken);
                    _logger.LogInformation("Started benchmark server for {TransportType} on port {Port}", transportType, port);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to start benchmark server for {TransportType} on port {Port}", transportType, port);
                    throw;
                }
            }
            
            // Give servers time to start
            await Task.Delay(1000, cancellationToken);
        }
        
        private async Task StopBenchmarkServersAsync()
        {
            if (_benchmarkServer != null)
            {
                try
                {
                    await _benchmarkServer.StopAllAsync();
                    _benchmarkServer.Dispose();
                    _benchmarkServer = null;
                    _logger.LogInformation("Stopped all benchmark servers");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error stopping benchmark servers");
                }
            }
        }
        
        private static int GetTransportPortOffset(string transportType)
        {
            return transportType switch
            {
                "LiteNetLib" => 0,
                "Ruffles" => 1,
                "Orleans.TCP" => 2,
                _ => 0
            };
        }
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