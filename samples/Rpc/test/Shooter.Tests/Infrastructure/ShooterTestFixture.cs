using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using Xunit;

namespace Shooter.Tests.Infrastructure;

/// <summary>
/// Test fixture that manages the Shooter application lifecycle for integration tests.
/// Starts the Aspire AppHost process with predetermined configuration for testing.
/// </summary>
public class ShooterTestFixture : IAsyncLifetime
{
    private Process? _appHostProcess;
    private readonly string _testRunId;
    private readonly string _logDirectory;
    private readonly List<Process> _processes = new();
    private MetricsSnapshot? _baselineMetrics;
    private ILogger<ShooterTestFixture>? _logger;
    
    public string LogDirectory => _logDirectory;
    public int BotCount { get; }
    public int ActionServerCount { get; }
    public int TestTimeoutSeconds { get; }
    
    /// <summary>
    /// Baseline metrics captured when services are started but before test execution.
    /// </summary>
    public MetricsSnapshot? BaselineMetrics => _baselineMetrics;
    
    public ShooterTestFixture()
    {
        _testRunId = Guid.NewGuid().ToString("N")[..8];
        
        // Find the samples/Rpc directory for consistent log location
        var currentDir = Directory.GetCurrentDirectory();
        var rpcDir = currentDir;
        while (!string.IsNullOrEmpty(rpcDir) && Path.GetFileName(rpcDir) != "Rpc")
        {
            rpcDir = Path.GetDirectoryName(rpcDir);
        }
        
        if (string.IsNullOrEmpty(rpcDir))
        {
            // Fallback to current directory
            rpcDir = currentDir;
        }
        
        // The services write logs to ../logs relative to their working directory
        // which ends up being in samples/Rpc/logs
        _logDirectory = Path.Combine(rpcDir, "logs");
        
        // Load test configuration
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.Test.json", optional: true)
            .AddEnvironmentVariables("SHOOTER_TEST_")
            .Build();
            
        BotCount = config.GetValue<int>("BotCount", 3);
        ActionServerCount = config.GetValue<int>("ActionServerCount", 2);
        TestTimeoutSeconds = config.GetValue<int>("TestTimeout", 120);
        
        // Initialize logger
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<ShooterTestFixture>();
    }
    
