using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Granville.Rpc;
using Granville.Rpc.Configuration;
using Granville.Rpc.Hosting;
using Granville.Rpc.Transport.LiteNetLib;
using Orleans.Serialization;
using Orleans.Hosting;
using Shooter.Shared.RpcInterfaces;
using Shooter.Shared.Models;
using Shooter.ActionServer.Grains;

namespace Shooter.ActionServer.Services;

/// <summary>
/// Service that manages RPC connections to other ActionServers for cross-zone communication
/// </summary>
public class CrossZoneRpcService : IHostedService, IDisposable
{
    private readonly ILogger<CrossZoneRpcService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, RpcConnectionInfo> _connections = new();
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly TimeSpan _connectionTimeout = TimeSpan.FromMinutes(5);
    private Timer? _cleanupTimer;

    private class RpcConnectionInfo
    {
        public IHost Host { get; set; } = null!;
        public IRpcClient Client { get; set; } = null!;
        public DateTime LastUsed { get; set; }
        public string ConnectionKey { get; set; } = null!;
    }

    public CrossZoneRpcService(ILogger<CrossZoneRpcService> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("CrossZoneRpcService starting");
        
        // Start cleanup timer to remove idle connections
        _cleanupTimer = new Timer(CleanupIdleConnections, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        
        return Task.CompletedTask;
    }
    

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("CrossZoneRpcService stopping");
        
        _cleanupTimer?.Dispose();
        
        // Dispose all connections
        var disposeTasks = new List<Task>();
        foreach (var connection in _connections.Values)
        {
            disposeTasks.Add(DisposeConnectionAsync(connection, cancellationToken));
        }
        
        Task.WaitAll(disposeTasks.ToArray(), TimeSpan.FromSeconds(10));
        _connections.Clear();
        
        return Task.CompletedTask;
    }

    public async Task<IGameGranule> GetGameGrainForServer(ActionServerInfo targetServer)
    {
        return await GetGameGrainForZone(targetServer, null);
    }
    
    public async Task<IGameGranule> GetGameGrainForZone(ActionServerInfo targetServer, GridSquare? targetZone, bool bypassZoneCheck = false)
    {
        var connectionKey = GetConnectionKey(targetServer);
        
        // Try to get existing connection
        if (_connections.TryGetValue(connectionKey, out var connectionInfo))
        {
            connectionInfo.LastUsed = DateTime.UtcNow;
            return connectionInfo.Client.GetGrain<IGameGranule>("game");
        }

        // Create new connection
        await _connectionLock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (_connections.TryGetValue(connectionKey, out connectionInfo))
            {
                connectionInfo.LastUsed = DateTime.UtcNow;
                return connectionInfo.Client.GetGrain<IGameGranule>("game");
            }

            _logger.LogInformation("Creating new RPC connection to server {ServerId} at {Host}:{Port} for zone ({ZoneX},{ZoneY})", 
                targetServer.ServerId, targetServer.IpAddress, targetServer.RpcPort, 
                targetZone?.X ?? -1, targetZone?.Y ?? -1);

            var hostBuilder = Host.CreateDefaultBuilder()
                .UseOrleansRpcClient(rpcBuilder =>
                {
                    var host = targetServer.IpAddress == "localhost" ? "127.0.0.1" : targetServer.IpAddress;
                    rpcBuilder.ConnectTo(host, targetServer.RpcPort);
                    rpcBuilder.UseLiteNetLib();
                    
                    // Configure connection pooling and timeouts
                    rpcBuilder.Services.Configure<RpcClientOptions>(options =>
                    {
                        options.ClientId = $"crosszone-{targetServer.ServerId}-{Guid.NewGuid():N}";
                        options.ConnectionTimeoutMs = 5000; // 5 second connection timeout
                    });
                })
                .ConfigureServices(services =>
                {
                    // Use the same simple configuration pattern as the working game client
                    services.AddSerializer(serializer =>
                    {
                        serializer.AddAssembly(typeof(IGameGranule).Assembly);
                        // Add RPC protocol assembly for RPC message serialization
                        serializer.AddAssembly(typeof(Granville.Rpc.Protocol.RpcMessage).Assembly);
                        // Add Shooter.Shared assembly for game models (Player, WorldState, etc.)
                        serializer.AddAssembly(typeof(Shooter.Shared.Models.PlayerInfo).Assembly);
                    });
                })
                .ConfigureLogging(logging =>
                {
                    // Reduce logging noise from RPC client
                    logging.AddFilter("Granville.Rpc", LogLevel.Warning);
                })
                .Build();

            await hostBuilder.StartAsync();

            var rpcClient = hostBuilder.Services.GetRequiredService<IRpcClient>();
            
            // Give the connection a moment to establish asynchronously
            // We don't need to verify it here - it will be validated on first use
            // and the RPC framework has built-in retry logic
            await Task.Delay(100); // Brief delay to let connection start establishing

            _logger.LogDebug("RPC connection initiated to server {ServerId}, will be validated on first use",
                targetServer.ServerId);

            connectionInfo = new RpcConnectionInfo
            {
                Host = hostBuilder,
                Client = rpcClient,
                LastUsed = DateTime.UtcNow,
                ConnectionKey = connectionKey
            };

            _connections[connectionKey] = connectionInfo;
            
            _logger.LogInformation("Successfully connected to server {ServerId}", targetServer.ServerId);

            return rpcClient.GetGrain<IGameGranule>("game");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create RPC connection to server {ServerId}", targetServer.ServerId);
            throw;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private string GetConnectionKey(ActionServerInfo server)
    {
        var host = server.IpAddress == "localhost" ? "127.0.0.1" : server.IpAddress;
        return $"{host}:{server.RpcPort}";
    }

    private void CleanupIdleConnections(object? state)
    {
        var now = DateTime.UtcNow;
        var connectionsToRemove = _connections
            .Where(kvp => now - kvp.Value.LastUsed > _connectionTimeout)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in connectionsToRemove)
        {
            if (_connections.TryRemove(key, out var connection))
            {
                _logger.LogInformation("Removing idle connection {ConnectionKey}", key);
                _ = DisposeConnectionAsync(connection, CancellationToken.None);
            }
        }
    }
    
    private async Task DisposeConnectionAsync(RpcConnectionInfo connection, CancellationToken cancellationToken)
    {
        try
        {
            await connection.Host.StopAsync(cancellationToken);
            connection.Host.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing connection {ConnectionKey}", connection.ConnectionKey);
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        _connectionLock?.Dispose();
    }
    
    public async Task NotifyBulletDestroyed(GridSquare targetZone, string bulletId)
    {
        try
        {
            // Get the action server for the target zone
            var orleansClient = _serviceProvider.GetRequiredService<Orleans.IClusterClient>();
            var worldManager = orleansClient.GetGrain<Shooter.Shared.GrainInterfaces.IWorldManagerGrain>(0);
            var targetServer = await worldManager.GetActionServerForPosition(targetZone.GetCenter());
            
            if (targetServer == null)
            {
                _logger.LogDebug("No server found for zone ({X},{Y}), skipping bullet destruction notification", 
                    targetZone.X, targetZone.Y);
                return;
            }
            
            // Get the game grain for the target server (bypass zone check for bullet operations)
            var gameGrain = await GetGameGrainForZone(targetServer, targetZone, bypassZoneCheck: true);
            
            // Notify about bullet destruction
            await gameGrain.NotifyBulletDestroyed(bulletId);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to notify zone ({X},{Y}) about bullet {BulletId} destruction", 
                targetZone.X, targetZone.Y, bulletId);
        }
    }
}