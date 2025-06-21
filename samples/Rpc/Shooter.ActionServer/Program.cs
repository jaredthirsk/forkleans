using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Hosting.Server;
using Shooter.ActionServer;
using Shooter.ActionServer.Services;
using Shooter.ActionServer.Simulation;
using System.Net;
using System.Net.Sockets;
using Shooter.ActionServer.Grains;
using Shooter.Shared.Models;
using System.Numerics;
using Shooter.Shared.RpcInterfaces;
using Forkleans.Configuration;
using Forkleans.Rpc;
using Forkleans.Rpc.Hosting;
using Forkleans.Rpc.Transport.LiteNetLib;
using Orleans.Configuration;
using Orleans.Hosting;
using Forkleans.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Configure logging
builder.Logging.SetMinimumLevel(LogLevel.Debug);
builder.Logging.AddFilter("Forkleans.Rpc", LogLevel.Debug);
builder.Logging.AddFilter("Shooter.ActionServer", LogLevel.Debug);

// Add file logging with unique filename based on Aspire instance
string logFileName = "logs/actionserver.log";

// Try to get instance ID from ASPIRE_INSTANCE_ID environment variable (set in AppHost)
var aspireInstanceId = Environment.GetEnvironmentVariable("ASPIRE_INSTANCE_ID");
if (!string.IsNullOrEmpty(aspireInstanceId))
{
    logFileName = $"logs/actionserver-{aspireInstanceId}.log";
}
else
{
    // Fallback: try to get the replica index from Aspire
    var replicaIndex = Environment.GetEnvironmentVariable("DOTNET_ASPIRE_REPLICA_INDEX");
    if (!string.IsNullOrEmpty(replicaIndex))
    {
        logFileName = $"logs/actionserver-{replicaIndex}.log";
    }
}

builder.Logging.AddProvider(new FileLoggerProvider(logFileName));
Console.WriteLine($"ActionServer logging to: {logFileName}");

// Add services
builder.Services.AddSingleton<WorldSimulation>(); // Register as concrete type
builder.Services.AddSingleton<IWorldSimulation>(provider => provider.GetRequiredService<WorldSimulation>()); // Also register as interface
builder.Services.AddHostedService(provider => provider.GetRequiredService<WorldSimulation>()); // Use same instance as hosted service
builder.Services.AddSingleton<GameService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<GameService>()); // GameService is also a hosted service
builder.Services.AddHttpClient();
builder.Services.AddSingleton<RpcServerPortProvider>();

// Add Orleans startup delay service
builder.Services.AddHostedService<OrleansStartupDelayService>();

// Configure Orleans client with retry
builder.UseOrleansClient((Orleans.Hosting.IClientBuilder clientBuilder) =>
{
    // In Aspire, use the configured gateway endpoint
    var gatewayEndpoint = builder.Configuration["Orleans:GatewayEndpoint"];
    if (!string.IsNullOrEmpty(gatewayEndpoint))
    {
        // Handle various formats: "host:port", "//host:port", "http://host:port"
        var cleanEndpoint = gatewayEndpoint;
        if (cleanEndpoint.Contains("://"))
        {
            var uri = new Uri(cleanEndpoint);
            cleanEndpoint = $"{uri.Host}:{uri.Port}";
        }
        else if (cleanEndpoint.StartsWith("//"))
        {
            cleanEndpoint = cleanEndpoint.Substring(2);
        }
        
        var parts = cleanEndpoint.Split(':');
        var host = parts[0];
        var port = int.Parse(parts[1]);
        
        // Resolve hostname to IP if needed
        IPAddress? ipAddress;
        if (!IPAddress.TryParse(host, out ipAddress))
        {
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                ipAddress = IPAddress.Loopback;
            }
            else
            {
                var hostEntry = System.Net.Dns.GetHostEntry(host);
                ipAddress = hostEntry.AddressList.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) 
                    ?? IPAddress.Loopback;
            }
        }
        
        clientBuilder.UseStaticClustering(new IPEndPoint(ipAddress, port));
    }
    else
    {
        clientBuilder.UseLocalhostClustering();
    }
    
    // Configure cluster options to match the Silo
    clientBuilder.Configure<Orleans.Configuration.ClusterOptions>(options =>
    {
        options.ClusterId = "dev";
        options.ServiceId = "ShooterDemo";
    });
});

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

app.MapGet("/api/game/zone-stats", (IWorldSimulation simulation) =>
{
    var state = simulation.GetCurrentState();
    
    var factoryCount = state.Entities.Count(e => e.Type == EntityType.Factory && e.State != EntityStateType.Dead);
    var enemyCount = state.Entities.Count(e => e.Type == EntityType.Enemy && e.State != EntityStateType.Dead);
    var playerCount = state.Entities.Count(e => e.Type == EntityType.Player && e.SubType == 0 && e.State != EntityStateType.Dead);
    
    return new ZoneStats(factoryCount, enemyCount, playerCount);
});

// Admin endpoint for graceful shutdown
app.MapPost("/api/admin/shutdown", async (IHostApplicationLifetime lifetime, ILogger<Program> logger) =>
{
    logger.LogWarning("Shutdown requested via admin endpoint");
    
    // Stop accepting new requests
    await Task.Delay(1000);
    
    // Trigger application shutdown
    lifetime.StopApplication();
    
    return Results.Ok(new { message = "Shutdown initiated" });
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

