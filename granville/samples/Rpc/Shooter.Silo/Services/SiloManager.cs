using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Shooter.Silo.Services;

public class SiloManager : BackgroundService
{
    private readonly ILogger<SiloManager> _logger;
    private readonly IConfiguration _configuration;
    private readonly Dictionary<string, Process> _managedSilos = new();
    private readonly object _lock = new();
    private int _nextSiloPort = 11112; // Start from port 11112 for additional silos
    private int _nextHttpPort = 7075; // Start from port 7075 for additional silos

    public SiloManager(ILogger<SiloManager> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Monitor managed silos
        return Task.Run(async () =>
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                lock (_lock)
                {
                    var deadSilos = _managedSilos
                        .Where(kvp => kvp.Value.HasExited)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var siloId in deadSilos)
                    {
                        _logger.LogWarning("Managed Silo {SiloId} has exited", siloId);
                        _managedSilos.Remove(siloId);
                    }
                }

                await Task.Delay(5000, stoppingToken);
            }
        }, stoppingToken);
    }

    public Task<string> StartNewSiloAsync()
    {
        var siloId = $"silo-{Guid.NewGuid():N}";
        
        lock (_lock)
        {
            try
            {
                var siloPort = _nextSiloPort++;
                var gatewayPort = 30000 + (siloPort - 11111);
                var httpPort = _nextHttpPort++;
                var httpsPort = httpPort + 20000; // HTTPS port offset

                _logger.LogInformation("Starting new Silo {SiloId} with ports: Orleans={SiloPort}, Gateway={GatewayPort}, HTTP={HttpPort}, HTTPS={HttpsPort}", 
                    siloId, siloPort, gatewayPort, httpPort, httpsPort);

                // Build the silo executable path
                var currentDirectory = Directory.GetCurrentDirectory();
                var siloPath = Path.Combine(currentDirectory, "..", "Shooter.Silo");
                var siloExe = Path.Combine(siloPath, "bin", "Debug", "net9.0", "Shooter.Silo");
                
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    siloExe += ".exe";
                }

                if (!File.Exists(siloExe))
                {
                    // Try release build
                    siloExe = Path.Combine(siloPath, "bin", "Release", "net9.0", "Shooter.Silo");
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        siloExe += ".exe";
                    }
                }

                if (!File.Exists(siloExe))
                {
                    throw new FileNotFoundException($"Silo executable not found at {siloExe}");
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = siloExe,
                    WorkingDirectory = siloPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                // Set environment variables for the new silo
                startInfo.Environment["ASPNETCORE_URLS"] = $"http://localhost:{httpPort};https://localhost:{httpsPort}";
                startInfo.Environment["Orleans__Endpoints__SiloPort"] = siloPort.ToString();
                startInfo.Environment["Orleans__Endpoints__GatewayPort"] = gatewayPort.ToString();
                startInfo.Environment["ASPIRE_INSTANCE_ID"] = siloId;
                startInfo.Environment["SILO_INSTANCE_ID"] = siloId;

                // Copy other relevant environment variables
                var envVars = new[] { "ASPNETCORE_ENVIRONMENT", "Orleans__ClusterId", "Orleans__ServiceId" };
                foreach (var envVar in envVars)
                {
                    var value = Environment.GetEnvironmentVariable(envVar);
                    if (!string.IsNullOrEmpty(value))
                    {
                        startInfo.Environment[envVar] = value;
                    }
                }

                var process = new Process { StartInfo = startInfo };

                // Capture output for debugging
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        _logger.LogDebug("[{SiloId}] {Output}", siloId, e.Data);
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        _logger.LogError("[{SiloId}] {Error}", siloId, e.Data);
                    }
                };

                if (!process.Start())
                {
                    throw new InvalidOperationException("Failed to start silo process");
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                _managedSilos[siloId] = process;
                
                _logger.LogInformation("Started Silo {SiloId} with PID {Pid}", siloId, process.Id);
                
                return Task.FromResult(siloId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start new Silo");
                throw;
            }
        }
    }

    public Task<bool> StopSiloAsync(string siloId)
    {
        lock (_lock)
        {
            if (!_managedSilos.TryGetValue(siloId, out var process))
            {
                _logger.LogWarning("Silo {SiloId} is not managed by this instance", siloId);
                return Task.FromResult(false);
            }

            try
            {
                if (!process.HasExited)
                {
                    _logger.LogInformation("Stopping Silo {SiloId} (PID: {Pid})", siloId, process.Id);
                    
                    // Try graceful shutdown first
                    process.CloseMainWindow();
                    
                    // Wait for up to 10 seconds for graceful shutdown
                    if (!process.WaitForExit(10000))
                    {
                        _logger.LogWarning("Silo {SiloId} did not exit gracefully, forcing termination", siloId);
                        process.Kill();
                        process.WaitForExit(5000);
                    }
                }

                _managedSilos.Remove(siloId);
                _logger.LogInformation("Successfully stopped Silo {SiloId}", siloId);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping Silo {SiloId}", siloId);
                return Task.FromResult(false);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("SiloManager stopping, cleaning up managed silos...");
        
        List<Process> processes;
        lock (_lock)
        {
            processes = _managedSilos.Values.ToList();
        }

        // Stop all managed silos
        foreach (var process in processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    await process.WaitForExitAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping managed silo process");
            }
        }

        await base.StopAsync(cancellationToken);
    }
}