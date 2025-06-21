using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Shooter.Silo.Services;

public class ActionServerManager : IHostedService
{
    private readonly ILogger<ActionServerManager> _logger;
    private readonly IConfiguration _configuration;
    private readonly Dictionary<string, Process> _actionServerProcesses = new();
    private readonly object _processLock = new();
    private int _nextInstanceId;

    public ActionServerManager(ILogger<ActionServerManager> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        
        // Get the initial replica count from configuration (passed from AppHost)
        _nextInstanceId = _configuration.GetValue<int>("InitialActionServerCount", 9);
        _logger.LogInformation("ActionServerManager initialized with next instance ID: {NextInstanceId}", _nextInstanceId);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ActionServerManager started");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ActionServerManager stopping, terminating all managed ActionServers");
        
        lock (_processLock)
        {
            foreach (var process in _actionServerProcesses.Values)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit(5000);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error terminating ActionServer process");
                }
            }
            _actionServerProcesses.Clear();
        }
        
        return Task.CompletedTask;
    }

    public async Task<string> StartNewActionServerAsync()
    {
        var instanceId = _nextInstanceId++;
        var serverId = $"shooter-actionserver-{instanceId}";
        var rpcPort = 12000 + instanceId;
        
        _logger.LogInformation("Starting new ActionServer {ServerId} with RPC port {RpcPort}", serverId, rpcPort);
        
        try
        {
            // Determine the path to the ActionServer project
            var currentDirectory = Directory.GetCurrentDirectory();
            var actionServerPath = Path.GetFullPath(Path.Combine(currentDirectory, "..", "Shooter.ActionServer"));
            
            if (!Directory.Exists(actionServerPath))
            {
                _logger.LogError("ActionServer directory not found at {Path}", actionServerPath);
                throw new DirectoryNotFoundException($"ActionServer directory not found at {actionServerPath}");
            }
            
            // Build the command line arguments
            var arguments = new List<string>
            {
                "run",
                "--no-build", // Assume it's already built
                "--",
                $"--urls=http://localhost:{7072 + instanceId}",
                $"--environment=Production"
            };
            
            // Set environment variables
            var environmentVariables = new Dictionary<string, string>
            {
                ["Orleans__SiloUrl"] = _configuration["Urls"] ?? "https://localhost:7071",
                ["Orleans__GatewayEndpoint"] = "tcp://localhost:30000",
                ["RPC_PORT"] = rpcPort.ToString(),
                ["ASPIRE_INSTANCE_ID"] = instanceId.ToString(),
                ["DOTNET_ENVIRONMENT"] = "Production"
            };
            
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = string.Join(" ", arguments),
                WorkingDirectory = actionServerPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            
            foreach (var env in environmentVariables)
            {
                startInfo.Environment[env.Key] = env.Value;
            }
            
            var process = new Process { StartInfo = startInfo };
            
            // Set up output redirection
            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    _logger.LogInformation("[{ServerId}] {Output}", serverId, e.Data);
            };
            
            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    _logger.LogError("[{ServerId}] {Error}", serverId, e.Data);
            };
            
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            
            lock (_processLock)
            {
                _actionServerProcesses[serverId] = process;
            }
            
            // Give it a moment to start
            await Task.Delay(3000);
            
            if (process.HasExited)
            {
                _logger.LogError("ActionServer {ServerId} exited immediately with code {ExitCode}", serverId, process.ExitCode);
                throw new InvalidOperationException($"ActionServer {serverId} failed to start");
            }
            
            _logger.LogInformation("Successfully started ActionServer {ServerId}", serverId);
            return serverId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start new ActionServer");
            throw;
        }
    }

    public async Task<bool> StopActionServerAsync(string serverId)
    {
        _logger.LogInformation("Stopping ActionServer {ServerId}", serverId);
        
        Process? process;
        lock (_processLock)
        {
            if (!_actionServerProcesses.TryGetValue(serverId, out process))
            {
                _logger.LogWarning("ActionServer {ServerId} not found in managed processes", serverId);
                return false;
            }
            
            _actionServerProcesses.Remove(serverId);
        }
        
        try
        {
            if (!process.HasExited)
            {
                // Try graceful shutdown first
                process.Kill(entireProcessTree: true);
                await Task.Run(() => process.WaitForExit(5000));
                
                if (!process.HasExited)
                {
                    _logger.LogWarning("ActionServer {ServerId} did not exit gracefully, forcing termination", serverId);
                    process.Kill();
                }
            }
            
            _logger.LogInformation("Successfully stopped ActionServer {ServerId}", serverId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping ActionServer {ServerId}", serverId);
            return false;
        }
    }

    public List<string> GetManagedServerIds()
    {
        lock (_processLock)
        {
            return _actionServerProcesses.Keys.ToList();
        }
    }
}