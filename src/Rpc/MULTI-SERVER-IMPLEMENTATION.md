# Forkleans RPC Multi-Server Implementation Guide

## Phase 1: Connection Abstraction Implementation

### 1. Create IRpcConnection Interface

```csharp
// File: Orleans.Rpc.Abstractions/IRpcConnection.cs
using System;
using System.Net;
using System.Threading.Tasks;
using Forkleans.Rpc.Protocol;

namespace Forkleans.Rpc
{
    /// <summary>
    /// Represents a connection to a single RPC server.
    /// </summary>
    public interface IRpcConnection : IDisposable
    {
        /// <summary>
        /// Gets the unique identifier for this server.
        /// </summary>
        string ServerId { get; }
        
        /// <summary>
        /// Gets the endpoint this connection is connected to.
        /// </summary>
        IPEndPoint Endpoint { get; }
        
        /// <summary>
        /// Gets whether this connection is currently active.
        /// </summary>
        bool IsConnected { get; }
        
        /// <summary>
        /// Gets statistics about this connection.
        /// </summary>
        ConnectionStatistics Statistics { get; }
        
        /// <summary>
        /// Sends a request and waits for the response.
        /// </summary>
        Task<RpcResponse> SendRequestAsync(RpcRequest request);
        
        /// <summary>
        /// Event raised when the connection is lost.
        /// </summary>
        event EventHandler<ConnectionLostEventArgs> ConnectionLost;
    }
    
    public class ConnectionStatistics
    {
        public int PendingRequests { get; set; }
        public long TotalRequests { get; set; }
        public long TotalErrors { get; set; }
        public TimeSpan AverageLatency { get; set; }
        public DateTime LastActivity { get; set; }
    }
    
    public class ConnectionLostEventArgs : EventArgs
    {
        public string Reason { get; set; }
        public Exception Exception { get; set; }
    }
}
```

### 2. Implement RpcConnection

```csharp
// File: Orleans.Rpc.Client/RpcConnection.cs
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Forkleans.Rpc.Protocol;
using Forkleans.Rpc.Transport;

namespace Forkleans.Rpc
{
    internal sealed class RpcConnection : IRpcConnection
    {
        private readonly ILogger<RpcConnection> _logger;
        private readonly IRpcTransport _transport;
        private readonly RpcMessageSerializer _messageSerializer;
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<RpcResponse>> _pendingRequests;
        private readonly ConnectionStatistics _statistics;
        private bool _isConnected;
        private int _disposed;

        public string ServerId { get; }
        public IPEndPoint Endpoint { get; }
        public bool IsConnected => _isConnected && _disposed == 0;
        public ConnectionStatistics Statistics => _statistics;

        public event EventHandler<ConnectionLostEventArgs> ConnectionLost;

        public RpcConnection(
            string serverId,
            IPEndPoint endpoint,
            IRpcTransport transport,
            RpcMessageSerializer messageSerializer,
            ILogger<RpcConnection> logger)
        {
            ServerId = serverId ?? throw new ArgumentNullException(nameof(serverId));
            Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _messageSerializer = messageSerializer ?? throw new ArgumentNullException(nameof(messageSerializer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _pendingRequests = new ConcurrentDictionary<Guid, TaskCompletionSource<RpcResponse>>();
            _statistics = new ConnectionStatistics();
            
            // Subscribe to transport events
            _transport.DataReceived += OnDataReceived;
            _transport.ConnectionClosed += OnConnectionClosed;
        }

        public async Task<RpcResponse> SendRequestAsync(RpcRequest request)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException($"Connection to server {ServerId} is not active");
            }

            var tcs = new TaskCompletionSource<RpcResponse>();
            if (!_pendingRequests.TryAdd(request.MessageId, tcs))
            {
                throw new InvalidOperationException($"Duplicate request ID: {request.MessageId}");
            }

            try
            {
                Interlocked.Increment(ref _statistics.PendingRequests);
                Interlocked.Increment(ref _statistics.TotalRequests);
                
                var data = _messageSerializer.SerializeMessage(request);
                await _transport.SendAsync(Endpoint, data, CancellationToken.None);

                using (var cts = new CancellationTokenSource(request.TimeoutMs))
                {
                    var response = await tcs.Task.WaitAsync(cts.Token);
                    _statistics.LastActivity = DateTime.UtcNow;
                    return response;
                }
            }
            catch (OperationCanceledException)
            {
                _pendingRequests.TryRemove(request.MessageId, out _);
                Interlocked.Increment(ref _statistics.TotalErrors);
                throw new TimeoutException($"Request {request.MessageId} timed out after {request.TimeoutMs}ms");
            }
            catch (Exception ex)
            {
                _pendingRequests.TryRemove(request.MessageId, out _);
                Interlocked.Increment(ref _statistics.TotalErrors);
                _logger.LogError(ex, "Error sending request {MessageId} to server {ServerId}", 
                    request.MessageId, ServerId);
                throw;
            }
            finally
            {
                Interlocked.Decrement(ref _statistics.PendingRequests);
            }
        }

        private void OnDataReceived(object sender, RpcDataReceivedEventArgs e)
        {
            try
            {
                var message = _messageSerializer.DeserializeMessage(e.Data);
                
                if (message is RpcResponse response)
                {
                    if (_pendingRequests.TryRemove(response.RequestId, out var tcs))
                    {
                        if (response.Success)
                        {
                            tcs.SetResult(response);
                        }
                        else
                        {
                            tcs.SetException(new Exception(response.ErrorMessage ?? "RPC call failed"));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing received data from server {ServerId}", ServerId);
            }
        }

        private void OnConnectionClosed(object sender, RpcConnectionEventArgs e)
        {
            _isConnected = false;
            ConnectionLost?.Invoke(this, new ConnectionLostEventArgs 
            { 
                Reason = "Transport connection closed" 
            });
            
            // Fail all pending requests
            foreach (var pending in _pendingRequests)
            {
                pending.Value.TrySetException(new Exception("Connection lost"));
            }
            _pendingRequests.Clear();
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
            {
                _transport.DataReceived -= OnDataReceived;
                _transport.ConnectionClosed -= OnConnectionClosed;
                _transport.Dispose();
            }
        }
    }
}
```

