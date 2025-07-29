# RPC Client Multiplexer Implementation Guide

## Core Interfaces

### IServerDescriptor
```csharp
namespace Granville.Rpc.Multiplexing
{
    public interface IServerDescriptor
    {
        string ServerId { get; }
        string HostName { get; }
        int Port { get; }
        Dictionary<string, string> Metadata { get; }
        bool IsPrimary { get; }
        DateTime LastHealthCheck { get; }
        ServerHealthStatus HealthStatus { get; }
    }

    public enum ServerHealthStatus
    {
        Unknown,
        Healthy,
        Degraded,
        Unhealthy,
        Offline
    }
}
```

### IGrainRoutingStrategy
```csharp
namespace Granville.Rpc.Multiplexing
{
    public interface IGrainRoutingStrategy
    {
        Task<string> SelectServerAsync(
            Type grainInterface,
            string grainKey,
            IReadOnlyDictionary<string, IServerDescriptor> servers,
            IRoutingContext context);
    }

    public interface IRoutingContext
    {
        Dictionary<string, object> Properties { get; }
        T GetProperty<T>(string key);
        void SetProperty<T>(string key, T value);
    }
}
```

### IRpcClientMultiplexer
```csharp
namespace Granville.Rpc.Multiplexing
{
    public interface IRpcClientMultiplexer : IDisposable
    {
        // Server management
        Task<bool> RegisterServerAsync(IServerDescriptor server);
        Task<bool> UnregisterServerAsync(string serverId);
        IReadOnlyDictionary<string, IServerDescriptor> GetRegisteredServers();
        
        // Grain operations - matches RpcClient interface
        TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string grainClassNamePrefix = null) 
            where TGrainInterface : IGrainWithGuidKey;
        TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string grainClassNamePrefix = null) 
            where TGrainInterface : IGrainWithIntegerKey;
        TGrainInterface GetGrain<TGrainInterface>(string primaryKey, string grainClassNamePrefix = null) 
            where TGrainInterface : IGrainWithStringKey;
        
        // Context management
        void SetRoutingContext(IRoutingContext context);
        IRoutingContext GetRoutingContext();
        
        // Health monitoring
        Task<Dictionary<string, ServerHealthStatus>> GetServerHealthAsync();
        event EventHandler<ServerHealthChangedEventArgs> ServerHealthChanged;
    }
}
```

## Implementation Classes

### RpcClientMultiplexer
```csharp
namespace Granville.Rpc.Multiplexing
{
    public sealed class RpcClientMultiplexer : IRpcClientMultiplexer
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

        public RpcClientMultiplexer(
            ILogger<RpcClientMultiplexer> logger,
            IGrainRoutingStrategy routingStrategy,
            IOptions<RpcClientMultiplexerOptions> options,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _routingStrategy = routingStrategy;
            _options = options.Value;
            _serviceProvider = serviceProvider;
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
                    await connection.EnsureConnectedAsync();
                }
                
                return true;
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        public TGrainInterface GetGrain<TGrainInterface>(string primaryKey, string grainClassNamePrefix = null) 
            where TGrainInterface : IGrainWithStringKey
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RpcClientMultiplexer));
            
            var grainInterface = typeof(TGrainInterface);
            
            // Select server using routing strategy
            var serverId = _routingStrategy.SelectServerAsync(
                grainInterface,
                primaryKey,
                GetServerDescriptors(),
                _routingContext).GetAwaiter().GetResult();
            
            if (string.IsNullOrEmpty(serverId))
            {
                throw new InvalidOperationException(
                    $"No suitable server found for grain {grainInterface.Name} with key {primaryKey}");
            }
            
            // Get or create connection
            if (!_connections.TryGetValue(serverId, out var connection))
            {
                throw new InvalidOperationException($"Server {serverId} not found in connection pool");
            }
            
            // Ensure connected and get grain
            var client = connection.GetClientAsync().GetAwaiter().GetResult();
            return client.GetGrain<TGrainInterface>(primaryKey, grainClassNamePrefix);
        }

        private IReadOnlyDictionary<string, IServerDescriptor> GetServerDescriptors()
        {
            return _connections.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ServerDescriptor);
        }

        private async void HealthCheckCallback(object state)
        {
            foreach (var connection in _connections.Values)
            {
                try
                {
                    var health = await connection.CheckHealthAsync();
                    if (health != connection.ServerDescriptor.HealthStatus)
                    {
                        ServerHealthChanged?.Invoke(this, new ServerHealthChangedEventArgs
                        {
                            ServerId = connection.ServerDescriptor.ServerId,
                            OldStatus = connection.ServerDescriptor.HealthStatus,
                            NewStatus = health
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
    }
}
```

