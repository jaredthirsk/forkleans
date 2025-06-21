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

// Add file logging
builder.Logging.AddProvider(new FileLoggerProvider("logs/bot.log"));

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