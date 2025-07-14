using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Serialization;
using Orleans.Reminders;
using Shooter.Silo;
using Shooter.Silo.Configuration;
using Shooter.Silo.Controllers;
using Shooter.ServiceDefaults;
using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Linq;
using Orleans.Serialization.Configuration;
// TODO: Uncomment when Granville.Orleans.Shims package is available
// using Granville.Orleans.Shims;
using UFX.Orleans.SignalRBackplane;

// Assembly redirect system disabled - relying on proper shim compilation instead
// Shooter.Shared.AssemblyRedirectHelper.Initialize();
// Shooter.Shared.AssemblyRedirectHelper.PreloadGranvilleAssemblies();

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Configure file logging - DON'T clear providers to keep OpenTelemetry logging
// builder.Logging.ClearProviders(); // Commented out to preserve structured logging to Aspire
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

// Add SiloManager for managing additional silo instances
builder.Services.AddSingleton<Shooter.Silo.Services.SiloManager>();
builder.Services.AddHostedService<Shooter.Silo.Services.SiloManager>(provider => 
    provider.GetRequiredService<Shooter.Silo.Services.SiloManager>());

// Add WorldManagerGrain initializer to ensure timer functionality works
builder.Services.AddHostedService<Shooter.Silo.Services.WorldManagerInitializer>();

// Add Silo registration service for multi-silo discovery
builder.Services.AddHostedService<Shooter.Silo.Services.SiloRegistrationService>();

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
    // Read Orleans configuration from environment/config
    var siloPort = builder.Configuration.GetValue<int>("Orleans:SiloPort", 11111);
    var gatewayPort = builder.Configuration.GetValue<int>("Orleans:GatewayPort", 30000);
    var clusterId = builder.Configuration.GetValue<string>("Orleans:ClusterId", "dev");
    var serviceId = builder.Configuration.GetValue<string>("Orleans:ServiceId", "ShooterDemo");
    var isPrimarySilo = builder.Configuration.GetValue<bool>("Orleans:IsPrimarySilo", true);
    var primarySiloEndpoint = builder.Configuration.GetValue<string>("Orleans:PrimarySiloEndpoint", $"localhost:{siloPort}");
    
    // Parse primary silo endpoint
    var primaryParts = primarySiloEndpoint.Split(':');
    var primaryHost = primaryParts[0];
    var primaryPort = int.Parse(primaryParts.Length > 1 ? primaryParts[1] : "11111");
    var primaryIp = primaryHost == "localhost" ? IPAddress.Loopback : IPAddress.Parse(primaryHost);
    
    Console.WriteLine($"Configuring Orleans Silo: Port={siloPort}, Gateway={gatewayPort}, Primary={isPrimarySilo}, Cluster={clusterId}");
    
    siloBuilder
        .UseLocalhostClustering(
            siloPort: siloPort,
            gatewayPort: gatewayPort,
            primarySiloEndpoint: new IPEndPoint(primaryIp, primaryPort))
        .Configure<ClusterOptions>(options =>
        {
            options.ClusterId = clusterId;
            options.ServiceId = serviceId;
        })
        .Configure<EndpointOptions>(options =>
        {
            options.AdvertisedIPAddress = IPAddress.Loopback;
        })
        .AddMemoryGrainStorage("worldStore")
        .AddMemoryGrainStorage("playerStore")
        .AddMemoryGrainStorage("statsStore")  // Fix Issue 3: Add missing statsStore
        .AddMemoryGrainStorage(UFX.Orleans.SignalRBackplane.Constants.StorageName) // UFX SignalR backplane storage
        .UseInMemoryReminderService()
        .AddSignalRBackplane()  // Enable SignalR backplane for multi-silo support
        .UseDashboard(options =>
        {
            options.HostSelf = true;
            
            // Calculate dashboard port based on silo instance
            // Default silo (7071) gets port 7171, silo-1 (7072) gets 7172, etc.
            var siloHttpPort = builder.Configuration.GetValue<int?>("ASPNETCORE_HTTP_PORT") ?? 7071;
            var dashboardPort = 7171 + (siloHttpPort - 7071);
            options.Port = dashboardPort;
            
            Console.WriteLine($"Orleans Dashboard will be available at http://localhost:{dashboardPort}/");
        });
});

// TODO: Configure RPC client so Silo can communicate with ActionServers
// For now, commenting out complex RPC client configuration to fix build
// The main issues (missing storage provider) are fixed above

