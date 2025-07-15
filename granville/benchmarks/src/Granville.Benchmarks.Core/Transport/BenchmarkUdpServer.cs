using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Granville.Benchmarks.Core.Transport
{
    /// <summary>
    /// Simple UDP server for handling benchmark requests
    /// </summary>
    public class BenchmarkUdpServer : IDisposable
    {
        private readonly ILogger<BenchmarkUdpServer> _logger;
        private readonly ConcurrentDictionary<string, IBenchmarkTransportServer> _servers = new();
        private bool _disposed = false;
        
        public BenchmarkUdpServer(ILogger<BenchmarkUdpServer> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        /// <summary>
        /// Start a benchmark server for the specified transport type
        /// </summary>
        public async Task StartAsync(string transportType, int port, CancellationToken cancellationToken = default)
        {
            if (_servers.ContainsKey(transportType))
            {
                _logger.LogWarning("Server for transport {TransportType} is already running", transportType);
                return;
            }
            
            IBenchmarkTransportServer server = transportType switch
            {
                "LiteNetLib" => new LiteNetLibBenchmarkServer(_logger),
                "Ruffles" => new RufflesBenchmarkServer(_logger),
                "PureLiteNetLib" => new PureLiteNetLibBenchmarkServer(_logger),
                "PureRuffles" => new PureRufflesBenchmarkServer(_logger),
                _ => throw new ArgumentException($"Unsupported transport type: {transportType}")
            };
            
            await server.StartAsync(port, cancellationToken);
            _servers[transportType] = server;
            
            _logger.LogInformation("Started benchmark server for {TransportType} on port {Port}", transportType, port);
        }
        
        /// <summary>
        /// Stop a benchmark server for the specified transport type
        /// </summary>
        public async Task StopAsync(string transportType, CancellationToken cancellationToken = default)
        {
            if (_servers.TryRemove(transportType, out var server))
            {
                await server.StopAsync(cancellationToken);
                server.Dispose();
                _logger.LogInformation("Stopped benchmark server for {TransportType}", transportType);
            }
        }
        
        /// <summary>
        /// Stop all benchmark servers
        /// </summary>
        public async Task StopAllAsync(CancellationToken cancellationToken = default)
        {
            var stopTasks = new List<Task>();
            foreach (var kvp in _servers)
            {
                stopTasks.Add(kvp.Value.StopAsync(cancellationToken));
            }
            
            await Task.WhenAll(stopTasks);
            
            foreach (var kvp in _servers)
            {
                kvp.Value.Dispose();
            }
            
            _servers.Clear();
            _logger.LogInformation("Stopped all benchmark servers");
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    StopAllAsync().Wait(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error stopping benchmark servers during disposal");
                }
                
                _disposed = true;
            }
        }
    }
    
    /// <summary>
    /// Interface for transport-specific benchmark servers
    /// </summary>
    public interface IBenchmarkTransportServer : IDisposable
    {
        Task StartAsync(int port, CancellationToken cancellationToken = default);
        Task StopAsync(CancellationToken cancellationToken = default);
    }
}