### 3. Create Connection Pool Interface

```csharp
// File: Orleans.Rpc.Abstractions/IRpcConnectionPool.cs
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Forkleans.Rpc
{
    /// <summary>
    /// Manages connections to multiple RPC servers.
    /// </summary>
    public interface IRpcConnectionPool : IDisposable
    {
        /// <summary>
        /// Gets a connection for the specified grain.
        /// </summary>
        Task<IRpcConnection> GetConnectionAsync(GrainId grainId, GrainInterfaceType interfaceType);
        
        /// <summary>
        /// Gets a connection to a specific server.
        /// </summary>
        Task<IRpcConnection> GetConnectionByServerIdAsync(string serverId);
        
        /// <summary>
        /// Connects to a new server.
        /// </summary>
        Task<string> ConnectToServerAsync(IPEndPoint endpoint, CancellationToken cancellationToken);
        
        /// <summary>
        /// Disconnects from a server.
        /// </summary>
        Task DisconnectFromServerAsync(string serverId);
        
        /// <summary>
        /// Gets all active connections.
        /// </summary>
        IReadOnlyList<IRpcConnection> GetActiveConnections();
        
        /// <summary>
        /// Event raised when a new connection is established.
        /// </summary>
        event EventHandler<ServerConnectedEventArgs> ServerConnected;
        
        /// <summary>
        /// Event raised when a connection is lost.
        /// </summary>
        event EventHandler<ServerDisconnectedEventArgs> ServerDisconnected;
    }
    
    public class ServerConnectedEventArgs : EventArgs
    {
        public string ServerId { get; set; }
        public IPEndPoint Endpoint { get; set; }
    }
    
    public class ServerDisconnectedEventArgs : EventArgs
    {
        public string ServerId { get; set; }
        public string Reason { get; set; }
    }
}
```

### 4. Basic Connection Pool Implementation

