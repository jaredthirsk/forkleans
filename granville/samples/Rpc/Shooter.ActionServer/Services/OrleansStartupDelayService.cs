using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Http;

namespace Shooter.ActionServer.Services;

/// <summary>
/// Service to delay Orleans client startup to ensure silo is ready
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
        // Only delay if running in Aspire (gateway endpoint is configured)
        var gatewayEndpoint = _configuration["Orleans:GatewayEndpoint"];
        if (!string.IsNullOrEmpty(gatewayEndpoint))
        {
            _logger.LogInformation("Waiting for Orleans Silo to be ready...");
            
            // Get the Silo URL for health checks
            var siloUrl = _configuration["Orleans:SiloUrl"];
            if (!string.IsNullOrEmpty(siloUrl))
            {
                var healthEndpoint = $"{siloUrl}/health/ready";
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
                            // Add small additional delay to ensure gateway is fully ready
                            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                            return;
                        }
                        
                        _logger.LogDebug("Silo health check returned {StatusCode}, retrying...", response.StatusCode);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to connect to Silo health endpoint, retrying...");
                    }
                    
                    attempt++;
                    if (attempt < maxAttempts)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    }
                }
                
                _logger.LogWarning("Timed out waiting for Orleans Silo to be ready after {Attempts} attempts", maxAttempts);
            }
            else
            {
                // Fallback to fixed delay if no Silo URL is configured
                _logger.LogInformation("No Silo URL configured, using fixed delay...");
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }
            
            _logger.LogInformation("Orleans startup delay complete");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}