### RpcClientConnection
```csharp
internal sealed class RpcClientConnection
{
    private readonly IServerDescriptor _serverDescriptor;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _connectionLock;
    private RpcClient _client;
    private ConnectionState _state;
    private DateTime _lastConnectionAttempt;
    private int _connectionFailures;

    public IServerDescriptor ServerDescriptor => _serverDescriptor;

    public async Task<RpcClient> GetClientAsync()
    {
        if (_state == ConnectionState.Connected && _client != null)
        {
            return _client;
        }

        await EnsureConnectedAsync();
        return _client;
    }

    public async Task EnsureConnectedAsync()
    {
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
                        $"Connection to {_serverDescriptor.ServerId} is in backoff period");
                }
            }

            _state = ConnectionState.Connecting;
            _lastConnectionAttempt = DateTime.UtcNow;

            try
            {
                _logger.LogInformation("Connecting to server {ServerId} at {HostName}:{Port}",
                    _serverDescriptor.ServerId, _serverDescriptor.HostName, _serverDescriptor.Port);

                // Create new RpcClient with server-specific configuration
                var clientBuilder = new RpcClientBuilder(_serviceProvider)
                    .Configure<RpcClientOptions>(options =>
                    {
                        options.ServerEndpoints = new[]
                        {
                            new DnsEndPoint(_serverDescriptor.HostName, _serverDescriptor.Port)
                        };
                    });

                _client = clientBuilder.Build();
                await _client.StartAsync();

                _state = ConnectionState.Connected;
                _connectionFailures = 0;
                _serverDescriptor.HealthStatus = ServerHealthStatus.Healthy;
                
                _logger.LogInformation("Successfully connected to server {ServerId}", 
                    _serverDescriptor.ServerId);
            }
            catch (Exception ex)
            {
                _state = ConnectionState.Failed;
                _connectionFailures++;
                _serverDescriptor.HealthStatus = ServerHealthStatus.Offline;
                
                _logger.LogError(ex, "Failed to connect to server {ServerId} (attempt {Attempt})",
                    _serverDescriptor.ServerId, _connectionFailures);
                    
                throw;
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task<ServerHealthStatus> CheckHealthAsync()
    {
        if (_state != ConnectionState.Connected || _client == null)
        {
            return ServerHealthStatus.Offline;
        }

        try
        {
            // Perform a simple health check - could be enhanced with actual ping
            // For now, just check if the client is still connected
            if (_client.IsConnected)
            {
                return ServerHealthStatus.Healthy;
            }
            else
            {
                _state = ConnectionState.Disconnected;
                return ServerHealthStatus.Offline;
            }
        }
        catch
        {
            return ServerHealthStatus.Unhealthy;
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
```

## Routing Strategies

### ZoneBasedRoutingStrategy
```csharp
public class ZoneBasedRoutingStrategy : IGrainRoutingStrategy
{
    private readonly ILogger<ZoneBasedRoutingStrategy> _logger;

    public Task<string> SelectServerAsync(
        Type grainInterface,
        string grainKey,
        IReadOnlyDictionary<string, IServerDescriptor> servers,
        IRoutingContext context)
    {
        // Check if context has zone information
        var zone = context.GetProperty<string>("Zone");
        if (string.IsNullOrEmpty(zone))
        {
            _logger.LogWarning("No zone specified in routing context, using primary server");
            var primary = servers.Values.FirstOrDefault(s => s.IsPrimary);
            return Task.FromResult(primary?.ServerId);
        }

        // Find server handling this zone
        var zoneServer = servers.Values.FirstOrDefault(s => 
            s.Metadata.TryGetValue("zone", out var serverZone) && serverZone == zone);

        if (zoneServer != null)
        {
            _logger.LogDebug("Routing grain {Interface}:{Key} to zone server {ServerId}",
                grainInterface.Name, grainKey, zoneServer.ServerId);
            return Task.FromResult(zoneServer.ServerId);
        }

        // Fallback to primary
        _logger.LogWarning("No server found for zone {Zone}, falling back to primary", zone);
        var fallback = servers.Values.FirstOrDefault(s => s.IsPrimary);
        return Task.FromResult(fallback?.ServerId);
    }
}
```

