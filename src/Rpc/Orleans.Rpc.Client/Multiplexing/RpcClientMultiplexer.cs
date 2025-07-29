using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Runtime;

namespace Granville.Rpc.Multiplexing
{
    /// <summary>
    /// Manages multiple RPC client connections and routes grain requests to appropriate servers.
    /// </summary>
    internal sealed class RpcClientMultiplexer : IRpcClientMultiplexer
    {
        private readonly ILogger<RpcClientMultiplexer> _logger;
        private readonly IGrainRoutingStrategy _routingStrategy;
        private readonly RpcClientMultiplexerOptions _options;
        private readonly ConcurrentDictionary<string, RpcClientConnection> _connections;
        private readonly IServiceProvider _serviceProvider;
        private readonly SemaphoreSlim _connectionLock;
        private IRoutingContext _routingContext;
        private readonly Timer _healthCheckTimer;
        private bool _disposed;

        public event EventHandler<ServerHealthChangedEventArgs> ServerHealthChanged;

        public RpcClientMultiplexer(
            ILogger<RpcClientMultiplexer> logger,
            IGrainRoutingStrategy routingStrategy,
            IOptions<RpcClientMultiplexerOptions> options,
            IServiceProvider serviceProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _routingStrategy = routingStrategy ?? throw new ArgumentNullException(nameof(routingStrategy));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _connections = new ConcurrentDictionary<string, RpcClientConnection>();
            _connectionLock = new SemaphoreSlim(1, 1);
            _routingContext = new RoutingContext();
            
            if (_options.EnableHealthChecks)
            {
                _healthCheckTimer = new Timer(
                    HealthCheckCallback,
                    null,
                    _options.HealthCheckInterval,
                    _options.HealthCheckInterval);
            }
        }

