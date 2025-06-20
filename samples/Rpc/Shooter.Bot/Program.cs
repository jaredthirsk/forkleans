using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shooter.Bot.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configure logging
builder.Services.AddLogging(config =>
{
    config.AddConsole();
    config.SetMinimumLevel(LogLevel.Information);
});

// Add services
builder.Services.AddHttpClient();
builder.Services.AddSingleton<BotOrchestrator>();
builder.Services.AddHostedService<BotOrchestrator>(provider => provider.GetRequiredService<BotOrchestrator>());

// Configure from command line args
builder.Configuration.AddCommandLine(args);

var app = builder.Build();

await app.RunAsync();