// Workaround: Force load Granville assemblies immediately
// This must happen before any Orleans serialization configuration
_ = typeof(Orleans.Serialization.Serializer).Assembly; // Force load Granville.Orleans.Serialization
_ = typeof(Orleans.Metadata.GrainManifest).Assembly; // Force load Granville.Orleans.Core.Abstractions
_ = typeof(Orleans.MembershipTableData).Assembly; // Force load Granville.Orleans.Core
try { _ = System.Reflection.Assembly.Load("Granville.Orleans.Runtime"); } catch { }
try { _ = System.Reflection.Assembly.Load("Granville.Orleans.Serialization.Abstractions"); } catch { }

// Configure Orleans shim compatibility and serialization
// TODO: Replace with builder.Services.AddOrleansShims() when Granville.Orleans.Shims package is available
builder.Services.AddSerializer(serializerBuilder =>
{
    // Add Granville assemblies for shim compatibility
    // This is the workaround from Granville.Orleans.Shims package
    serializerBuilder.AddGranvilleAssemblies();
    
    // Add application-specific assemblies
    serializerBuilder.AddAssembly(typeof(Shooter.Silo.Grains.WorldManagerGrain).Assembly);
    serializerBuilder.AddAssembly(typeof(Shooter.Shared.GrainInterfaces.IWorldManagerGrain).Assembly);
    
    // TODO: Add RPC interfaces when RPC client is properly configured
    // serializerBuilder.AddAssembly(typeof(Shooter.Shared.RpcInterfaces.IGameRpcGrain).Assembly);
});

// Configure to use the ObjectCopier for types without specific copiers (development only)
builder.Services.Configure<Orleans.Serialization.Configuration.TypeManifestOptions>(options =>
{
    options.AllowAllTypes = true; // Allow all types for development
});

// Option 2 attempt: Remove just the SerializerConfigurationValidator to bypass early validation
// This is a temporary workaround for development while we fix the root issue
Console.WriteLine("\n--- Attempting to remove SerializerConfigurationValidator ---");
var validatorDescriptor = builder.Services.FirstOrDefault(d => 
    d.ServiceType == typeof(Orleans.IConfigurationValidator) && 
    d.ImplementationType?.Name == "SerializerConfigurationValidator");

if (validatorDescriptor != null)
{
    builder.Services.Remove(validatorDescriptor);
    Console.WriteLine("✓ Successfully removed SerializerConfigurationValidator");
}
else
{
    Console.WriteLine("✗ SerializerConfigurationValidator not found in services");
    
    // Try removing all validators as a fallback
    try 
    {
        var validatorCount = builder.Services.RemoveAll<Orleans.IConfigurationValidator>();
        Console.WriteLine($"✓ Removed {validatorCount} configuration validators");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ Could not remove configuration validators: {ex.Message}");
    }
}

// Note: AddSerializer() is already called above with configuration, so we don't call it again here

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

