using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Granville.Benchmarks.Runner.Services
{
    public class ResultsExporter
    {
        private readonly ILogger<ResultsExporter> _logger;
        private readonly string _resultsPath;
        
        public ResultsExporter(ILogger<ResultsExporter> logger)
        {
            _logger = logger;
            _resultsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "results");
            Directory.CreateDirectory(_resultsPath);
        }
        
        public async Task ExportResultsAsync(List<BenchmarkResult> results, CancellationToken cancellationToken)
        {
            if (!results.Any())
            {
                _logger.LogWarning("No results to export");
                return;
            }
            
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var baseFileName = $"benchmark_results_{timestamp}";
            
            // Export to multiple formats
            await ExportJsonAsync(results, baseFileName, cancellationToken);
            await ExportCsvAsync(results, baseFileName, cancellationToken);
            await ExportSummaryAsync(results, baseFileName, cancellationToken);
            
            _logger.LogInformation("Results exported to {Path}", _resultsPath);
        }
        
        private async Task ExportJsonAsync(List<BenchmarkResult> results, string baseFileName, CancellationToken cancellationToken)
        {
            var jsonPath = Path.Combine(_resultsPath, $"{baseFileName}.json");
            var json = JsonConvert.SerializeObject(results, Formatting.Indented);
            await File.WriteAllTextAsync(jsonPath, json, cancellationToken);
            _logger.LogInformation("JSON results exported to {Path}", jsonPath);
        }
        
        private async Task ExportCsvAsync(List<BenchmarkResult> results, string baseFileName, CancellationToken cancellationToken)
        {
            var csvPath = Path.Combine(_resultsPath, $"{baseFileName}.csv");
            
            using var writer = new StreamWriter(csvPath);
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            
            // Write headers
            await csv.WriteRecordsAsync(results.Select(r => new
            {
                r.TestName,
                r.WorkloadName,
                r.TransportType,
                r.IsReliable,
                r.NetworkCondition,
                r.Timestamp,
                r.Metrics.TotalMessages,
                r.Metrics.MessagesPerSecond,
                r.Metrics.AverageLatency,
                r.Metrics.MedianLatency,
                r.Metrics.P95Latency,
                r.Metrics.P99Latency,
                r.Metrics.MinLatency,
                r.Metrics.MaxLatency,
                r.Metrics.SuccessfulCalls,
                r.Metrics.FailedCalls,
                r.Metrics.TimeoutCalls,
                r.Metrics.ErrorRate,
                r.Metrics.PacketLossRate,
                r.Metrics.BytesPerSecond,
                r.Metrics.AverageCpuUsage,
                r.Metrics.PeakMemoryUsage,
                r.Metrics.Gen0Collections,
                r.Metrics.Gen1Collections,
                r.Metrics.Gen2Collections
            }), cancellationToken);
            
            _logger.LogInformation("CSV results exported to {Path}", csvPath);
        }
        
        private async Task ExportSummaryAsync(List<BenchmarkResult> results, string baseFileName, CancellationToken cancellationToken)
        {
            var summaryPath = Path.Combine(_resultsPath, $"{baseFileName}_summary.md");
            
            using var writer = new StreamWriter(summaryPath);
            
            await writer.WriteLineAsync("# Granville RPC Benchmark Results Summary");
            await writer.WriteLineAsync($"\nGenerated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            await writer.WriteLineAsync($"\nTotal benchmark runs: {results.Count}");
            
            // Group by workload
            var workloadGroups = results.GroupBy(r => r.WorkloadName);
            
            foreach (var workloadGroup in workloadGroups)
            {
                await writer.WriteLineAsync($"\n## {workloadGroup.Key}");
                
                // Create comparison table
                await writer.WriteLineAsync("\n| Transport | Reliable | Network | Avg Latency (ms) | P99 Latency (ms) | Throughput (msg/s) | Error Rate | Packet Loss |");
                await writer.WriteLineAsync("|-----------|----------|---------|------------------|------------------|-------------------|------------|-------------|");
                
                foreach (var result in workloadGroup.OrderBy(r => r.TransportType).ThenBy(r => r.NetworkCondition))
                {
                    var avgLatency = result.Metrics.AverageLatency / 1000; // Convert to ms
                    var p99Latency = result.Metrics.P99Latency / 1000;
                    
                    await writer.WriteLineAsync($"| {result.TransportType} | {result.IsReliable} | {result.NetworkCondition} | " +
                        $"{avgLatency:F2} | {p99Latency:F2} | {result.Metrics.MessagesPerSecond:F0} | " +
                        $"{result.Metrics.ErrorRate:P2} | {result.Metrics.PacketLossRate:P2} |");
                }
                
                // Add key findings
                await writer.WriteLineAsync("\n### Key Findings:");
                
                var bestLatency = workloadGroup.OrderBy(r => r.Metrics.AverageLatency).First();
                await writer.WriteLineAsync($"- Best latency: {bestLatency.TransportType} " +
                    $"({bestLatency.Metrics.AverageLatency / 1000:F2}ms average)");
                
                var bestThroughput = workloadGroup.OrderByDescending(r => r.Metrics.MessagesPerSecond).First();
                await writer.WriteLineAsync($"- Best throughput: {bestThroughput.TransportType} " +
                    $"({bestThroughput.Metrics.MessagesPerSecond:F0} messages/sec)");
                
                var mostReliable = workloadGroup.OrderBy(r => r.Metrics.ErrorRate).First();
                await writer.WriteLineAsync($"- Most reliable: {mostReliable.TransportType} " +
                    $"({mostReliable.Metrics.ErrorRate:P2} error rate)");
            }
            
            // Add recommendations
            await writer.WriteLineAsync("\n## Recommendations");
            await writer.WriteLineAsync("\nBased on the benchmark results:");
            
            // Analyze UDP vs TCP
            var udpResults = results.Where(r => r.TransportType != "Orleans.TCP").ToList();
            var tcpResults = results.Where(r => r.TransportType == "Orleans.TCP").ToList();
            
            if (udpResults.Any() && tcpResults.Any())
            {
                var avgUdpLatency = udpResults.Average(r => r.Metrics.AverageLatency) / 1000;
                var avgTcpLatency = tcpResults.Average(r => r.Metrics.AverageLatency) / 1000;
                var latencyImprovement = (avgTcpLatency - avgUdpLatency) / avgTcpLatency * 100;
                
                await writer.WriteLineAsync($"\n- UDP transports show {latencyImprovement:F1}% lower latency compared to TCP");
            }
            
            _logger.LogInformation("Summary exported to {Path}", summaryPath);
        }
    }
}