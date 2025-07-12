using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Shooter.ActionServer.Services;

/// <summary>
/// Service to cleanup console redirection on shutdown
/// </summary>
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