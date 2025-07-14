using Orleans;
using Shooter.Shared.GrainInterfaces;

namespace Shooter.Silo.Services;

/// <summary>
/// Background service that registers this silo with the SiloRegistryGrain and sends periodic heartbeats.
/// </summary>
public class SiloRegistrationService : BackgroundService
{
    private readonly ILogger<SiloRegistrationService> _logger;
    private readonly IClusterClient _clusterClient;
    private readonly IConfiguration _configuration;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private string? _siloId;
    private ISiloRegistryGrain? _registryGrain;

    public SiloRegistrationService(
        ILogger<SiloRegistrationService> logger,
        IClusterClient clusterClient,
        IConfiguration configuration,
        IHostApplicationLifetime applicationLifetime)
    {
        _logger = logger;
        _clusterClient = clusterClient;
        _configuration = configuration;
        _applicationLifetime = applicationLifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for Orleans to be ready
        await Task.Delay(5000, stoppingToken);
        
        try
        {
            // Generate a unique silo ID
            _siloId = GenerateSiloId();
            
            // Get the registry grain
            _registryGrain = _clusterClient.GetGrain<ISiloRegistryGrain>(0);
            
            // Register this silo
            var siloInfo = CreateSiloInfo();
            await _registryGrain.RegisterSilo(siloInfo);
            
            _logger.LogInformation("Registered silo {SiloId} with registry", _siloId);
            
            // Register shutdown handler
            _applicationLifetime.ApplicationStopping.Register(async () =>
            {
                try
                {
                    if (_registryGrain != null && !string.IsNullOrEmpty(_siloId))
                    {
                        await _registryGrain.UnregisterSilo(_siloId);
                        _logger.LogInformation("Unregistered silo {SiloId} from registry", _siloId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to unregister silo on shutdown");
                }
            });
            
            // Send periodic heartbeats
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(30000, stoppingToken); // Every 30 seconds
                    
                    if (_registryGrain != null && !string.IsNullOrEmpty(_siloId))
                    {
                        await _registryGrain.UpdateHeartbeat(_siloId);
                        _logger.LogDebug("Sent heartbeat for silo {SiloId}", _siloId);
                    }
                }
                catch (TaskCanceledException)
                {
                    // Expected when shutting down
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send heartbeat");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register silo with registry");
        }
    }

    private string GenerateSiloId()
    {
        // Use a combination of machine name and timestamp for uniqueness
        var siloPort = _configuration.GetValue<int>("Orleans:SiloPort", 11111);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        return $"{Environment.MachineName}-{siloPort}-{timestamp}";
    }

    private SiloInfo CreateSiloInfo()
    {
        // Get HTTP/HTTPS ports from configuration
        var httpPort = _configuration.GetValue<int>("Aspire:Shooter:Silo:http:0:Port", 
            _configuration.GetValue<int>("Aspire:Shooter:Silo:0:Port", 7071));
        var httpsPort = _configuration.GetValue<int>("Aspire:Shooter:Silo:https:0:Port", 
            _configuration.GetValue<int>("Aspire:Shooter:Silo:1:Port", 7171));
        
        // Check if we're running in development or with specific URLs
        var urls = _configuration["ASPNETCORE_URLS"] ?? $"http://localhost:{httpPort};https://localhost:{httpsPort}";
        
        // Parse the URLs to get the actual endpoints
        var urlList = urls.Split(';');
        var httpUrl = urlList.FirstOrDefault(u => u.StartsWith("http://")) ?? $"http://localhost:{httpPort}";
        var httpsUrl = urlList.FirstOrDefault(u => u.StartsWith("https://")) ?? $"https://localhost:{httpsPort}";
        
        // Extract host from URLs (in case it's not localhost)
        var httpUri = new Uri(httpUrl);
        var httpsUri = new Uri(httpsUrl);
        
        var ipAddress = httpUri.Host == "localhost" ? "127.0.0.1" : httpUri.Host;
        
        return new SiloInfo
        {
            SiloId = _siloId!,
            HttpEndpoint = httpUrl,
            HttpsEndpoint = httpsUrl,
            SignalRUrl = $"{httpsUrl}/gamehub",
            IpAddress = ipAddress,
            HttpPort = httpUri.Port,
            HttpsPort = httpsUri.Port,
            IsPrimary = _configuration.GetValue<bool>("Orleans:IsPrimarySilo", true),
            LastHeartbeat = DateTime.UtcNow
        };
    }
}