```csharp
// File: Orleans.Rpc.Client/RpcConnectionPool.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Forkleans.Rpc.Configuration;
using Forkleans.Rpc.Transport;

namespace Forkleans.Rpc
{
    internal sealed class RpcConnectionPool : IRpcConnectionPool
    {
        private readonly ILogger<RpcConnectionPool> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly RpcClientOptions _options;
        private readonly IRpcTransportFactory _transportFactory;
        private readonly ConcurrentDictionary<string, IRpcConnection> _connections;
        private readonly IServerSelectionStrategy _selectionStrategy;
        private int _disposed;

        public event EventHandler<ServerConnectedEventArgs> ServerConnected;
        public event EventHandler<ServerDisconnectedEventArgs> ServerDisconnected;

        public RpcConnectionPool(
            ILogger<RpcConnectionPool> logger,
            IServiceProvider serviceProvider,
            IOptions<RpcClientOptions> options,
            IRpcTransportFactory transportFactory,
            IServerSelectionStrategy selectionStrategy = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
            _connections = new ConcurrentDictionary<string, IRpcConnection>();
            _selectionStrategy = selectionStrategy ?? new RoundRobinSelectionStrategy();
        }

        public async Task<IRpcConnection> GetConnectionAsync(GrainId grainId, GrainInterfaceType interfaceType)
        {
            var activeConnections = GetActiveConnections();
            if (activeConnections.Count == 0)
            {
                throw new InvalidOperationException("No active connections available");
            }

            // Use selection strategy to pick a server
            var serverIds = activeConnections.Select(c => c.ServerId).ToList();
            var selectedServerId = await _selectionStrategy.SelectServerAsync(
                grainId, interfaceType, serverIds);

            return await GetConnectionByServerIdAsync(selectedServerId);
        }

        public Task<IRpcConnection> GetConnectionByServerIdAsync(string serverId)
        {
            if (_connections.TryGetValue(serverId, out var connection) && connection.IsConnected)
            {
                return Task.FromResult(connection);
            }

            throw new InvalidOperationException($"No active connection to server {serverId}");
        }

        public async Task<string> ConnectToServerAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
        {
            var serverId = GenerateServerId(endpoint);
            
            // Check if already connected
            if (_connections.TryGetValue(serverId, out var existing) && existing.IsConnected)
            {
                _logger.LogWarning("Already connected to server {ServerId} at {Endpoint}", serverId, endpoint);
                return serverId;
            }

            // Create transport for this connection
            var transport = _transportFactory.CreateTransport(_serviceProvider);
            
            try
            {
                // Start transport and connect
                await transport.StartAsync(endpoint, cancellationToken);
                
                // Create connection wrapper
                var messageSerializer = _serviceProvider.GetRequiredService<Protocol.RpcMessageSerializer>();
                var connectionLogger = _serviceProvider.GetRequiredService<ILogger<RpcConnection>>();
                
                var connection = new RpcConnection(
                    serverId,
                    endpoint,
                    transport,
                    messageSerializer,
                    connectionLogger);
                
                // Handle connection lost
                connection.ConnectionLost += (sender, args) => 
                {
                    _logger.LogWarning("Connection to server {ServerId} lost: {Reason}", 
                        serverId, args.Reason);
                    _connections.TryRemove(serverId, out _);
                    ServerDisconnected?.Invoke(this, new ServerDisconnectedEventArgs 
                    { 
                        ServerId = serverId, 
                        Reason = args.Reason 
                    });
                };
                
                // Add to pool
                if (!_connections.TryAdd(serverId, connection))
                {
                    connection.Dispose();
                    throw new InvalidOperationException($"Failed to add connection for server {serverId}");
                }
                
                _logger.LogInformation("Connected to server {ServerId} at {Endpoint}", serverId, endpoint);
                ServerConnected?.Invoke(this, new ServerConnectedEventArgs 
                { 
                    ServerId = serverId, 
                    Endpoint = endpoint 
                });
                
                return serverId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to server at {Endpoint}", endpoint);
                transport.Dispose();
                throw;
            }
        }

        public async Task DisconnectFromServerAsync(string serverId)
        {
            if (_connections.TryRemove(serverId, out var connection))
            {
                connection.Dispose();
                _logger.LogInformation("Disconnected from server {ServerId}", serverId);
                
                ServerDisconnected?.Invoke(this, new ServerDisconnectedEventArgs 
                { 
                    ServerId = serverId, 
                    Reason = "Disconnected by client" 
                });
            }
        }

        public IReadOnlyList<IRpcConnection> GetActiveConnections()
        {
            return _connections.Values
                .Where(c => c.IsConnected)
                .ToList();
        }

        private string GenerateServerId(IPEndPoint endpoint)
        {
            // For now, use endpoint as ID
            // In production, this would come from server handshake
            return $"{endpoint.Address}:{endpoint.Port}";
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
            {
                foreach (var connection in _connections.Values)
                {
                    connection.Dispose();
                }
                _connections.Clear();
            }
        }
    }
}
```

### 5. Server Selection Strategies

