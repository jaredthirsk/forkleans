using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace Shooter.Bot.Services;

/// <summary>
/// Service to delay bot startup to ensure Orleans silo and action servers are ready.
/// </summary>
public class OrleansStartupDelayService : IHostedService
{
    private readonly ILogger<OrleansStartupDelayService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;

    public OrleansStartupDelayService(
        ILogger<OrleansStartupDelayService> logger, 
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Get the Silo URL from configuration
        var siloUrl = _configuration["SiloUrl"];
        if (!string.IsNullOrEmpty(siloUrl))
        {
            _logger.LogInformation("Waiting for Orleans Silo and Action Servers to be ready...");
            
            // Wait for Silo to be ready first
            var healthEndpoint = $"{siloUrl}/ready";
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(2);
            
            var maxAttempts = 30; // 30 attempts * 2 seconds = 1 minute max
            var attempt = 0;
            
            while (attempt < maxAttempts)
            {
                try
                {
                    var response = await httpClient.GetAsync(healthEndpoint, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("Orleans Silo is ready (health check passed)");
                        break;
                    }
                    
                    if (attempt % 10 == 0) // Only log every 10th attempt to reduce noise
                    {
                        _logger.LogDebug("Silo health check returned {StatusCode}, retrying... (attempt {Attempt}/{Max})", 
                            response.StatusCode, attempt, maxAttempts);
                    }
                }
                catch (Exception ex)
                {
                    if (attempt % 10 == 0) // Only log every 10th attempt to reduce noise
                    {
                        _logger.LogDebug(ex, "Failed to connect to Silo health endpoint, retrying... (attempt {Attempt}/{Max})", 
                            attempt, maxAttempts);
                    }
                }
                
                attempt++;
                if (attempt < maxAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
            }
            
            if (attempt >= maxAttempts)
            {
                _logger.LogWarning("Timed out waiting for Orleans Silo to be ready after {Attempts} attempts", maxAttempts);
            }
            
            // Additional delay to ensure action servers are registered
            _logger.LogInformation("Waiting additional 10 seconds for Action Servers to register...");
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            
            _logger.LogInformation("Startup delay complete, bot can now connect");
        }
        else
        {
            // No Silo URL configured, use a fixed delay
            _logger.LogInformation("No Silo URL configured, using fixed 15 second delay...");
            await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}