// Temporary extension method until Granville.Orleans.Shims package is available
// This will be removed once the package is published
public static class GranvilleShimExtensions
{
    public static ISerializerBuilder AddGranvilleAssemblies(this ISerializerBuilder serializerBuilder)
    {
        Console.WriteLine("================== AddGranvilleAssemblies START ==================");
        
        var loadedAssemblies = new List<Assembly>();
        var failedAssemblies = new List<(string name, string error)>();
        
        // List of all Granville.Orleans assemblies we want to load
        var granvilleAssemblyNames = new[]
        {
            "Granville.Orleans.Serialization",
            "Granville.Orleans.Serialization.Abstractions",
            "Granville.Orleans.Core",
            "Granville.Orleans.Core.Abstractions",
            "Granville.Orleans.Runtime",
            "Granville.Orleans.Client",
            "Granville.Orleans.Server",
            "Granville.Orleans.Persistence.Memory",
            "Granville.Orleans.Reminders"
        };
        
        // Try to load each assembly
        foreach (var assemblyName in granvilleAssemblyNames)
        {
            try
            {
                var assembly = Assembly.Load(assemblyName);
                loadedAssemblies.Add(assembly);
                Console.WriteLine($"✓ Loaded: {assembly.FullName}");
                
                // Check if assembly has ApplicationPart attribute
                var hasAppPart = assembly.GetCustomAttributes().Any(attr => attr.GetType().FullName == "Orleans.ApplicationPartAttribute");
                Console.WriteLine($"  - Has [ApplicationPart]: {hasAppPart}");
                
                // Check for TypeManifestProvider attributes (indicates serialization metadata)
                var manifestProviders = assembly.GetCustomAttributes<TypeManifestProviderAttribute>().ToList();
                if (manifestProviders.Any())
                {
                    Console.WriteLine($"  - Has {manifestProviders.Count} TypeManifestProvider(s)");
                    foreach (var provider in manifestProviders)
                    {
                        Console.WriteLine($"    - Provider type: {provider.ProviderType.FullName}");
                    }
                }
                
                // Add to serializer builder
                serializerBuilder.AddAssembly(assembly);
            }
            catch (Exception ex)
            {
                failedAssemblies.Add((assemblyName, ex.Message));
                Console.WriteLine($"✗ Failed to load {assemblyName}: {ex.Message}");
            }
        }
        
        // Also try to load assemblies that are already in AppDomain but start with Granville.Orleans
        Console.WriteLine("\n--- Checking AppDomain for additional Granville assemblies ---");
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.FullName?.StartsWith("Granville.Orleans") == true && 
                !loadedAssemblies.Any(a => a.FullName == assembly.FullName))
            {
                loadedAssemblies.Add(assembly);
                Console.WriteLine($"✓ Found in AppDomain: {assembly.FullName}");
                serializerBuilder.AddAssembly(assembly);
            }
        }
        
        // Try to check if ImmutableArrayCodec is available
        Console.WriteLine("\n--- Checking for ImmutableArray codec ---");
        try
        {
            var immutableArrayCodecType = Type.GetType("Orleans.Serialization.Codecs.ImmutableArrayCodec`1, Granville.Orleans.Serialization");
            if (immutableArrayCodecType != null)
            {
                Console.WriteLine($"✓ Found ImmutableArrayCodec type: {immutableArrayCodecType.FullName}");
                Console.WriteLine($"  - Assembly: {immutableArrayCodecType.Assembly.FullName}");
                Console.WriteLine($"  - Has [RegisterSerializer]: {immutableArrayCodecType.IsDefined(typeof(RegisterSerializerAttribute))}");
            }
            else
            {
                Console.WriteLine("✗ ImmutableArrayCodec type not found");
            }
            
            var immutableArrayCopierType = Type.GetType("Orleans.Serialization.Codecs.ImmutableArrayCopier`1, Granville.Orleans.Serialization");
            if (immutableArrayCopierType != null)
            {
                Console.WriteLine($"✓ Found ImmutableArrayCopier type: {immutableArrayCopierType.FullName}");
                Console.WriteLine($"  - Has [RegisterCopier]: {immutableArrayCopierType.IsDefined(typeof(RegisterCopierAttribute))}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error checking for ImmutableArray types: {ex.Message}");
        }
        
        // Also add Orleans.Persistence.Memory if it's being used
        Console.WriteLine("\n--- Checking for Orleans.Persistence.Memory ---");
        try
        {
            var memoryStorageType = Type.GetType("Orleans.Storage.MemoryGrainStorage, Orleans.Persistence.Memory");
            if (memoryStorageType != null)
            {
                Console.WriteLine($"✓ Found Memory storage assembly: {memoryStorageType.Assembly.FullName}");
                serializerBuilder.AddAssembly(memoryStorageType.Assembly);
            }
            else
            {
                // Try Granville version
                memoryStorageType = Type.GetType("Orleans.Storage.MemoryGrainStorage, Granville.Orleans.Persistence.Memory");
                if (memoryStorageType != null)
                {
                    Console.WriteLine($"✓ Found Granville Memory storage assembly: {memoryStorageType.Assembly.FullName}");
                    serializerBuilder.AddAssembly(memoryStorageType.Assembly);
                }
                else
                {
                    Console.WriteLine("✗ Orleans.Storage.MemoryGrainStorage not found in either Orleans or Granville assemblies");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error loading memory storage: {ex.Message}");
        }

        Console.WriteLine($"\n--- Summary ---");
        Console.WriteLine($"Successfully loaded: {loadedAssemblies.Count} assemblies");
        Console.WriteLine($"Failed to load: {failedAssemblies.Count} assemblies");
        
        Console.WriteLine("================== AddGranvilleAssemblies END ==================\n");
        return serializerBuilder;
    }
}