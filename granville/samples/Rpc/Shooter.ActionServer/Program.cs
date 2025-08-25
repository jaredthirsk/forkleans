using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Hosting.Server;
using Shooter.ActionServer;
using Shooter.ActionServer.Services;
using Shooter.ActionServer.Simulation;
using Shooter.ServiceDefaults;
using System.Net;
using System.Net.Sockets;
using Shooter.ActionServer.Grains;
using Shooter.Shared.Models;
using System.Numerics;
using Shooter.Shared.RpcInterfaces;
using Shooter.Shared.GrainInterfaces;
using Orleans.Configuration;
using Granville.Rpc;
using Granville.Rpc.Hosting;
using Granville.Rpc.Transport.LiteNetLib;
using Granville.Rpc.Transport.Ruffles;
using Orleans.Hosting;
using Orleans.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Runtime.Loader;
using System.Reflection;
using Shooter.ActionServer.Configuration;
using Shooter.ActionServer.Hubs;
using Shooter.ActionServer.Views;

// Initialize assembly redirect helper to redirect Orleans.* to Granville.Orleans.*
Shooter.Shared.AssemblyRedirectHelper.Initialize();
Shooter.Shared.AssemblyRedirectHelper.PreloadGranvilleAssemblies();

// Parse command line arguments
var transportType = args.FirstOrDefault(arg => arg.StartsWith("--transport="))?.Replace("--transport=", "") ?? "litenetlib";
var enablePhaserView = args.Any(arg => arg == "--phaser-view" || arg == "--phaser") || 
                       Environment.GetEnvironmentVariable("ENABLE_PHASER_VIEW")?.ToLower() == "true";

Console.WriteLine($"🚀 ActionServer starting with args: {string.Join(" ", args)}");
Console.WriteLine($"🎮 Phaser view enabled: {enablePhaserView} (from args: {args.Any(arg => arg == "--phaser-view" || arg == "--phaser")}, from env: {Environment.GetEnvironmentVariable("ENABLE_PHASER_VIEW")})");
Console.WriteLine($"🌐 Transport type: {transportType}");

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Configure logging
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Granville.Rpc", LogLevel.Warning);  // Reduce RPC noise
builder.Logging.AddFilter("Granville.Rpc.RpcConnection", LogLevel.Error);  // Especially connection noise
builder.Logging.AddFilter("Orleans.Rpc.Server.RpcConnection", LogLevel.Error);  // Alternative namespace
builder.Logging.AddFilter("Shooter.ActionServer", LogLevel.Information);

// Explicitly filter out trace and debug from all RPC components
builder.Logging.AddFilter((category, level) => 
    (category?.StartsWith("Granville.Rpc") == true || category?.StartsWith("Orleans.Rpc") == true) 
    && level < LogLevel.Warning);

// Add file logging with unique filename based on Aspire instance
string logFileName = "../logs/actionserver.log";

// Try to get instance ID from ASPIRE_INSTANCE_ID environment variable (set in AppHost)
var aspireInstanceId = Environment.GetEnvironmentVariable("ASPIRE_INSTANCE_ID");
if (!string.IsNullOrEmpty(aspireInstanceId))
{
    logFileName = $"../logs/actionserver-{aspireInstanceId}.log";
}
else
{
    // Fallback: try to get the replica index from Aspire
    var replicaIndex = Environment.GetEnvironmentVariable("DOTNET_ASPIRE_REPLICA_INDEX");
    if (!string.IsNullOrEmpty(replicaIndex))
    {
        logFileName = $"../logs/actionserver-{replicaIndex}.log";
    }
}

builder.Logging.AddProvider(new FileLoggerProvider(logFileName));

// Add console redirection for stdout and stderr
var consoleLogFileName = logFileName.Replace(".log", "-console.log");
var consoleLogDir = Path.GetDirectoryName(consoleLogFileName);
if (!string.IsNullOrEmpty(consoleLogDir))
{
    Directory.CreateDirectory(consoleLogDir);
}
var consoleWriter = new StreamWriter(consoleLogFileName, append: true) { AutoFlush = true };
var consoleLock = new object();

// Store original console outputs for restoration
var originalOut = Console.Out;
var originalError = Console.Error;