```csharp
// File: Orleans.Rpc.Client/ServerSelection/IServerSelectionStrategy.cs
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Forkleans.Rpc
{
    public interface IServerSelectionStrategy
    {
        Task<string> SelectServerAsync(
            GrainId grainId,
            GrainInterfaceType interfaceType,
            IReadOnlyList<string> availableServers);
    }
}

// File: Orleans.Rpc.Client/ServerSelection/RoundRobinSelectionStrategy.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Forkleans.Rpc
{
    public class RoundRobinSelectionStrategy : IServerSelectionStrategy
    {
        private int _counter;

        public Task<string> SelectServerAsync(
            GrainId grainId,
            GrainInterfaceType interfaceType,
            IReadOnlyList<string> availableServers)
        {
            if (availableServers.Count == 0)
                throw new ArgumentException("No available servers", nameof(availableServers));

            var index = Interlocked.Increment(ref _counter) % availableServers.Count;
            return Task.FromResult(availableServers[index]);
        }
    }
}

// File: Orleans.Rpc.Client/ServerSelection/ConsistentHashSelectionStrategy.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Forkleans.Rpc
{
    public class ConsistentHashSelectionStrategy : IServerSelectionStrategy
    {
        private readonly int _virtualNodes;

        public ConsistentHashSelectionStrategy(int virtualNodes = 150)
        {
            _virtualNodes = virtualNodes;
        }

        public Task<string> SelectServerAsync(
            GrainId grainId,
            GrainInterfaceType interfaceType,
            IReadOnlyList<string> availableServers)
        {
            if (availableServers.Count == 0)
                throw new ArgumentException("No available servers", nameof(availableServers));

            if (availableServers.Count == 1)
                return Task.FromResult(availableServers[0]);

            // Build hash ring
            var ring = new SortedDictionary<uint, string>();
            foreach (var server in availableServers)
            {
                for (int i = 0; i < _virtualNodes; i++)
                {
                    var key = $"{server}:{i}";
                    var hash = GetHash(key);
                    ring[hash] = server;
                }
            }

            // Hash the grain ID
            var grainHash = GetHash(grainId.ToString());

            // Find the server
            var node = ring.FirstOrDefault(n => n.Key >= grainHash);
            if (node.Value == null)
            {
                // Wrap around to first node
                node = ring.First();
            }

            return Task.FromResult(node.Value);
        }

        private uint GetHash(string key)
        {
            using (var md5 = MD5.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(key);
                var hash = md5.ComputeHash(bytes);
                return BitConverter.ToUInt32(hash, 0);
            }
        }
    }
}
```

### 6. Updated RpcClient with Connection Pool

```csharp
// File: Orleans.Rpc.Client/RpcClient.cs (Updated)
internal sealed class RpcClient : IClusterClient, IRpcClient, IHostedService
{
    private readonly ILogger<RpcClient> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly RpcClientOptions _clientOptions;
    private readonly IRpcConnectionPool _connectionPool;
    private readonly IClusterClientLifecycle _lifecycle;
    
    private bool _isInitialized;

    public bool IsInitialized => _isInitialized;
    public IServiceProvider ServiceProvider => _serviceProvider;

    public RpcClient(
        ILogger<RpcClient> logger,
        IServiceProvider serviceProvider,
        IOptions<RpcClientOptions> clientOptions,
        IRpcConnectionPool connectionPool,
        IClusterClientLifecycle lifecycle)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _clientOptions = clientOptions?.Value ?? throw new ArgumentNullException(nameof(clientOptions));
        _connectionPool = connectionPool ?? throw new ArgumentNullException(nameof(connectionPool));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting RPC client {ClientId} in {Mode} mode", 
            _clientOptions.ClientId, _clientOptions.Mode);
        
        try
        {
            // Initialize runtime client
            var runtimeClient = _serviceProvider.GetService<IRuntimeClient>() as OutsideRpcRuntimeClient;
            runtimeClient?.ConsumeServices();
            
            await (_lifecycle as ILifecycleSubject)?.OnStart(cancellationToken);
            
            // Connect to configured servers
            if (_clientOptions.ServerEndpoints.Count == 0)
            {
                throw new InvalidOperationException("No server endpoints configured");
            }

            // Connect based on mode
            if (_clientOptions.Mode == MultiServerMode.SingleServer)
            {
                // Legacy behavior - connect to first server only
                var endpoint = _clientOptions.ServerEndpoints[0];
                await _connectionPool.ConnectToServerAsync(endpoint, cancellationToken);
            }
            else
            {
                // Multi-server mode - connect to all servers
                var connectTasks = _clientOptions.ServerEndpoints
                    .Select(ep => ConnectToServerWithRetry(ep, cancellationToken))
                    .ToList();
                
                await Task.WhenAll(connectTasks);
                
                var connectedCount = _connectionPool.GetActiveConnections().Count;
                if (connectedCount == 0)
                {
                    throw new InvalidOperationException("Failed to connect to any servers");
                }
                
                _logger.LogInformation("Connected to {ConnectedCount}/{TotalCount} servers", 
                    connectedCount, _clientOptions.ServerEndpoints.Count);
            }
            
            _isInitialized = true;
            _logger.LogInformation("RPC client started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start RPC client");
            throw;
        }
    }

    private async Task ConnectToServerWithRetry(IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < _clientOptions.MaxRetryAttempts; attempt++)
        {
            try
            {
                await _connectionPool.ConnectToServerAsync(endpoint, cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < _clientOptions.MaxRetryAttempts - 1)
            {
                _logger.LogWarning(ex, "Failed to connect to {Endpoint}, attempt {Attempt}/{MaxAttempts}", 
                    endpoint, attempt + 1, _clientOptions.MaxRetryAttempts);
                
                await Task.Delay(_clientOptions.RetryDelayMs, cancellationToken);
            }
        }
    }

    internal async Task<Protocol.RpcResponse> SendRequestAsync(Protocol.RpcRequest request)
    {
        EnsureInitialized();
        
        // Get connection based on grain
        var connection = await _connectionPool.GetConnectionAsync(
            request.GrainId, 
            request.InterfaceType);
        
        _logger.LogDebug("Sending request {MessageId} to server {ServerId}", 
            request.MessageId, connection.ServerId);
        
        return await connection.SendRequestAsync(request);
    }

    private void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("RPC client is not initialized");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping RPC client");
        
        try
        {
            _connectionPool.Dispose();
            await (_lifecycle as ILifecycleSubject)?.OnStop(cancellationToken);
            _isInitialized = false;
            _logger.LogInformation("RPC client stopped successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while stopping RPC client");
            throw;
        }
    }

    // ... rest of GetGrain methods remain unchanged ...
}
```

