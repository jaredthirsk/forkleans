using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shooter.Bot;
using Shooter.Bot.Services;
using Shooter.Client.Common;
using Shooter.ServiceDefaults;
using System.Runtime.Loader;
using System.Reflection;

// Assembly redirect for Granville.Orleans.* to Orleans.*
// This allows Granville.Rpc (built against Granville.Orleans) to work with Microsoft.Orleans
AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
{
    if (assemblyName.Name?.StartsWith("Granville.Orleans") == true)
    {
        var orleansName = assemblyName.Name.Replace("Granville.Orleans", "Orleans");
        try
        {
            // Try to load Orleans assembly
            return context.LoadFromAssemblyName(new AssemblyName(orleansName));
        }
        catch { }
    }
    return null;
};

// Command line options:
// --test or -t: Enable test mode (default: true)
// --transport: Specify transport type (litenetlib or ruffles, default: litenetlib)
// --SiloUrl: Specify the Orleans silo URL (default: https://localhost:7071/)
// --BotName: Override the auto-generated bot name

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// Configure logging from appsettings.json
// Don't override with hardcoded values - let appsettings.json control the log levels
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
builder.Logging.AddFilter("Polly", LogLevel.Warning);

// Add file logging with unique filename based on bot name or instance
string logFileName = "../logs/bot.log";

// Try to get bot name from configuration first
var botName = builder.Configuration["BotName"];
if (!string.IsNullOrEmpty(botName))
{
    // Sanitize bot name for filename
    var safeBotName = botName.Replace(" ", "_").Replace(":", "").Replace("/", "").Replace("\\", "");
    logFileName = $"../logs/bot-{safeBotName}.log";
}
else
{
    // Try to get instance ID from environment
    var aspireInstanceId = Environment.GetEnvironmentVariable("ASPIRE_INSTANCE_ID");
    if (!string.IsNullOrEmpty(aspireInstanceId))
    {
        logFileName = $"../logs/bot-{aspireInstanceId}.log";
    }
    else
    {
        // Fallback: try to get the replica index from Aspire
        var replicaIndex = Environment.GetEnvironmentVariable("DOTNET_ASPIRE_REPLICA_INDEX");
        if (!string.IsNullOrEmpty(replicaIndex))
        {
            logFileName = $"../logs/bot-{replicaIndex}.log";
        }
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

Console.WriteLine($"Bot logging to: {logFileName}");
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
builder.Services.AddHttpClient();

// Add health checks
builder.Services.AddHealthChecks();

// Add metrics health check
builder.Services.AddMetricsHealthCheck("Bot");

// Get configuration
var siloUrl = builder.Configuration["SiloUrl"] ?? "https://localhost:7071/";
if (!siloUrl.EndsWith("/"))
    siloUrl += "/";

// Register the RPC game client service
builder.Services.AddSingleton<GranvilleRpcGameClientService>(serviceProvider =>
{
    var logger = serviceProvider.GetRequiredService<ILogger<GranvilleRpcGameClientService>>();
    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient();
    httpClient.BaseAddress = new Uri(siloUrl);
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    return new GranvilleRpcGameClientService(logger, httpClient, configuration);
});

// Add bot service
builder.Services.AddSingleton<BotService>();
builder.Services.AddHostedService<BotService>(provider => provider.GetRequiredService<BotService>());

// Configure from command line args with custom switch mappings
var switchMappings = new Dictionary<string, string>()
{
    { "--test", "TestMode" },
    { "-t", "TestMode" },
    { "--transport", "RpcTransport" }
};
builder.Configuration.AddCommandLine(args, switchMappings);

var app = builder.Build();

await app.RunAsync();

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