// Redirect console outputs
Console.SetOut(new ConsoleRedirector(originalOut, consoleWriter, "OUT", consoleLock));
Console.SetError(new ConsoleRedirector(originalError, consoleWriter, "ERR", consoleLock));

// Register for cleanup on shutdown
builder.Services.AddSingleton(consoleWriter);
builder.Services.AddHostedService<ConsoleRedirectorCleanupService>();

Console.WriteLine($"ActionServer logging to: {logFileName}");
Console.WriteLine($"Console output logging to: {consoleLogFileName}");

// Set up unhandled exception handlers to ensure they're captured
AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
{
    var ex = args.ExceptionObject as Exception;
    Console.Error.WriteLine($"Unhandled exception in AppDomain: {ex}");
};

TaskScheduler.UnobservedTaskException += (sender, args) =>
{
    Console.Error.WriteLine($"Unobserved task exception: {args.Exception}");
    args.SetObserved(); // Prevent process termination
};

// Add services
builder.Services.AddHealthChecks()
    .AddCheck<Shooter.ActionServer.HealthChecks.ActionServerHealthCheck>("actionserver", tags: new[] { "ready" });

// Add metrics health check
builder.Services.AddMetricsHealthCheck("ActionServer");
builder.Services.AddSingleton<GameEventBroker>();
builder.Services.AddSingleton<CrossZoneRpcService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<CrossZoneRpcService>());
builder.Services.AddSingleton<WorldSimulation>(); // Register as concrete type
builder.Services.AddSingleton<IWorldSimulation>(provider => provider.GetRequiredService<WorldSimulation>()); // Also register as interface
builder.Services.AddHostedService(provider => provider.GetRequiredService<WorldSimulation>()); // Use same instance as hosted service
builder.Services.AddSingleton<GameService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<GameService>()); // GameService is also a hosted service

builder.Services.AddHttpClient();
builder.Services.AddSingleton<RpcServerPortProvider>();

// Add Phaser view configuration
builder.Services.AddSingleton(new PhaserViewConfiguration { IsEnabled = enablePhaserView });
if (enablePhaserView)
{
    Console.WriteLine("Phaser view is ENABLED for this ActionServer");
    // Add SignalR for real-time updates to Phaser view
    builder.Services.AddSignalR();
}

// Register zone-aware RPC server adapter
builder.Services.AddSingleton<IZoneAwareRpcServer, ZoneAwareRpcServerAdapter>();

// Add Orleans startup delay service
builder.Services.AddHostedService<OrleansStartupDelayService>();

// IMPORTANT: Configure Orleans client BEFORE RPC to ensure correct service registration order
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
        options.ClusterId = builder.Configuration["Orleans:ClusterId"] ?? "dev";
        options.ServiceId = builder.Configuration["Orleans:ServiceId"] ?? "ShooterDemo";
    });
});

// Configure serialization to include grain interface assemblies
builder.Services.AddSerializer(serializerBuilder =>
{
    serializerBuilder.AddAssembly(typeof(Shooter.Shared.GrainInterfaces.IWorldManagerGrain).Assembly);
    // Add RPC protocol assembly for RPC message serialization
    serializerBuilder.AddAssembly(typeof(Granville.Rpc.Protocol.RpcMessage).Assembly);
});

// Diagnostic: Check for generated proxy types
var sharedAssembly = typeof(Shooter.Shared.GrainInterfaces.IWorldManagerGrain).Assembly;
Console.WriteLine($"Checking assembly: {sharedAssembly.FullName}");
var generatedTypes = sharedAssembly.GetTypes()
    .Where(t => t.Namespace?.StartsWith("GranvilleCodeGen") == true);
foreach (var type in generatedTypes)
{
    Console.WriteLine($"Found generated type: {type.FullName}");
}

// Configure dynamic HTTP port assignment
if (builder.Environment.IsDevelopment() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    // Only use dynamic port assignment if ASPNETCORE_URLS isn't set (i.e., not running under Aspire)
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
    
    // Configure transport based on command line option
    switch (transportType.ToLowerInvariant())
    {
        case "ruffles":
            Console.WriteLine("Using Ruffles UDP transport");
            rpcBuilder.UseRuffles();
            break;
        case "litenetlib":
        default:
            Console.WriteLine("Using LiteNetLib UDP transport");
            rpcBuilder.UseLiteNetLib();
            break;
    }
    
    // Add assemblies containing grains
    rpcBuilder.AddAssemblyContaining<GameRpcGrain>()           // Grain implementations
             .AddAssemblyContaining<IGameRpcGrain>()           // Grain interfaces
             .AddAssemblyContaining<Shooter.Shared.Models.PlayerInfo>();  // Shared models for serialization
});


