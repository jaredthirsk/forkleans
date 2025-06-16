using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Hosting;
using Forkleans.Rpc;
using Forkleans.Rpc.Hosting;
using Forkleans.Rpc.Transport;
using Forkleans.Rpc.Transport.LiteNetLib;
using Shooter.ActionServer.Grains;
using Shooter.ActionServer.Services;
using Shooter.ActionServer.Simulation;
using Shooter.Shared.Models;
using Shooter.Shared.RpcInterfaces;
using System.Net;
using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);

// Let the host environment (Aspire, Docker, etc.) assign ports dynamically
// Only use explicit URLs if provided via command line or environment
if (string.IsNullOrEmpty(builder.Configuration["urls"]) && 
    string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    // No URLs specified, use dynamic port assignment
    // Use 127.0.0.1:0 instead of localhost:0 as required by Kestrel
    builder.WebHost.UseUrls("http://127.0.0.1:0");
}

builder.AddServiceDefaults();

// Add a startup delay to allow Orleans silo to fully initialize
builder.Services.AddHostedService<OrleansStartupDelayService>();

// Configure Orleans client
builder.Services.AddOrleansClient((Orleans.Hosting.IClientBuilder clientBuilder) =>
{
    var gatewayEndpoint = builder.Configuration["Orleans:GatewayEndpoint"];
    
    if (!string.IsNullOrEmpty(gatewayEndpoint))
    {
        // Parse the endpoint from Aspire (format: "tcp://host:port" or "host:port")
        var uri = new Uri(gatewayEndpoint);
        var host = uri.Host;
        var port = uri.Port;
        
        clientBuilder
            .UseStaticClustering(new IPEndPoint(IPAddress.Parse(host == "localhost" ? "127.0.0.1" : host), port))
            .Configure<ClusterOptions>(options =>
            {
                options.ClusterId = "dev";
                options.ServiceId = "ShooterDemo";
            });
    }
    else
    {
        // Fallback to localhost clustering for local development without Aspire
        clientBuilder
            .UseLocalhostClustering()
            .Configure<ClusterOptions>(options =>
            {
                options.ClusterId = "dev";
                options.ServiceId = "ShooterDemo";
            });
    }
});

// Add HTTP client for registering with silo
builder.Services.AddHttpClient();

// Add world simulation
builder.Services.AddSingleton<IWorldSimulation, WorldSimulation>();
builder.Services.AddHostedService(provider => (WorldSimulation)provider.GetRequiredService<IWorldSimulation>());

// Add game service
builder.Services.AddSingleton<GameService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<GameService>());

// UDP game server removed - using Forkleans RPC instead

// Add Forkleans RPC server alongside Orleans client
// After fixing the property keys in Forkleans, RPC can now coexist with Orleans
// Configure RPC server port tracking
var rpcPortProvider = new RpcServerPortProvider();
builder.Services.AddSingleton(rpcPortProvider);

// Determine RPC port based on instance ID or environment
int rpcPort;

// First, try to get port from environment variable (useful for Aspire)
var envPort = Environment.GetEnvironmentVariable("RPC_PORT");
if (!string.IsNullOrEmpty(envPort) && int.TryParse(envPort, out var parsedPort))
{
    rpcPort = parsedPort;
    builder.Logging.AddConsole();
    Console.WriteLine($"Using RPC port from environment: {rpcPort}");
}
else
{
    // Try to get instance ID from Aspire
    var instanceId = Environment.GetEnvironmentVariable("ASPIRE_INSTANCE_ID");
    var baseRpcPort = 12000;
    
    if (!string.IsNullOrEmpty(instanceId) && int.TryParse(instanceId, out var id))
    {
        // Use instance ID to calculate port offset
        rpcPort = baseRpcPort + id;
        Console.WriteLine($"Using RPC port based on instance ID {id}: {rpcPort}");
    }
    else
    {
        // Fallback: find an available port with retry and random delay
        var random = new Random();
        await Task.Delay(random.Next(100, 500)); // Random delay to reduce race conditions
        
        rpcPort = 0;
        for (int port = baseRpcPort; port < baseRpcPort + 1000; port++)
        {
            try
            {
                using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
                socket.Bind(new IPEndPoint(IPAddress.Any, port));
                socket.Close();
                rpcPort = port;
                break;
            }
            catch
            {
                // Port is in use, try next
            }
        }
        
        if (rpcPort == 0)
        {
            throw new InvalidOperationException($"No available ports found in range {baseRpcPort}-{baseRpcPort + 999}");
        }
        Console.WriteLine($"Found available RPC port: {rpcPort}");
    }
}

rpcPortProvider.SetPort(rpcPort);
builder.Configuration["RpcPort"] = rpcPort.ToString();

builder.Host.UseOrleansRpc(rpcBuilder =>
{
    // Use the selected port
    rpcBuilder.ConfigureEndpoint(rpcPort);
    
    // Use LiteNetLib transport for UDP
    rpcBuilder.UseLiteNetLib();
    
    // Add assemblies containing grains
    rpcBuilder.AddAssemblyContaining<GameRpcGrain>()           // Grain implementations
             .AddAssemblyContaining<IGameRpcGrain>();          // Grain interfaces
});

