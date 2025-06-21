using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Shooter.Bot.Services;

public class BotOrchestrator : BackgroundService
{
    private readonly ILogger<BotOrchestrator> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly List<TestBot> _bots = new();
    private readonly CancellationTokenSource _shutdownCts = new();

    public BotOrchestrator(
        ILogger<BotOrchestrator> logger,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Bot Orchestrator starting...");

        // Get configuration
        var botCount = _configuration.GetValue<int>("BotCount", 1);
        var siloUrl = _configuration.GetValue<string>("SiloUrl") ?? "http://localhost:7071";
        var testMode = _configuration.GetValue<bool>("TestMode", true);

        _logger.LogInformation("Starting {BotCount} bots in {Mode} mode", botCount, testMode ? "test" : "normal");

        // Wait for services to be ready
        _logger.LogInformation("Waiting 10 seconds for services to be ready...");
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        // Create and start bots
        for (int i = 0; i < botCount; i++)
        {
            var botName = testMode ? $"TestBot_{i}" : $"Bot_{i}";
            var bot = new TestBot(
                _logger,
                _httpClientFactory,
                botName,
                siloUrl,
                testMode);

            _bots.Add(bot);
            _ = Task.Run(() => bot.RunAsync(stoppingToken));

            // Stagger bot starts
            await Task.Delay(2000, stoppingToken);
        }

        // Keep running until cancelled
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Bot Orchestrator shutting down...");
        }

        // Stop all bots
        foreach (var bot in _bots)
        {
            await bot.StopAsync();
        }
    }
}