// Add background service for registration
builder.Services.AddHostedService<ActionServerRegistrationService>();

// Add stats reporting service
builder.Services.AddHostedService<StatsReportingService>();

// Add status reporting service
builder.Services.AddHostedService<StatusReportingService>();

// Add diagnostic service
// Diagnostic service removed - was causing unnecessary log noise
// builder.Services.AddHostedService<DiagnosticService>();

var app = builder.Build();

app.MapDefaultEndpoints();

// Map additional health checks (MapDefaultEndpoints already maps /health)
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

// Add game endpoints
app.MapGet("/", (PhaserViewConfiguration phaserConfig) => 
{
    if (phaserConfig.IsEnabled)
    {
        return Results.Redirect("/phaser");
    }
    return Results.Text("ActionServer is running");
});

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

// Add Phaser view endpoints if enabled
if (enablePhaserView)
{
    Console.WriteLine("🎮 Setting up Phaser view endpoints...");
    app.UseDefaultFiles();
    app.UseStaticFiles();
    
    app.MapGet("/phaser", () => {
        Console.WriteLine("📡 Phaser view requested (default zone)");
        return Results.Content(PhaserViewHtml.GetHtml(), "text/html");
    });
    
    app.MapGet("/phaser/{x:int}/{y:int}", (int x, int y) => {
        Console.WriteLine($"📡 Phaser view requested for zone ({x}, {y})");
        return Results.Content(PhaserViewHtml.GetHtml(), "text/html");
    });
    
    app.MapHub<WorldStateHub>("/worldStateHub");
    
    app.MapGet("/api/phaser/config", (IWorldSimulation simulation, CrossZoneRpcService crossZoneRpc, RpcServerPortProvider rpcPortProvider, HttpContext context) =>
    {
        var assignedSquare = simulation.GetAssignedSquare();
        
        // Check if zone coordinates are provided in query parameters
        int? requestedX = null, requestedY = null;
        if (context.Request.Query.TryGetValue("x", out var xValue) && int.TryParse(xValue, out var x))
            requestedX = x;
        if (context.Request.Query.TryGetValue("y", out var yValue) && int.TryParse(yValue, out var y))
            requestedY = y;
            
        var targetZone = (requestedX.HasValue && requestedY.HasValue) 
            ? new { X = requestedX.Value, Y = requestedY.Value }
            : new { assignedSquare.X, assignedSquare.Y };
            
        return new
        {
            AssignedZone = new { assignedSquare.X, assignedSquare.Y },
            TargetZone = targetZone,
            RpcPort = rpcPortProvider.Port,
            ServerInstanceId = Environment.GetEnvironmentVariable("ASPIRE_INSTANCE_ID") ?? "default"
        };
    });
    
    app.MapGet("/api/phaser/adjacent-zones", async (CrossZoneRpcService crossZoneRpc, Orleans.IClusterClient orleansClient) =>
    {
        try
        {
            var worldManager = orleansClient.GetGrain<IWorldManagerGrain>(0);
            var actionServers = await worldManager.GetAllActionServers();
            
            // Return all action servers with their zone assignments
            var result = actionServers.Select(s => new 
            {
                s.ServerId,
                Zone = new { s.AssignedSquare.X, s.AssignedSquare.Y },
                s.IpAddress,
                s.HttpEndpoint,
                s.RpcPort
            });
            
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to get adjacent zones: {ex.Message}");
        }
    });
}

// Show the actual URLs the server is listening on
var server = app.Services.GetRequiredService<IServer>();
var addressFeature = server.Features.Get<IServerAddressesFeature>();
if (addressFeature != null)
{
    Console.WriteLine("🌐 ActionServer listening on:");
    foreach (var address in addressFeature.Addresses)
    {
        Console.WriteLine($"   {address}");
        if (enablePhaserView)
        {
            var phaserUrl = address.EndsWith('/') ? $"{address}phaser" : $"{address}/phaser";
            Console.WriteLine($"   🎮 Phaser view: {phaserUrl}");
        }
    }
}

app.Run();

