using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Granville.Benchmarks.Core.Metrics;
using Microsoft.Extensions.Logging;

namespace Granville.Benchmarks.Core.Workloads
{
    public abstract class GameWorkloadBase : IWorkload
    {
        protected readonly ILogger _logger;
        protected WorkloadConfiguration _configuration = null!;
        protected List<Task> _clientTasks = new();
        
        public abstract string Name { get; }
        public abstract string Description { get; }
        
        protected GameWorkloadBase(ILogger logger)
        {
            _logger = logger;
        }
        
        public virtual Task InitializeAsync(WorkloadConfiguration configuration)
        {
            _configuration = configuration;
            _logger.LogInformation("Initializing workload {Name} with {ClientCount} clients", Name, configuration.ClientCount);
            return Task.CompletedTask;
        }
        
        public virtual async Task<WorkloadResult> RunAsync(MetricsCollector metricsCollector, CancellationToken cancellationToken)
        {
            using var clientCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            
            try
            {
                _logger.LogInformation("Starting workload {Name}", Name);
                metricsCollector.Start();
                
                // Create client tasks with their own cancellation token
                for (int i = 0; i < _configuration.ClientCount; i++)
                {
                    var clientId = i;
                    var clientTask = RunClientAsync(clientId, metricsCollector, clientCts.Token);
                    _clientTasks.Add(clientTask);
                }
                
                // Wait for duration or cancellation
                var delayTask = Task.Delay(_configuration.Duration, cancellationToken);
                var completedTask = await Task.WhenAny(delayTask, Task.WhenAll(_clientTasks));
                
                // Cancel all clients if still running
                if (completedTask == delayTask)
                {
                    _logger.LogInformation("Workload duration reached, stopping clients");
                    clientCts.Cancel(); // Actually cancel the client tasks
                }
                
                // Wait for all clients to complete (with timeout to avoid hanging)
                try
                {
                    await Task.WhenAll(_clientTasks).WaitAsync(TimeSpan.FromSeconds(30));
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("Timeout waiting for clients to stop, continuing anyway");
                }
                catch (TaskCanceledException)
                {
                    // TaskCanceledException is expected when clients are cancelled - this is normal completion
                    _logger.LogDebug("Client tasks cancelled successfully");
                }
                
                var metrics = metricsCollector.GetMetrics(
                    Name, 
                    _configuration.TransportType, 
                    _configuration.UseReliableTransport,
                    _configuration.MessageSize,
                    _configuration.ClientCount);
                
                _logger.LogInformation("Workload {Name} completed. Messages: {TotalMessages}, Success rate: {SuccessRate:P2}", 
                    Name, metrics.TotalMessages, 1 - metrics.ErrorRate);
                
                return new WorkloadResult
                {
                    Success = true,
                    Metrics = metrics
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Workload {Name} failed", Name);
                return new WorkloadResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }
        
        public virtual Task CleanupAsync()
        {
            _logger.LogInformation("Cleaning up workload {Name}", Name);
            _clientTasks.Clear();
            return Task.CompletedTask;
        }
        
        protected abstract Task RunClientAsync(int clientId, MetricsCollector metricsCollector, CancellationToken cancellationToken);
    }
}