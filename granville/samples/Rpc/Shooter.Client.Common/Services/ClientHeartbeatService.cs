using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Shooter.Client.Common.Services
{
    /// <summary>
    /// Service that monitors client responsiveness and detects hangs
    /// </summary>
    public class ClientHeartbeatService : IHostedService, IDisposable
    {
        private readonly ILogger<ClientHeartbeatService> _logger;
        private readonly Timer _heartbeatTimer;
        private readonly Timer _monitorTimer;
        private DateTime _lastHeartbeat = DateTime.UtcNow;
        private DateTime _lastLoggedActivity = DateTime.UtcNow;
        private readonly TimeSpan _hangThreshold = TimeSpan.FromSeconds(10); // Detect hangs after 10 seconds
        private readonly TimeSpan _criticalHangThreshold = TimeSpan.FromSeconds(30); // Critical after 30 seconds
        private bool _isHung = false;
        private readonly object _lock = new object();

        public ClientHeartbeatService(ILogger<ClientHeartbeatService> logger)
        {
            _logger = logger;

            // Heartbeat every second
            _heartbeatTimer = new Timer(Heartbeat, null, Timeout.InfiniteTimeSpan, TimeSpan.FromSeconds(1));

            // Monitor every 5 seconds
            _monitorTimer = new Timer(CheckResponsiveness, null, Timeout.InfiniteTimeSpan, TimeSpan.FromSeconds(5));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[HEARTBEAT] Client heartbeat service starting");
            _heartbeatTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(1));
            _monitorTimer.Change(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[HEARTBEAT] Client heartbeat service stopping");
            _heartbeatTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _monitorTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return Task.CompletedTask;
        }

        private void Heartbeat(object? state)
        {
            try
            {
                lock (_lock)
                {
                    _lastHeartbeat = DateTime.UtcNow;

                    // Log recovery if we were hung
                    if (_isHung)
                    {
                        var hangDuration = DateTime.UtcNow - _lastLoggedActivity;
                        _logger.LogWarning("[HEARTBEAT] Client recovered from hang after {Seconds:F1} seconds",
                            hangDuration.TotalSeconds);
                        _isHung = false;
                    }

                    _lastLoggedActivity = DateTime.UtcNow;
                }

                // Log heartbeat periodically (every 30 seconds)
                if (DateTime.UtcNow.Second == 0 || DateTime.UtcNow.Second == 30)
                {
                    _logger.LogDebug("[HEARTBEAT] Client heartbeat - responsive");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HEARTBEAT] Error in heartbeat timer");
            }
        }

        private void CheckResponsiveness(object? state)
        {
            try
            {
                lock (_lock)
                {
                    var timeSinceLastHeartbeat = DateTime.UtcNow - _lastHeartbeat;

                    if (timeSinceLastHeartbeat > _criticalHangThreshold)
                    {
                        if (!_isHung || timeSinceLastHeartbeat.TotalSeconds % 10 < 5) // Log every 10 seconds
                        {
                            _logger.LogCritical("[HEARTBEAT] CRITICAL: Client has been unresponsive for {Seconds:F1} seconds! Possible deadlock or infinite loop.",
                                timeSinceLastHeartbeat.TotalSeconds);

                            // Log thread pool stats
                            ThreadPool.GetAvailableThreads(out int workerThreads, out int completionPortThreads);
                            ThreadPool.GetMaxThreads(out int maxWorkerThreads, out int maxCompletionPortThreads);
                            _logger.LogCritical("[HEARTBEAT] Thread pool: {WorkerThreads}/{MaxWorkerThreads} worker threads available, {CompletionPortThreads}/{MaxCompletionPortThreads} I/O threads available",
                                workerThreads, maxWorkerThreads, completionPortThreads, maxCompletionPortThreads);

                            // Log process info
                            using var process = Process.GetCurrentProcess();
                            _logger.LogCritical("[HEARTBEAT] Process threads: {ThreadCount}, Working set: {WorkingSetMB:F1} MB",
                                process.Threads.Count, process.WorkingSet64 / (1024.0 * 1024.0));
                        }
                        _isHung = true;
                    }
                    else if (timeSinceLastHeartbeat > _hangThreshold)
                    {
                        if (!_isHung)
                        {
                            _logger.LogWarning("[HEARTBEAT] WARNING: Client appears to be hanging! No heartbeat for {Seconds:F1} seconds",
                                timeSinceLastHeartbeat.TotalSeconds);

                            // Log thread pool stats for diagnostics
                            ThreadPool.GetAvailableThreads(out int workerThreads, out int completionPortThreads);
                            ThreadPool.GetMaxThreads(out int maxWorkerThreads, out int maxCompletionPortThreads);
                            _logger.LogWarning("[HEARTBEAT] Thread pool: {WorkerThreads}/{MaxWorkerThreads} worker threads, {CompletionPortThreads}/{MaxCompletionPortThreads} I/O threads",
                                workerThreads, maxWorkerThreads, completionPortThreads, maxCompletionPortThreads);
                        }
                        _isHung = true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HEARTBEAT] Error in monitor timer");
            }
        }

        public void RecordActivity(string? source = null)
        {
            lock (_lock)
            {
                _lastHeartbeat = DateTime.UtcNow;
                if (_isHung)
                {
                    var hangDuration = DateTime.UtcNow - _lastLoggedActivity;
                    _logger.LogInformation("[HEARTBEAT] Activity detected from {Source}, client responsive again after {Seconds:F1} seconds",
                        source ?? "unknown", hangDuration.TotalSeconds);
                    _isHung = false;
                }
                _lastLoggedActivity = DateTime.UtcNow;
            }
        }

        public void Dispose()
        {
            _heartbeatTimer?.Dispose();
            _monitorTimer?.Dispose();
        }
    }
}