### ServiceBasedRoutingStrategy
```csharp
public class ServiceBasedRoutingStrategy : IGrainRoutingStrategy
{
    private readonly Dictionary<Type, string> _serviceMapping;

    public ServiceBasedRoutingStrategy()
    {
        _serviceMapping = new Dictionary<Type, string>();
    }

    public void MapService<TGrainInterface>(string serverId)
    {
        _serviceMapping[typeof(TGrainInterface)] = serverId;
    }

    public Task<string> SelectServerAsync(
        Type grainInterface,
        string grainKey,
        IReadOnlyDictionary<string, IServerDescriptor> servers,
        IRoutingContext context)
    {
        if (_serviceMapping.TryGetValue(grainInterface, out var serverId))
        {
            return Task.FromResult(serverId);
        }

        // Check interface attributes for hints
        var serviceAttr = grainInterface.GetCustomAttribute<RpcServiceAttribute>();
        if (serviceAttr != null && !string.IsNullOrEmpty(serviceAttr.PreferredServer))
        {
            return Task.FromResult(serviceAttr.PreferredServer);
        }

        // Default to primary
        var primary = servers.Values.FirstOrDefault(s => s.IsPrimary);
        return Task.FromResult(primary?.ServerId);
    }
}
```

### CompositeRoutingStrategy
```csharp
public class CompositeRoutingStrategy : IGrainRoutingStrategy
{
    private readonly List<(Func<Type, bool> predicate, IGrainRoutingStrategy strategy)> _strategies;

    public CompositeRoutingStrategy()
    {
        _strategies = new List<(Func<Type, bool>, IGrainRoutingStrategy)>();
    }

    public void AddStrategy(Func<Type, bool> predicate, IGrainRoutingStrategy strategy)
    {
        _strategies.Add((predicate, strategy));
    }

    public async Task<string> SelectServerAsync(
        Type grainInterface,
        string grainKey,
        IReadOnlyDictionary<string, IServerDescriptor> servers,
        IRoutingContext context)
    {
        foreach (var (predicate, strategy) in _strategies)
        {
            if (predicate(grainInterface))
            {
                return await strategy.SelectServerAsync(grainInterface, grainKey, servers, context);
            }
        }

        // Default fallback
        var primary = servers.Values.FirstOrDefault(s => s.IsPrimary);
        return primary?.ServerId;
    }
}
```

## Configuration

### RpcClientMultiplexerOptions
```csharp
public class RpcClientMultiplexerOptions
{
    public bool EagerConnect { get; set; } = false;
    public bool EnableHealthChecks { get; set; } = true;
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public int MaxConnectionRetries { get; set; } = 3;
    public TimeSpan RetryBackoffBase { get; set; } = TimeSpan.FromSeconds(2);
}
```

### Dependency Injection Setup
```csharp
public static class ServiceCollectionExtensions
{
    public static IRpcClientBuilder AddRpcClientMultiplexer(
        this IRpcClientBuilder builder,
        Action<RpcClientMultiplexerOptions> configureOptions = null)
    {
        var services = builder.Services;
        
        // Register options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // Register multiplexer
        services.TryAddSingleton<IRpcClientMultiplexer, RpcClientMultiplexer>();
        
        // Register default routing strategy
        services.TryAddSingleton<IGrainRoutingStrategy, ZoneBasedRoutingStrategy>();
        
        return builder;
    }
}
```

## Usage Example

```csharp
// In Startup.cs
services.AddRpcClient()
    .AddRpcClientMultiplexer(options =>
    {
        options.EnableHealthChecks = true;
        options.HealthCheckInterval = TimeSpan.FromSeconds(15);
    })
    .UseRoutingStrategy<CompositeRoutingStrategy>(strategy =>
    {
        // Zone-aware grains
        strategy.AddStrategy(
            type => type.IsAssignableTo<IZoneAwareGrain>(),
            new ZoneBasedRoutingStrategy());
            
        // Service-specific routing
        var serviceRouting = new ServiceBasedRoutingStrategy();
        serviceRouting.MapService<IGameRpcGrain>("primary-server");
        serviceRouting.MapService<IChatGrain>("chat-server");
        strategy.AddStrategy(_ => true, serviceRouting);
    });

// In application code
public class GranvilleRpcGameClientService
{
    private readonly IRpcClientMultiplexer _multiplexer;
    
    public async Task ConnectAsync(string playerName)
    {
        // Register all action servers
        foreach (var server in await DiscoverServersAsync())
        {
            await _multiplexer.RegisterServerAsync(server);
        }
        
        // Update routing context for zone
        _multiplexer.SetRoutingContext(new RoutingContext 
        { 
            ["Zone"] = currentZone 
        });
        
        // Get grain - automatically routed to correct server
        var gameGrain = _multiplexer.GetGrain<IGameRpcGrain>("game");
        await gameGrain.ConnectPlayer(playerId);
    }
    
    public void OnZoneChanged(string newZone)
    {
        // Just update context - no connection recreation needed
        _multiplexer.SetRoutingContext(new RoutingContext 
        { 
            ["Zone"] = newZone 
        });
    }
}
```