### 7. Configuration Updates

```csharp
// File: Orleans.Rpc.Abstractions/Configuration/RpcClientOptions.cs (Updated)
public class RpcClientOptions
{
    public string ClientId { get; set; } = Guid.NewGuid().ToString("N");
    public int ConnectionTimeoutMs { get; set; } = 30000;
    public int RequestTimeoutMs { get; set; } = 30000;
    public int MaxRetryAttempts { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 1000;
    public List<IPEndPoint> ServerEndpoints { get; set; } = new List<IPEndPoint>();
    
    // New multi-server options
    public MultiServerMode Mode { get; set; } = MultiServerMode.SingleServer;
    public bool EnableConnectionPooling { get; set; } = true;
    public int MaxConnectionsPerServer { get; set; } = 1;
    public bool EnableHealthChecks { get; set; } = true;
    public int HealthCheckIntervalMs { get; set; } = 30000;
}

public enum MultiServerMode
{
    /// <summary>
    /// Legacy mode - connect to first server only
    /// </summary>
    SingleServer,
    
    /// <summary>
    /// Connect to all configured servers
    /// </summary>
    MultiServer,
    
    /// <summary>
    /// Connect on-demand as grains are accessed
    /// </summary>
    OnDemand
}
```

## Usage Example

```csharp
// Configure multi-server RPC client
builder.Host.UseOrleansRpcClient(rpcBuilder =>
{
    rpcBuilder.UseLiteNetLib();
    
    // Enable multi-server mode
    rpcBuilder.Configure<RpcClientOptions>(options =>
    {
        options.Mode = MultiServerMode.MultiServer;
        
        // Add multiple server endpoints
        options.ServerEndpoints.Add(new IPEndPoint(IPAddress.Parse("10.0.0.1"), 11111));
        options.ServerEndpoints.Add(new IPEndPoint(IPAddress.Parse("10.0.0.2"), 11111));
        options.ServerEndpoints.Add(new IPEndPoint(IPAddress.Parse("10.0.0.3"), 11111));
        
        // Configure retry behavior
        options.MaxRetryAttempts = 3;
        options.RetryDelayMs = 1000;
        
        // Enable health checks
        options.EnableHealthChecks = true;
        options.HealthCheckIntervalMs = 30000;
    });
    
    // Configure server selection strategy
    rpcBuilder.Services.AddSingleton<IServerSelectionStrategy, ConsistentHashSelectionStrategy>();
});

// Use the client - routing happens automatically
var client = host.Services.GetRequiredService<IRpcClient>();
var grain = client.GetGrain<IGameGrain>(playerId);
await grain.UpdatePosition(position); // Automatically routed to appropriate server
```

## Next Steps

This implementation provides the foundation for multi-server support while maintaining backward compatibility. Future phases would add:

1. **Per-server manifest management**
2. **Smart routing based on grain availability**
3. **Connection health monitoring and auto-reconnect**
4. **Load balancing metrics**
5. **Server affinity for grain references**