    public async Task InitializeAsync()
    {
        // Kill any existing Shooter processes to avoid conflicts
        try
        {
            var killScript = Path.Combine(Directory.GetCurrentDirectory(), "../../scripts/kill-shooter-processes.sh");
            if (File.Exists(killScript))
            {
                var killProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = killScript,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                if (killProcess != null)
                {
                    await killProcess.WaitForExitAsync();
                }
            }
        }
        catch { /* Ignore errors */ }
        
        // Ensure log directory exists
        Directory.CreateDirectory(_logDirectory);
        
        // Find the AppHost project path
        // When running tests, we're in bin/Debug/net9.0, so we need to go up to the samples/Rpc directory
        var currentDir = Directory.GetCurrentDirectory();
        var searchDir = currentDir;
        var appHostPath = string.Empty;
        
        // Search up the directory tree for the AppHost project
        while (!string.IsNullOrEmpty(searchDir))
        {
            var candidate = Path.Combine(searchDir, "Shooter.AppHost");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "Shooter.AppHost.csproj")))
            {
                appHostPath = candidate;
                break;
            }
            
            // Also check if we're already in the Rpc directory
            if (Path.GetFileName(searchDir) == "Rpc")
            {
                candidate = Path.Combine(searchDir, "Shooter.AppHost");
                if (Directory.Exists(candidate))
                {
                    appHostPath = candidate;
                    break;
                }
            }
            
            searchDir = Path.GetDirectoryName(searchDir);
        }
        
        if (string.IsNullOrEmpty(appHostPath) || !Directory.Exists(appHostPath))
        {
            throw new DirectoryNotFoundException($"AppHost directory not found. Started search from: {currentDir}");
        }
        
        // Start the AppHost process
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{appHostPath}\" -- --transport=litenetlib",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = appHostPath
        };
        
        // Set environment variables
        startInfo.Environment["TEST_MODE"] = "true";
        startInfo.Environment["TEST_RUN_ID"] = _testRunId;
        startInfo.Environment["InitialActionServerCount"] = ActionServerCount.ToString();
        startInfo.Environment["LOG_DIRECTORY"] = _logDirectory;
        startInfo.Environment["ASPIRE_ALLOW_UNSECURED_TRANSPORT"] = "true";
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "IntegrationTest";
        startInfo.Environment["ASPNETCORE_URLS"] = "http://localhost:7070;https://localhost:7071";  // Ensure Silo listens on expected ports
        
        // Use a random port for Aspire dashboard to avoid conflicts
        var random = new Random();
        var dashboardPort = 20000 + random.Next(1000, 9000);
        startInfo.Environment["DOTNET_DASHBOARD_OTLP_ENDPOINT_URL"] = $"http://localhost:{dashboardPort}";
        
        // Pass the absolute log directory path to each service
        startInfo.Environment["SHOOTER_LOG_DIR"] = _logDirectory;
        
        _appHostProcess = Process.Start(startInfo);
        
        if (_appHostProcess == null)
        {
            throw new InvalidOperationException("Failed to start AppHost process");
        }
        
        // Capture output for debugging
        _appHostProcess.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                Console.WriteLine($"[AppHost] {e.Data}");
        };
        _appHostProcess.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                Console.WriteLine($"[AppHost ERROR] {e.Data}");
        };
        _appHostProcess.BeginOutputReadLine();
        _appHostProcess.BeginErrorReadLine();
        
        // Give the AppHost time to start all services
        await Task.Delay(10000);  // Increased delay to allow all services to start
        
        // Wait for services to be ready
        await WaitForServicesReady();
        
        // Capture baseline metrics after services are ready
        await CaptureBaselineMetrics();
    }
    
    public async Task DisposeAsync()
    {
        // Stop all processes
        foreach (var process in _processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                    await process.WaitForExitAsync();
                }
                process.Dispose();
            }
            catch { }
        }
        
        if (_appHostProcess != null && !_appHostProcess.HasExited)
        {
            try
            {
                _appHostProcess.Kill(true);
                await _appHostProcess.WaitForExitAsync();
                _appHostProcess.Dispose();
            }
            catch { }
        }
        
        // Optionally clean up log directory after tests
        // For now, we'll keep logs for debugging
    }
    
    private async Task WaitForServicesReady()
    {
        var maxWaitTime = TimeSpan.FromSeconds(120);  // Increased timeout
        var startTime = DateTime.UtcNow;
        
        // Create HttpClient that ignores SSL certificate errors for test environment
        var httpClientHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
        };
        var httpClient = new HttpClient(httpClientHandler) { Timeout = TimeSpan.FromSeconds(5) };
        
        // Track service readiness
        var siloReady = false;
        var actionServersReady = new bool[ActionServerCount];
        var botsStarted = new bool[BotCount];
        
        // Base ports for services (these should match what's configured in AppHost)
        var siloPort = 7071;  // Default HTTPS port for Silo
        
        while (DateTime.UtcNow - startTime < maxWaitTime)
        {
            // Check Silo health
            if (!siloReady)
            {
                try
                {
                    var siloHealth = await CheckHealthEndpoint(httpClient, $"https://localhost:{siloPort}/health/ready");
                    if (siloHealth.IsHealthy)
                    {
                        siloReady = true;
                        Console.WriteLine("✓ Silo is ready");
                    }
                    else
                    {
                        Console.WriteLine($"  Silo not ready: {siloHealth.Status}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Silo health check failed: {ex.Message}");
                }
            }
            
            // Check ActionServer health
            for (int i = 0; i < ActionServerCount; i++)
            {
                if (!actionServersReady[i])
                {
                    try
                    {
                        // ActionServers use dynamic ports, but we can check for their log files
                        // and try common ports or read from logs
                        var logFile = Path.Combine(_logDirectory, $"actionserver-{i}.log");
                        if (File.Exists(logFile))
                        {
                            // Try to extract port from log file
                            var port = await ExtractActionServerPort(logFile);
                            if (port > 0)
                            {
                                var health = await CheckHealthEndpoint(httpClient, $"http://localhost:{port}/health/ready");
                                if (health.IsHealthy)
                                {
                                    actionServersReady[i] = true;
                                    Console.WriteLine($"✓ ActionServer {i} is ready on port {port}");
                                }
                                else
                                {
                                    Console.WriteLine($"  ActionServer {i} not ready: {health.Status}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  ActionServer {i} health check failed: {ex.Message}");
                    }
                }
            }
            
            // Check if bot logs exist (bots don't have health endpoints)
            for (int i = 0; i < BotCount; i++)
            {
                if (!botsStarted[i])
                {
                    var logFile = Path.Combine(_logDirectory, $"bot-{i}.log");
                    if (File.Exists(logFile))
                    {
                        // Check if bot has connected by looking for connection message in log
                        var connected = await CheckBotConnected(logFile);
                        if (connected)
                        {
                            botsStarted[i] = true;
                            Console.WriteLine($"✓ Bot {i} is connected");
                        }
                    }
                }
            }
            
            // Check if all services are ready
            if (siloReady && 
                actionServersReady.All(ready => ready) && 
                botsStarted.All(started => started))
            {
                Console.WriteLine("All services are ready!");
                
                // Give services a bit more time to stabilize
                await Task.Delay(2000);
                return;
            }
            
            // Show progress
            var readyCount = (siloReady ? 1 : 0) + 
                           actionServersReady.Count(r => r) + 
                           botsStarted.Count(s => s);
            var totalCount = 1 + ActionServerCount + BotCount;
            Console.WriteLine($"Waiting for services... {readyCount}/{totalCount} ready");
            
            await Task.Delay(1000);
        }
        
        // Timeout - provide detailed diagnostics
        var diagnostics = new List<string>();
        if (!siloReady) diagnostics.Add("Silo not ready");
        for (int i = 0; i < ActionServerCount; i++)
        {
            if (!actionServersReady[i]) diagnostics.Add($"ActionServer {i} not ready");
        }
        for (int i = 0; i < BotCount; i++)
        {
            if (!botsStarted[i]) diagnostics.Add($"Bot {i} not started");
        }
        
        // List what log files we did find
        var foundLogs = Directory.GetFiles(_logDirectory, "*.log");
        Console.WriteLine($"Found log files: {string.Join(", ", foundLogs.Select(Path.GetFileName))}");
        
        throw new TimeoutException($"Services did not start within {maxWaitTime.TotalSeconds} seconds. Issues: {string.Join(", ", diagnostics)}");
    }
    
    private async Task<HealthCheckResult> CheckHealthEndpoint(HttpClient httpClient, string url)
    {
        try
        {
            var response = await httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                
                // Try to parse health check response
                try
                {
                    using var doc = JsonDocument.Parse(content);
                    var status = doc.RootElement.GetProperty("status").GetString() ?? "Unknown";
                    return new HealthCheckResult { IsHealthy = status == "Healthy", Status = status };
                }
                catch
                {
                    // If not JSON, just check status code
                    return new HealthCheckResult { IsHealthy = true, Status = "OK" };
                }
            }
            return new HealthCheckResult { IsHealthy = false, Status = $"HTTP {response.StatusCode}" };
        }
        catch (Exception ex)
        {
            return new HealthCheckResult { IsHealthy = false, Status = ex.Message };
        }
    }
    
    private async Task<int> ExtractActionServerPort(string logFile)
    {
        try
        {
            // Read last few lines of log file looking for port information
            var lines = await ReadLastLines(logFile, 50);
            foreach (var line in lines)
            {
                // Look for patterns like "Now listening on: http://[::]:5234"
                if (line.Contains("Now listening on:"))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(line, @":(\d+)");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out var port))
                    {
                        return port;
                    }
                }
            }
        }
        catch { }
        return 0;
    }
    
    private async Task<bool> CheckBotConnected(string logFile)
    {
        try
        {
            // Read last few lines looking for connection confirmation
            var lines = await ReadLastLines(logFile, 100);
            return lines.Any(line => 
                line.Contains("connected as player") || 
                line.Contains("Bot connected") ||
                line.Contains("Successfully connected"));
        }
        catch { }
        return false;
    }
    
    private async Task<string[]> ReadLastLines(string filePath, int lineCount)
    {
        const int bufferSize = 4096;
        var lines = new List<string>();
        
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            if (fs.Length == 0) return Array.Empty<string>();
            
            var buffer = new byte[bufferSize];
            var leftover = "";
            var position = Math.Max(0, fs.Length - bufferSize);
            
            while (position >= 0 && lines.Count < lineCount)
            {
                fs.Seek(position, SeekOrigin.Begin);
                var bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length);
                var text = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead) + leftover;
                var textLines = text.Split('\n');
                
                for (int i = textLines.Length - 1; i >= 0; i--)
                {
                    if (!string.IsNullOrWhiteSpace(textLines[i]))
                    {
                        lines.Insert(0, textLines[i].Trim());
                        if (lines.Count >= lineCount) break;
                    }
                }
                
                leftover = textLines[0];
                position -= bufferSize;
            }
        }
        
        return lines.ToArray();
    }
    
    private async Task CaptureBaselineMetrics()
    {
        try
        {
            _logger?.LogInformation("Capturing baseline metrics after service startup");
            
            // Give services a moment to stabilize their metrics
            await Task.Delay(1000);
            
            // Capture the current metrics state as baseline
            _baselineMetrics = MetricsSnapshot.Current;
            
            _logger?.LogInformation("Baseline metrics captured - ActivePlayers: {ActivePlayers}, ActiveBots: {ActiveBots}", 
                _baselineMetrics.ActivePlayers, _baselineMetrics.ActiveBots);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to capture baseline metrics, tests may still proceed");
            // Don't fail the fixture initialization if metrics capture fails
        }
    }
    
    private class HealthCheckResult
    {
        public bool IsHealthy { get; set; }
        public string Status { get; set; } = "";
    }
    
    public async Task<string[]> GetLogFiles(string pattern = "*")
    {
        return await Task.FromResult(Directory.GetFiles(_logDirectory, pattern));
    }
}