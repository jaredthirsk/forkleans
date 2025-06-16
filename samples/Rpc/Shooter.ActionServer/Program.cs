using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Hosting.Server;
using Shooter.ActionServer.Services;
using Shooter.ActionServer.Simulation;
using System.Net;
using Shooter.ActionServer.Grains;
using Shooter.Shared.Models;
using System.Numerics;
using Shooter.Shared.RpcInterfaces;
using Forkleans.Configuration;
using Forkleans.Rpc;
using Forkleans.Rpc.Hosting;
using Forkleans.Rpc.Transport.LiteNetLib;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services
builder.Services.AddSingleton<IWorldSimulation, WorldSimulation>();
builder.Services.AddSingleton<GameService>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<RpcServerPortProvider>();

// Add Orleans startup delay service
builder.Services.AddHostedService<OrleansStartupDelayService>();

// Configure Orleans client with retry
builder.UseOrleansClient((Forkleans.Hosting.IClientBuilder clientBuilder) =>
{
    // In Aspire, use the configured gateway endpoint
    var gatewayEndpoint = builder.Configuration["Orleans:GatewayEndpoint"];
    if (!string.IsNullOrEmpty(gatewayEndpoint))
    {
        var parts = gatewayEndpoint.Split(':');
        var host = parts[0];
        var port = int.Parse(parts[1]);
        clientBuilder.UseStaticClustering(new IPEndPoint(IPAddress.Parse(host), port));
    }
    else
    {
        clientBuilder.UseLocalhostClustering();
    }
});

// Ensure simulation runs after Orleans client is ready
builder.Services.AddHostedService<WorldSimulation>();

// Configure dynamic HTTP port assignment
if (builder.Environment.IsDevelopment())
{
    // Don't use a specific port in development - let the system assign one
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.Listen(IPAddress.Any, 0); // 0 means let the OS assign a port
    });
}

// Determine RPC port BEFORE building the app
var rpcPortProvider = new RpcServerPortProvider();
builder.Services.AddSingleton(rpcPortProvider);

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

// All game communication now happens via RPC only
// HTTP endpoints are only used for health/status monitoring

app.Run();

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

// Service to provide RPC port to other services
public class RpcServerPortProvider
{
    private readonly TaskCompletionSource<int> _portTcs = new();
    
    public void SetPort(int port)
    {
        _portTcs.TrySetResult(port);
    }
    
    public async Task<int> WaitForPortAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        
        try
        {
            return await _portTcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("RPC port was not set within the timeout period");
        }
    }
    
    public int Port => _portTcs.Task.IsCompletedSuccessfully ? _portTcs.Task.Result : 0;
}