// Add background service for registration
builder.Services.AddHostedService<ActionServerRegistrationService>();

var app = builder.Build();

app.MapDefaultEndpoints();

// Add game endpoints
app.MapGet("/", () => "ActionServer is running");

app.MapGet("/status", (IWorldSimulation simulation) => 
{
    var state = simulation.GetCurrentState();
    return new
    {
        EntityCount = state.Entities.Count,
        Timestamp = state.Timestamp
    };
});

// Game endpoints
app.MapPost("/game/connect/{playerId}", async (string playerId, GameService gameService) =>
{
    var connected = await gameService.ConnectPlayer(playerId);
    return Results.Ok(new { Connected = connected });
});

app.MapDelete("/game/disconnect/{playerId}", async (string playerId, GameService gameService) =>
{
    await gameService.DisconnectPlayer(playerId);
    return Results.Ok();
});

app.MapGet("/game/state", async (GameService gameService) =>
{
    var state = await gameService.GetWorldState();
    return Results.Ok(state);
});

app.MapPost("/game/input/{playerId}", async (string playerId, PlayerInput input, GameService gameService) =>
{
    await gameService.UpdatePlayerInput(playerId, input.MoveDirection, input.IsShooting);
    return Results.Ok();
});

// Entity transfers now happen via RPC only

app.Run();

// Background service to handle registration with Orleans silo
public class ActionServerRegistrationService : BackgroundService
{
    private readonly ILogger<ActionServerRegistrationService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWorldSimulation _simulation;
    private readonly IConfiguration _configuration;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IServiceProvider _serviceProvider;
    private readonly RpcServerPortProvider _rpcPortProvider;
    private string? _serverId;
    private string? _siloUrl;

    public ActionServerRegistrationService(
        ILogger<ActionServerRegistrationService> logger,
        IHttpClientFactory httpClientFactory,
        IWorldSimulation simulation,
        IConfiguration configuration,
        IHostApplicationLifetime lifetime,
        IServiceProvider serviceProvider,
        RpcServerPortProvider rpcPortProvider)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _simulation = simulation;
        _configuration = configuration;
        _lifetime = lifetime;
        _serviceProvider = serviceProvider;
        _rpcPortProvider = rpcPortProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for the app to start
        var tcs = new TaskCompletionSource();
        using var registration = _lifetime.ApplicationStarted.Register(() => tcs.SetResult());
        await tcs.Task;
        
        var httpClient = _httpClientFactory.CreateClient();
        
        // Wait a bit for the server to fully start
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        
        // Get the server's actual addresses
        var server = _serviceProvider.GetService<IServer>();
        var addresses = server?.Features.Get<IServerAddressesFeature>()?.Addresses;
        
        string? actualAddress = null;
        int actualPort = 5100;
        
        if (addresses != null && addresses.Any())
        {
            actualAddress = addresses.First();
            _logger.LogInformation("Server listening on: {Addresses}", string.Join(", ", addresses));
            
            var uri = new Uri(actualAddress);
            actualPort = uri.Port;
        }
        else
        {
            // Fallback to environment variable or configuration
            var serverAddresses = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") 
                ?? _configuration["urls"] 
                ?? "http://localhost:5100";
            
            _logger.LogInformation("Using configured addresses: {Addresses}", serverAddresses);
            
            var firstAddress = serverAddresses.Split(';')[0];
            var uri = new Uri(firstAddress);
            actualPort = uri.Port;
        }
        
        _serverId = Guid.NewGuid().ToString();
        _siloUrl = _configuration["Orleans:SiloUrl"] ?? "https://localhost:7071";
        
        _logger.LogInformation("ActionServer starting with ID: {ServerId}, HTTP Port: {Port}", _serverId, actualPort);
        
        // Wait for RPC server to start and get its port
        int rpcPort;
        try
        {
            _logger.LogInformation("Waiting for RPC server to start...");
            rpcPort = await _rpcPortProvider.WaitForPortAsync(TimeSpan.FromSeconds(10), stoppingToken);
            _logger.LogInformation("RPC server started on port {Port}", rpcPort);
        }
        catch (TimeoutException)
        {
            _logger.LogError("RPC server failed to start within timeout");
            return;
        }
        
        // Retry registration with exponential backoff
        var retryCount = 0;
        var maxRetries = 10;
        var registered = false;
        
