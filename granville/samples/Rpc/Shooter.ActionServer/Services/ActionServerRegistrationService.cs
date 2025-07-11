using Shooter.Shared.GrainInterfaces;
using Shooter.ActionServer.Simulation;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Shooter.ActionServer.Services;

public class ActionServerRegistrationService : BackgroundService
{
    private readonly Orleans.IClusterClient _orleansClient;
    private readonly IConfiguration _configuration;
    private readonly IWorldSimulation _worldSimulation;
    private readonly ILogger<ActionServerRegistrationService> _logger;
    private readonly RpcServerPortProvider _rpcPortProvider;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IServiceProvider _serviceProvider;
    private string? _serverId;

    public ActionServerRegistrationService(
        Orleans.IClusterClient orleansClient,
        IConfiguration configuration,
        IWorldSimulation worldSimulation,
        ILogger<ActionServerRegistrationService> logger,
        RpcServerPortProvider rpcPortProvider,
        IHostApplicationLifetime lifetime,
        IServiceProvider serviceProvider)
    {
        _orleansClient = orleansClient;
        _configuration = configuration;
        _worldSimulation = worldSimulation;
        _logger = logger;
        _rpcPortProvider = rpcPortProvider;
        _lifetime = lifetime;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for the application to be ready
        await Task.Delay(2000, stoppingToken);

        try
        {
            // Generate a unique server ID
            _serverId = $"action-server-{Guid.NewGuid():N}";
            
            // Get the actual HTTP port the server is listening on
            var server = _serviceProvider.GetRequiredService<IServer>();
            var serverAddresses = server.Features.Get<IServerAddressesFeature>();
            var actualAddress = serverAddresses?.Addresses.FirstOrDefault();
            
            string advertisedHost;
            int httpPort;
            
            if (!string.IsNullOrEmpty(actualAddress))
            {
                var actualUri = new Uri(actualAddress);
                var host = actualUri.Host;
                // Replace 0.0.0.0 with localhost
                if (host == "0.0.0.0" || host == "::" || host == "+")
                {
                    host = "localhost";
                }
                advertisedHost = Environment.GetEnvironmentVariable("ADVERTISED_HOST") ?? host;
                httpPort = actualUri.Port;
            }
            else
            {
                // Fallback to configuration
                var urls = _configuration["ASPNETCORE_URLS"] ?? "http://localhost:5000";
                var firstUrl = urls.Split(';')[0];
                var uri = new Uri(firstUrl);
                advertisedHost = Environment.GetEnvironmentVariable("ADVERTISED_HOST") ?? uri.Host;
                httpPort = uri.Port;
            }
            
            // Get the RPC port
            var rpcPort = _rpcPortProvider.Port;
            
            var httpEndpoint = $"http://{advertisedHost}:{httpPort}";
            _logger.LogInformation("Registering ActionServer {ServerId} with Silo - HTTP endpoint: {HttpEndpoint}, RPC port: {RpcPort}", 
                _serverId, httpEndpoint, rpcPort);
            
            // Register with WorldManager
            var worldManager = _orleansClient.GetGrain<IWorldManagerGrain>(0);
            var serverInfo = await worldManager.RegisterActionServer(
                _serverId,
                advertisedHost,
                0, // UDP port not used anymore
                httpEndpoint,
                rpcPort);
            
            _logger.LogInformation("ActionServer registered successfully. Assigned zone: ({X}, {Y})", 
                serverInfo.AssignedSquare.X, serverInfo.AssignedSquare.Y);
            
            // Tell the simulation what zone it's managing
            _worldSimulation.SetAssignedSquare(serverInfo.AssignedSquare);
            
            // Keep the registration alive with periodic heartbeats
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                
                // In a production system, you might want to re-register periodically
                // to handle cases where the silo was restarted
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register ActionServer with Silo");
            
            // Shut down the application if we can't register
            _lifetime.StopApplication();
        }
        finally
        {
            // Unregister on shutdown
            if (!string.IsNullOrEmpty(_serverId))
            {
                try
                {
                    var worldManager = _orleansClient.GetGrain<IWorldManagerGrain>(0);
                    await worldManager.UnregisterActionServer(_serverId);
                    _logger.LogInformation("ActionServer {ServerId} unregistered", _serverId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to unregister ActionServer");
                }
            }
        }
    }
}