        public async Task<bool> RegisterServerAsync(IServerDescriptor server)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RpcClientMultiplexer));
            if (server == null) throw new ArgumentNullException(nameof(server));
            
            _logger.LogInformation("Registering server {ServerId} at {HostName}:{Port}", 
                server.ServerId, server.HostName, server.Port);
            
            await _connectionLock.WaitAsync();
            try
            {
                if (_connections.ContainsKey(server.ServerId))
                {
                    _logger.LogWarning("Server {ServerId} already registered", server.ServerId);
                    return false;
                }

                var connection = new RpcClientConnection(server, _serviceProvider, _logger);
                _connections[server.ServerId] = connection;
                
                // Eagerly connect if configured
                if (_options.EagerConnect)
                {
                    try
                    {
                        await connection.EnsureConnectedAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to eagerly connect to server {ServerId}", server.ServerId);
                    }
                }
                
                _logger.LogInformation("Successfully registered server {ServerId}", server.ServerId);
                return true;
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        public async Task<bool> UnregisterServerAsync(string serverId)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RpcClientMultiplexer));
            if (string.IsNullOrEmpty(serverId)) throw new ArgumentNullException(nameof(serverId));
            
            _logger.LogInformation("Unregistering server {ServerId}", serverId);
            
            await _connectionLock.WaitAsync();
            try
            {
                if (_connections.TryRemove(serverId, out var connection))
                {
                    await connection.DisposeAsync();
                    _logger.LogInformation("Successfully unregistered server {ServerId}", serverId);
                    return true;
                }
                
                _logger.LogWarning("Server {ServerId} not found in registry", serverId);
                return false;
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        public IReadOnlyDictionary<string, IServerDescriptor> GetRegisteredServers()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RpcClientMultiplexer));
            
            return _connections.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ServerDescriptor);
        }

        public TGrainInterface GetGrain<TGrainInterface>(string primaryKey, string? grainClassNamePrefix = null) 
            where TGrainInterface : IGrainWithStringKey
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RpcClientMultiplexer));
            
            var grainInterface = typeof(TGrainInterface);
            _logger.LogDebug("GetGrain<{Interface}>({Key}) called", grainInterface.Name, primaryKey);
            
            // Select server using routing strategy
            var serverId = SelectServerForGrain(grainInterface, primaryKey);
            
            // Get client and create grain reference
            var client = GetRpcClient(serverId);
            return client.GetGrain<TGrainInterface>(primaryKey, grainClassNamePrefix);
        }

        public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string? grainClassNamePrefix = null) 
            where TGrainInterface : IGrainWithGuidKey
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RpcClientMultiplexer));
            
            var grainInterface = typeof(TGrainInterface);
            var serverId = SelectServerForGrain(grainInterface, primaryKey.ToString());
            var client = GetRpcClient(serverId);
            return client.GetGrain<TGrainInterface>(primaryKey, grainClassNamePrefix);
        }

        public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string? grainClassNamePrefix = null) 
            where TGrainInterface : IGrainWithIntegerKey
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RpcClientMultiplexer));
            
            var grainInterface = typeof(TGrainInterface);
            var serverId = SelectServerForGrain(grainInterface, primaryKey.ToString());
            var client = GetRpcClient(serverId);
            return client.GetGrain<TGrainInterface>(primaryKey, grainClassNamePrefix);
        }

        public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string keyExtension, string? grainClassNamePrefix = null) 
            where TGrainInterface : IGrainWithGuidCompoundKey
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RpcClientMultiplexer));
            
            var grainInterface = typeof(TGrainInterface);
            var serverId = SelectServerForGrain(grainInterface, $"{primaryKey}+{keyExtension}");
            var client = GetRpcClient(serverId);
            return client.GetGrain<TGrainInterface>(primaryKey, keyExtension, grainClassNamePrefix);
        }

        public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string keyExtension, string? grainClassNamePrefix = null) 
            where TGrainInterface : IGrainWithIntegerCompoundKey
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RpcClientMultiplexer));
            
            var grainInterface = typeof(TGrainInterface);
            var serverId = SelectServerForGrain(grainInterface, $"{primaryKey}+{keyExtension}");
            var client = GetRpcClient(serverId);
            return client.GetGrain<TGrainInterface>(primaryKey, keyExtension, grainClassNamePrefix);
        }

        public TGrainInterface GetGrain<TGrainInterface>(GrainId grainId) 
            where TGrainInterface : IAddressable
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RpcClientMultiplexer));
            
            var grainInterface = typeof(TGrainInterface);
            var serverId = SelectServerForGrain(grainInterface, grainId.ToString());
            var client = GetRpcClient(serverId);
            return client.GetGrain<TGrainInterface>(grainId);
        }

        public IAddressable GetGrain(GrainId grainId)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RpcClientMultiplexer));
            
            var serverId = SelectServerForGrain(typeof(IAddressable), grainId.ToString());
            var client = GetRpcClient(serverId);
            return client.GetGrain(grainId);
        }

        public IAddressable GetGrain(GrainId grainId, GrainInterfaceType interfaceType)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RpcClientMultiplexer));
            
            var serverId = SelectServerForGrain(typeof(IAddressable), grainId.ToString());
            var client = GetRpcClient(serverId);
            return client.GetGrain(grainId, interfaceType);
        }

        public IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RpcClientMultiplexer));
            
            var serverId = SelectServerForGrain(grainInterfaceType, grainPrimaryKey.ToString());
            var client = GetRpcClient(serverId);
            return client.GetGrain(grainInterfaceType, grainPrimaryKey);
        }

        public IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RpcClientMultiplexer));
            
            var serverId = SelectServerForGrain(grainInterfaceType, grainPrimaryKey.ToString());
            var client = GetRpcClient(serverId);
            return client.GetGrain(grainInterfaceType, grainPrimaryKey);
        }

        public IGrain GetGrain(Type grainInterfaceType, string grainPrimaryKey)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RpcClientMultiplexer));
            
            var serverId = SelectServerForGrain(grainInterfaceType, grainPrimaryKey);
            var client = GetRpcClient(serverId);
            return client.GetGrain(grainInterfaceType, grainPrimaryKey);
        }

        public void SetRoutingContext(IRoutingContext context)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RpcClientMultiplexer));
            _routingContext = context ?? throw new ArgumentNullException(nameof(context));
            
            _logger.LogDebug("Routing context updated with properties: {Properties}",
                string.Join(", ", context.GetPropertyKeys()));
        }

        public IRoutingContext GetRoutingContext()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RpcClientMultiplexer));
            return _routingContext;
        }

        public async Task<Dictionary<string, ServerHealthStatus>> GetServerHealthAsync()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RpcClientMultiplexer));
            
            var results = new Dictionary<string, ServerHealthStatus>();
            
            foreach (var kvp in _connections)
            {
                try
                {
                    var health = await kvp.Value.CheckHealthAsync();
                    results[kvp.Key] = health;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to check health for server {ServerId}", kvp.Key);
                    results[kvp.Key] = ServerHealthStatus.Unknown;
                }
            }
            
            return results;
        }

        private string SelectServerForGrain(Type grainInterface, string grainKey)
        {
            var servers = GetRegisteredServers();
            if (servers.Count == 0)
            {
                throw new InvalidOperationException("No servers registered with multiplexer");
            }
            
            var serverId = _routingStrategy.SelectServerAsync(
                grainInterface,
                grainKey,
                servers,
                _routingContext).GetAwaiter().GetResult();
            
            if (string.IsNullOrEmpty(serverId))
            {
                throw new InvalidOperationException(
                    $"No suitable server found for grain {grainInterface.Name} with key {grainKey}");
            }
            
            _logger.LogDebug("Selected server {ServerId} for grain {Interface}:{Key}",
                serverId, grainInterface.Name, grainKey);
            
            return serverId;
        }

        private RpcClient GetRpcClient(string serverId)
        {
            if (!_connections.TryGetValue(serverId, out var connection))
            {
                throw new InvalidOperationException($"Server {serverId} not found in connection pool");
            }
            
            var client = connection.GetClientAsync().GetAwaiter().GetResult();
            return client;
        }

        private async void HealthCheckCallback(object state)
        {
            if (_disposed) return;
            
            try
            {
                foreach (var connection in _connections.Values)
                {
                    try
                    {
                        var oldStatus = connection.ServerDescriptor.HealthStatus;
                        var newStatus = await connection.CheckHealthAsync();
                        
                        if (oldStatus != newStatus)
                        {
                            _logger.LogInformation("Server {ServerId} health changed from {OldStatus} to {NewStatus}",
                                connection.ServerDescriptor.ServerId, oldStatus, newStatus);
                            
                            ServerHealthChanged?.Invoke(this, new ServerHealthChangedEventArgs
                            {
                                ServerId = connection.ServerDescriptor.ServerId,
                                OldStatus = oldStatus,
                                NewStatus = newStatus
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Health check failed for server {ServerId}", 
                            connection.ServerDescriptor.ServerId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in health check timer callback");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            _healthCheckTimer?.Dispose();
            _connectionLock?.Dispose();
            
            var disposeTasks = _connections.Values.Select(c => c.DisposeAsync().AsTask()).ToArray();
            Task.WaitAll(disposeTasks);
            
            _connections.Clear();
        }
    }
}