        while (!registered && retryCount < maxRetries && !stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Attempting to register with silo (attempt {Attempt}/{MaxAttempts})", retryCount + 1, maxRetries);
                
                // Determine the best IP/hostname to use
                string ipAddress;
                
                // First try Aspire service instance name
                var serviceName = Environment.GetEnvironmentVariable("ASPIRE_SERVICE_INSTANCE_NAME");
                if (!string.IsNullOrEmpty(serviceName))
                {
                    ipAddress = serviceName;
                    _logger.LogInformation("Using Aspire service name: {IpAddress}", ipAddress);
                }
                else if (!string.IsNullOrEmpty(actualAddress))
                {
                    // Extract hostname from the actual HTTP address
                    try
                    {
                        var uri = new Uri(actualAddress);
                        ipAddress = uri.Host;
                        _logger.LogInformation("Using hostname from HTTP endpoint: {IpAddress}", ipAddress);
                    }
                    catch
                    {
                        ipAddress = "127.0.0.1";
                        _logger.LogWarning("Failed to extract hostname, using localhost");
                    }
                }
                else
                {
                    ipAddress = "127.0.0.1";
                    _logger.LogInformation("Using default localhost");
                }
                
                _logger.LogInformation("Registering with IP/hostname: {IpAddress}", ipAddress);
                
                var response = await httpClient.PostAsJsonAsync(
                    $"{_siloUrl}/api/world/action-servers/register",
                    new { ServerId = _serverId, IpAddress = ipAddress, UdpPort = rpcPort, HttpEndpoint = actualAddress, RpcPort = rpcPort },
                    stoppingToken);
                    
                if (response.IsSuccessStatusCode)
                {
                    var serverInfo = await response.Content.ReadFromJsonAsync<ActionServerInfo>(cancellationToken: stoppingToken);
                    if (serverInfo != null)
                    {
                        ((WorldSimulation)_simulation).SetAssignedSquare(serverInfo.AssignedSquare);
                        _logger.LogInformation("Registered with silo, assigned square: ({X}, {Y})", 
                            serverInfo.AssignedSquare.X, serverInfo.AssignedSquare.Y);
                        registered = true;
                    }
                }
                else
                {
                    _logger.LogWarning("Failed to register with silo: {Status}", response.StatusCode);
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Failed to connect to silo, will retry");
            }
            
            if (!registered)
            {
                retryCount++;
                var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, retryCount), 30)); // Exponential backoff with 30s max
                _logger.LogInformation("Waiting {Delay} before retry", delay);
                await Task.Delay(delay, stoppingToken);
            }
        }
        
        if (!registered)
        {
            _logger.LogError("Failed to register with silo after {MaxRetries} attempts", maxRetries);
            throw new InvalidOperationException("Unable to register with Orleans silo");
        }
        
        try
        {
            // Keep the registration alive
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ActionServer registration failed");
        }
        finally
        {
            // Unregister on shutdown
            if (!string.IsNullOrEmpty(_serverId) && !string.IsNullOrEmpty(_siloUrl))
            {
                try
                {
                    await httpClient.DeleteAsync($"{_siloUrl}/api/world/action-servers/{_serverId}");
                    _logger.LogInformation("Unregistered from silo");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to unregister from silo");
                }
            }
        }
    }
}

public record PlayerInput(Vector2 MoveDirection, bool IsShooting);

// Service to delay Orleans client startup to ensure silo is ready
public class OrleansStartupDelayService : IHostedService
{
    private readonly ILogger<OrleansStartupDelayService> _logger;
    private readonly IConfiguration _configuration;

    public OrleansStartupDelayService(ILogger<OrleansStartupDelayService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Only delay if running in Aspire (gateway endpoint is configured)
        if (!string.IsNullOrEmpty(_configuration["Orleans:GatewayEndpoint"]))
        {
            _logger.LogInformation("Delaying Orleans client startup to ensure silo is ready...");
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            _logger.LogInformation("Orleans startup delay complete");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

// TODO: Implement RPC server hosted service when Orleans RPC integration is configured
// Hosted service to manage RPC server lifecycle
// public class RpcServerHostedService : IHostedService
// {
//     private readonly IRpcServer _rpcServer;
//     private readonly ILogger<RpcServerHostedService> _logger;
//     private readonly IConfiguration _configuration;
//     private int _actualPort;
// 
//     public RpcServerHostedService(IRpcServer rpcServer, ILogger<RpcServerHostedService> logger, IConfiguration configuration)
//     {
//         _rpcServer = rpcServer;
//         _logger = logger;
//         _configuration = configuration;
//     }
// 
//     public async Task StartAsync(CancellationToken cancellationToken)
//     {
//         try
//         {
//             await _rpcServer.StartAsync(cancellationToken);
//             
//             // Get the actual port that was bound
//             var transport = _rpcServer.Transport as LiteNetLibTransport;
//             if (transport != null)
//             {
//                 _actualPort = transport.LocalPort;
//                 _logger.LogInformation("RPC server started on UDP port {Port}", _actualPort);
//                 
//                 // Store the UDP port in configuration for registration
//                 _configuration["RpcServer:UdpPort"] = _actualPort.ToString();
//             }
//             else
//             {
//                 _logger.LogError("Failed to get RPC server port - transport is not LiteNetLib");
//             }
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(ex, "Failed to start RPC server");
//             throw;
//         }
//     }
// 
//     public async Task StopAsync(CancellationToken cancellationToken)
//     {
//         try
//         {
//             await _rpcServer.StopAsync(cancellationToken);
//             _logger.LogInformation("RPC server stopped");
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(ex, "Error stopping RPC server");
//         }
//     }
// }