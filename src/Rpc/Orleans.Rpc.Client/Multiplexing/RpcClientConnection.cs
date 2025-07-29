using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Granville.Rpc.Configuration;
using Granville.Rpc.Hosting;

namespace Granville.Rpc.Multiplexing
{
    /// <summary>
    /// Manages a single RPC client connection to a server.
    /// </summary>
    internal sealed class RpcClientConnection : IAsyncDisposable
    {
        private readonly IServerDescriptor _serverDescriptor;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _connectionLock;
        private IRpcClient _client;
        private IHost _host;
        private ConnectionState _state;
        private DateTime _lastConnectionAttempt;
        private int _connectionFailures;
        private bool _disposed;

        public IServerDescriptor ServerDescriptor => _serverDescriptor;

        public RpcClientConnection(
            IServerDescriptor serverDescriptor,
            IServiceProvider serviceProvider,
            ILogger logger)
        {
            _serverDescriptor = serverDescriptor ?? throw new ArgumentNullException(nameof(serverDescriptor));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _connectionLock = new SemaphoreSlim(1, 1);
            _state = ConnectionState.Disconnected;
        }

        public async Task<IRpcClient> GetClientAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RpcClientConnection));
            
            if (_state == ConnectionState.Connected && _client != null)
            {
                return _client;
            }

            await EnsureConnectedAsync();
            return _client;
        }

        public async Task EnsureConnectedAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RpcClientConnection));
            
            await _connectionLock.WaitAsync();
            try
            {
                if (_state == ConnectionState.Connected && _client != null)
                {
                    return;
                }

                // Implement exponential backoff for reconnection
                if (_state == ConnectionState.Failed)
                {
                    var timeSinceLastAttempt = DateTime.UtcNow - _lastConnectionAttempt;
                    var backoffTime = TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, _connectionFailures)));
                    
                    if (timeSinceLastAttempt < backoffTime)
                    {
                        throw new InvalidOperationException(
                            $"Connection to {_serverDescriptor.ServerId} is in backoff period. " +
                            $"Next attempt in {(backoffTime - timeSinceLastAttempt).TotalSeconds:F1} seconds");
                    }
                }

                _state = ConnectionState.Connecting;
                _lastConnectionAttempt = DateTime.UtcNow;

                try
                {
                    _logger.LogInformation("Connecting to server {ServerId} at {HostName}:{Port}",
                        _serverDescriptor.ServerId, _serverDescriptor.HostName, _serverDescriptor.Port);

                    // Dispose existing client if any
                    if (_host != null)
                    {
                        try
                        {
                            await _host.StopAsync(CancellationToken.None);
                            _host.Dispose();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error disposing old host for server {ServerId}", 
                                _serverDescriptor.ServerId);
                        }
                        _host = null;
                        _client = null;
                    }

                    // Create new RpcClient with server-specific configuration
                    var hostBuilder = Host.CreateDefaultBuilder()
                        .ConfigureServices((context, services) =>
                        {
                            // Copy logging from parent
                            services.AddLogging();
                        })
                        .UseOrleansRpcClient(rpcBuilder =>
                        {
                            rpcBuilder.ConnectTo(_serverDescriptor.HostName, _serverDescriptor.Port);
                        })
                        .Build();

                    await hostBuilder.StartAsync(CancellationToken.None);
                    _host = hostBuilder;
                    _client = hostBuilder.Services.GetRequiredService<IRpcClient>();
                    
                    // Client is already started by the host

                    _state = ConnectionState.Connected;
                    _connectionFailures = 0;
                    _serverDescriptor.HealthStatus = ServerHealthStatus.Healthy;
                    _serverDescriptor.LastHealthCheck = DateTime.UtcNow;
                    
                    _logger.LogInformation("Successfully connected to server {ServerId}", 
                        _serverDescriptor.ServerId);
                }
                catch (Exception ex)
                {
                    _state = ConnectionState.Failed;
                    _connectionFailures++;
                    _serverDescriptor.HealthStatus = ServerHealthStatus.Offline;
                    _serverDescriptor.LastHealthCheck = DateTime.UtcNow;
                    
                    _logger.LogError(ex, "Failed to connect to server {ServerId} (attempt {Attempt})",
                        _serverDescriptor.ServerId, _connectionFailures);
                    
                    throw new InvalidOperationException(
                        $"Failed to connect to server {_serverDescriptor.ServerId} at " +
                        $"{_serverDescriptor.HostName}:{_serverDescriptor.Port}", ex);
                }
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        public async Task<ServerHealthStatus> CheckHealthAsync()
        {
            if (_disposed)
                return ServerHealthStatus.Unknown;
            
            await _connectionLock.WaitAsync();
            try
            {
                _serverDescriptor.LastHealthCheck = DateTime.UtcNow;
                
                if (_state != ConnectionState.Connected || _client == null)
                {
                    _serverDescriptor.HealthStatus = ServerHealthStatus.Offline;
                    return ServerHealthStatus.Offline;
                }

                try
                {
                    // For now, assume connected if we have a client
                    // Could enhance this with an actual ping/health check call
                    _serverDescriptor.HealthStatus = ServerHealthStatus.Healthy;
                    return ServerHealthStatus.Healthy;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Health check failed for server {ServerId}", 
                        _serverDescriptor.ServerId);
                    _serverDescriptor.HealthStatus = ServerHealthStatus.Unhealthy;
                    return ServerHealthStatus.Unhealthy;
                }
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            
            _disposed = true;
            
            await _connectionLock.WaitAsync();
            try
            {
                if (_host != null)
                {
                    try
                    {
                        await _host.StopAsync(CancellationToken.None);
                        _host.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error disposing host for server {ServerId}", 
                            _serverDescriptor.ServerId);
                    }
                    _host = null;
                    _client = null;
                }
                
                _state = ConnectionState.Disconnected;
            }
            finally
            {
                _connectionLock.Release();
                _connectionLock?.Dispose();
            }
        }

        private enum ConnectionState
        {
            Disconnected,
            Connecting,
            Connected,
            Failed
        }
    }
}