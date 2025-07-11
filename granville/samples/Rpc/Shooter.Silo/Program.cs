using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Serialization;
using Shooter.Silo;
using Shooter.Silo.Configuration;
using Shooter.Silo.Controllers;
using Shooter.ServiceDefaults;
using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Runtime.Loader;
// using UFX.Orleans.SignalRBackplane; // Temporarily disabled - depends on Microsoft.Orleans 8.2.0

// Assembly redirect system disabled - relying on proper shim compilation instead
// Shooter.Shared.AssemblyRedirectHelper.Initialize();
// Shooter.Shared.AssemblyRedirectHelper.PreloadGranvilleAssemblies();

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Configure file logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var logFileName = "../logs/silo.log";
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

Console.WriteLine($"Silo logging to: {logFileName}");
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
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks()
    .AddCheck<Shooter.Silo.HealthChecks.OrleansHealthCheck>("orleans", tags: new[] { "ready" });

// Add SignalR
builder.Services.AddSignalR();

// Add metrics health check  
builder.Services.AddMetricsHealthCheck("Silo");
builder.Services.AddHttpClient();

// Configure GameSettings
builder.Services.Configure<GameSettings>(builder.Configuration.GetSection("GameSettings"));

// Add ActionServer management service
builder.Services.AddSingleton<Shooter.Silo.Services.ActionServerManager>();
builder.Services.AddHostedService<Shooter.Silo.Services.ActionServerManager>(provider => 
    provider.GetRequiredService<Shooter.Silo.Services.ActionServerManager>());

// Add WorldManagerGrain initializer to ensure timer functionality works
builder.Services.AddHostedService<Shooter.Silo.Services.WorldManagerInitializer>();

// Add CORS to allow client to call API and SignalR
builder.Services.AddCors(options =>
{
    options.AddPolicy("GameClients", policy =>
    {
        policy.WithOrigins(
                "http://localhost:5000",
                "https://localhost:5001",
                "http://localhost:5173",  // Vite dev server
                "https://localhost:5173"
              )
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials(); // Required for SignalR
    });
});

// Configure Orleans
builder.Host.UseOrleans(siloBuilder =>
{
    siloBuilder
        .UseLocalhostClustering(
            siloPort: 11111,
            gatewayPort: 30000,
            primarySiloEndpoint: new IPEndPoint(IPAddress.Loopback, 11111))
        .Configure<ClusterOptions>(options =>
        {
            options.ClusterId = "dev";
            options.ServiceId = "ShooterDemo";
        })
        .Configure<EndpointOptions>(options =>
        {
            options.AdvertisedIPAddress = IPAddress.Loopback;
        })
        .AddMemoryGrainStorage("worldStore")
        .AddMemoryGrainStorage("playerStore")
        .AddMemoryGrainStorage("statsStore")  // Fix Issue 3: Add missing statsStore
        // .AddMemoryGrainStorage(UFX.Orleans.SignalRBackplane.Constants.StorageName) // UFX temporarily disabled
        .UseInMemoryReminderService();
        // .AddSignalRBackplane();  // UFX temporarily disabled - Enable SignalR backplane for multi-silo support
});

// TODO: Configure RPC client so Silo can communicate with ActionServers
// For now, commenting out complex RPC client configuration to fix build
// The main issues (missing storage provider) are fixed above

// Configure serialization to include grain assemblies
builder.Services.AddSerializer(serializerBuilder =>
{
    serializerBuilder.AddAssembly(typeof(Shooter.Silo.Grains.WorldManagerGrain).Assembly);
    serializerBuilder.AddAssembly(typeof(Shooter.Shared.GrainInterfaces.IWorldManagerGrain).Assembly);
    // Add Orleans.Serialization assembly to get ImmutableArray codec and Response types
    serializerBuilder.AddAssembly(typeof(Orleans.Serialization.Codecs.ImmutableArrayCodec<>).Assembly);
    // Add Orleans core assemblies that contain types like GrainManifest, MembershipTableData
    serializerBuilder.AddAssembly(typeof(Orleans.Metadata.GrainManifest).Assembly); // Orleans.Core.Abstractions
    serializerBuilder.AddAssembly(typeof(Orleans.MembershipTableData).Assembly); // Orleans.Core
    // Add Orleans.Runtime assembly for internal types
    serializerBuilder.AddAssembly(typeof(Orleans.Runtime.SiloAddress).Assembly); // Orleans.Runtime
    // Add Granville.Orleans.Persistence.Memory for storage-related types
    serializerBuilder.AddAssembly(typeof(Orleans.Storage.MemoryGrainStorage).Assembly); // Granville.Orleans.Persistence.Memory
    // TODO: Add RPC interfaces when RPC client is properly configured
    // serializerBuilder.AddAssembly(typeof(Shooter.Shared.RpcInterfaces.IGameRpcGrain).Assembly);
});

// Configure to use the ObjectCopier for types without specific copiers (development only)
builder.Services.Configure<Orleans.Serialization.Configuration.TypeManifestOptions>(options =>
{
    options.AllowAllTypes = true; // Allow all types for development
});

// Add the serialization host services that include internal codecs
builder.Services.AddSerializer();

// Add trace logging for Orleans type discovery and grain registration
builder.Logging.AddFilter("Orleans.Metadata", LogLevel.Trace);
builder.Logging.AddFilter("Orleans.Runtime.Catalog", LogLevel.Trace);
builder.Logging.AddFilter("Orleans.Runtime.Placement", LogLevel.Trace);

// Force load grain assembly to ensure it's available for discovery
var grainAssembly = typeof(Shooter.Silo.Grains.WorldManagerGrain).Assembly;
Console.WriteLine($"Loaded grain assembly: {grainAssembly.FullName}");
foreach (var type in grainAssembly.GetTypes())
{
    if (type.IsClass && !type.IsAbstract && typeof(Orleans.Grain).IsAssignableFrom(type))
    {
        Console.WriteLine($"Found grain implementation: {type.FullName}");
    }
}

var app = builder.Build();

app.MapDefaultEndpoints();

// Map health checks with specific routes
// Note: MapDefaultEndpoints() may already map /health, so we'll use different paths
app.MapHealthChecks("/healthz");
app.MapHealthChecks("/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("GameClients"); // Enable CORS with our policy
app.UseAuthorization();
app.MapControllers();

// Map SignalR hub
app.MapHub<Shooter.Silo.Hubs.GameHub>("/gamehub");

// Add a simple endpoint to check if Orleans is ready
app.MapGet("/orleans-ready", (Orleans.IGrainFactory grainFactory) =>
{
    return Results.Ok(new { Ready = true });
});

app.Run();

// Service to cleanup console redirection on shutdown
public class ConsoleRedirectorCleanupService : IHostedService
{
    private readonly StreamWriter _consoleWriter;
    private readonly ILogger<ConsoleRedirectorCleanupService> _logger;

    public ConsoleRedirectorCleanupService(StreamWriter consoleWriter, ILogger<ConsoleRedirectorCleanupService> logger)
    {
        _consoleWriter = consoleWriter;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cleaning up console redirection...");
        
        try
        {
            // Flush and close the console writer
            _consoleWriter.Flush();
            _consoleWriter.Close();
            _consoleWriter.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during console redirection cleanup");
        }
        
        return Task.CompletedTask;
    }
}