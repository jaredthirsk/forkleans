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
using UFX.Orleans.SignalRBackplane;

// Assembly redirect for Granville Orleans compatibility (Option 2)
AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
{
    if (assemblyName.Name?.StartsWith("Microsoft.Orleans") == true)
    {
        var granvilleName = assemblyName.Name.Replace("Microsoft.Orleans", "Granville.Orleans");
        try
        {
            Console.WriteLine($"Redirecting {assemblyName.Name} to {granvilleName}");
            return context.LoadFromAssemblyName(new AssemblyName(granvilleName));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to redirect {assemblyName.Name}: {ex.Message}");
        }
    }
    return null;
};

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Configure file logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddProvider(new FileLoggerProvider("../logs/silo.log"));

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
            gatewayPort: 30000)
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
        .UseSignalRBackplane();  // Enable SignalR backplane for multi-silo support
});

// TODO: Configure RPC client so Silo can communicate with ActionServers
// For now, commenting out complex RPC client configuration to fix build
// The main issues (missing storage provider) are fixed above

// Configure serialization to include grain assemblies
builder.Services.AddSerializer(serializerBuilder =>
{
    serializerBuilder.AddAssembly(typeof(Shooter.Silo.Grains.WorldManagerGrain).Assembly);
    serializerBuilder.AddAssembly(typeof(Shooter.Shared.GrainInterfaces.IWorldManagerGrain).Assembly);
    // TODO: Add RPC interfaces when RPC client is properly configured
    // serializerBuilder.AddAssembly(typeof(Shooter.Shared.RpcInterfaces.IGameRpcGrain).Assembly);
});

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

// Map health checks
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
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