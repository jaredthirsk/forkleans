using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shooter.Bot;
using Shooter.Bot.Services;
using Shooter.Client.Common;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// Configure logging
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Shooter.Bot", LogLevel.Debug);
builder.Logging.AddFilter("Shooter.Client.Common", LogLevel.Debug);
builder.Logging.AddFilter("Forkleans.Rpc", LogLevel.Debug);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
builder.Logging.AddFilter("Polly", LogLevel.Warning);

// Add file logging with unique filename based on bot name or instance
string logFileName = "logs/bot.log";

// Try to get bot name from configuration first
var botName = builder.Configuration["BotName"];
if (!string.IsNullOrEmpty(botName))
{
    // Sanitize bot name for filename
    var safeBotName = botName.Replace(" ", "_").Replace(":", "").Replace("/", "").Replace("\\", "");
    logFileName = $"logs/bot-{safeBotName}.log";
}
else
{
    // Try to get instance ID from environment
    var aspireInstanceId = Environment.GetEnvironmentVariable("ASPIRE_INSTANCE_ID");
    if (!string.IsNullOrEmpty(aspireInstanceId))
    {
        logFileName = $"logs/bot-{aspireInstanceId}.log";
    }
    else
    {
        // Fallback: try to get the replica index from Aspire
        var replicaIndex = Environment.GetEnvironmentVariable("DOTNET_ASPIRE_REPLICA_INDEX");
        if (!string.IsNullOrEmpty(replicaIndex))
        {
            logFileName = $"logs/bot-{replicaIndex}.log";
        }
    }
}

builder.Logging.AddProvider(new FileLoggerProvider(logFileName));
Console.WriteLine($"Bot logging to: {logFileName}");

// Add services
builder.Services.AddHttpClient();

// Get configuration
var siloUrl = builder.Configuration["SiloUrl"] ?? "https://localhost:7071/";
if (!siloUrl.EndsWith("/"))
    siloUrl += "/";

// Register the RPC game client service
builder.Services.AddSingleton<ForkleansRpcGameClientService>(serviceProvider =>
{
    var logger = serviceProvider.GetRequiredService<ILogger<ForkleansRpcGameClientService>>();
    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient();
    httpClient.BaseAddress = new Uri(siloUrl);
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    return new ForkleansRpcGameClientService(logger, httpClient, configuration);
});

// Add bot service
builder.Services.AddSingleton<BotService>();
builder.Services.AddHostedService<BotService>(provider => provider.GetRequiredService<BotService>());

// Configure from command line args
builder.Configuration.AddCommandLine(args);

var app = builder.Build();

await app.RunAsync();