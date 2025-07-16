using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Granville.Benchmarks.Runner.Services
{
    /// <summary>
    /// Enhanced network emulator that supports packet loss, latency, jitter, and bandwidth limitations.
    /// Uses platform-specific tools: tc (Linux), clumsy (Windows), or application-level simulation.
    /// </summary>
    public class NetworkEmulator
    {
        private readonly ILogger<NetworkEmulator> _logger;
        private NetworkCondition? _currentCondition;
        private readonly bool _useSystemTools;
        private readonly string _networkInterface;
        
        public NetworkEmulator(ILogger<NetworkEmulator> logger, bool useSystemTools = false, string networkInterface = "lo")
        {
            _logger = logger;
            _useSystemTools = useSystemTools;
            _networkInterface = networkInterface;
        }
        
        public async Task ApplyConditionsAsync(NetworkCondition condition)
        {
            _logger.LogInformation("Applying network conditions: {Name} (latency: {Latency}ms ±{Jitter}ms, loss: {Loss}%, bandwidth: {Bandwidth})", 
                condition.Name, condition.LatencyMs, condition.JitterMs, condition.PacketLoss * 100, 
                condition.Bandwidth > 0 ? $"{condition.Bandwidth} bps" : "unlimited");
            
            _currentCondition = condition;
            
            if (_useSystemTools)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    await ApplyLinuxConditionsAsync(condition);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    await ApplyWindowsConditionsAsync(condition);
                }
                else
                {
                    _logger.LogWarning("System-level network emulation not supported on this platform, falling back to application-level");
                }
            }
            else
            {
                _logger.LogInformation("Using application-level network emulation");
            }
        }
        
        public async Task ClearConditionsAsync()
        {
            _logger.LogInformation("Clearing network conditions");
            
            if (_currentCondition == null)
                return;
                
            if (_useSystemTools)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    await ClearLinuxConditionsAsync();
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    await ClearWindowsConditionsAsync();
                }
            }
            
            _currentCondition = null;
        }
        
        /// <summary>
        /// Gets the current network condition for application-level simulation.
        /// </summary>
        public NetworkCondition? GetCurrentCondition() => _currentCondition;
        
        /// <summary>
        /// Simulates packet loss at application level.
        /// Returns true if the packet should be dropped.
        /// </summary>
        public bool ShouldDropPacket()
        {
            if (_currentCondition?.PacketLoss > 0)
            {
                return Random.Shared.NextDouble() < _currentCondition.PacketLoss;
            }
            return false;
        }
        
        /// <summary>
        /// Calculates simulated latency including jitter.
        /// </summary>
        public int GetSimulatedLatencyMs()
        {
            if (_currentCondition == null)
                return 0;
                
            var baseLatency = _currentCondition.LatencyMs;
            if (_currentCondition.JitterMs > 0)
            {
                // Apply jitter as ± variation
                var jitter = (Random.Shared.NextDouble() * 2 - 1) * _currentCondition.JitterMs;
                baseLatency += (int)jitter;
            }
            
            return Math.Max(0, baseLatency);
        }
        
        /// <summary>
        /// Checks if sending data would exceed bandwidth limit.
        /// </summary>
        public bool IsWithinBandwidthLimit(int bytesToSend, TimeSpan timeWindow)
        {
            if (_currentCondition?.Bandwidth <= 0)
                return true; // No limit
                
            var bitsPerSecond = _currentCondition.Bandwidth;
            var bytesPerSecond = bitsPerSecond / 8;
            var bytesAllowedInWindow = bytesPerSecond * timeWindow.TotalSeconds;
            
            return bytesToSend <= bytesAllowedInWindow;
        }
        
        private async Task ApplyLinuxConditionsAsync(NetworkCondition condition)
        {
            try
            {
                // Clear existing rules first
                await ClearLinuxConditionsAsync();
                
                // Build tc command
                var tcCommand = $"tc qdisc add dev {_networkInterface} root netem";
                
                // Add latency and jitter
                if (condition.LatencyMs > 0)
                {
                    tcCommand += $" delay {condition.LatencyMs}ms";
                    if (condition.JitterMs > 0)
                    {
                        tcCommand += $" {condition.JitterMs}ms";
                    }
                }
                
                // Add packet loss
                if (condition.PacketLoss > 0)
                {
                    tcCommand += $" loss {condition.PacketLoss * 100}%";
                }
                
                // Add bandwidth limit using tbf (Token Bucket Filter)
                if (condition.Bandwidth > 0)
                {
                    // First add netem for latency/loss
                    await ExecuteShellCommand("sudo", tcCommand);
                    
                    // Then add tbf for bandwidth limiting
                    var tbfCommand = $"tc qdisc add dev {_networkInterface} parent 1:1 handle 10: tbf " +
                                   $"rate {condition.Bandwidth}bit burst 32kbit latency 400ms";
                    await ExecuteShellCommand("sudo", tbfCommand);
                }
                else
                {
                    await ExecuteShellCommand("sudo", tcCommand);
                }
                
                _logger.LogInformation("Applied Linux network conditions successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply Linux network conditions. Ensure tc is installed and you have sudo access.");
            }
        }
        
        private async Task ClearLinuxConditionsAsync()
        {
            try
            {
                await ExecuteShellCommand("sudo", $"tc qdisc del dev {_networkInterface} root 2>/dev/null || true");
                _logger.LogInformation("Cleared Linux network conditions");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clear Linux network conditions");
            }
        }
        
        private async Task ApplyWindowsConditionsAsync(NetworkCondition condition)
        {
            try
            {
                // Check if clumsy is available
                var clumsyPath = FindClumsyPath();
                if (string.IsNullOrEmpty(clumsyPath))
                {
                    _logger.LogWarning("clumsy.exe not found. Install from https://jagt.github.io/clumsy/");
                    _logger.LogInformation("Falling back to application-level emulation");
                    return;
                }
                
                // Build clumsy filter
                var filter = "udp"; // Target UDP traffic
                var args = $"-f \"{filter}\"";
                
                // Add latency
                if (condition.LatencyMs > 0)
                {
                    args += $" --lag on --lag-time {condition.LatencyMs}";
                }
                
                // Add packet loss
                if (condition.PacketLoss > 0)
                {
                    args += $" --drop on --drop-chance {condition.PacketLoss * 100:F1}";
                }
                
                // Add bandwidth throttling (clumsy supports this via --throttle)
                if (condition.Bandwidth > 0)
                {
                    var kbps = condition.Bandwidth / 1000;
                    args += $" --throttle on --throttle-bandwidth {kbps}";
                }
                
                await ExecuteShellCommand(clumsyPath, args);
                _logger.LogInformation("Applied Windows network conditions using clumsy");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply Windows network conditions");
            }
        }
        
        private async Task ClearWindowsConditionsAsync()
        {
            try
            {
                // Stop clumsy if running
                await ExecuteShellCommand("taskkill", "/F /IM clumsy.exe 2>nul || exit 0");
                _logger.LogInformation("Cleared Windows network conditions");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clear Windows network conditions");
            }
        }
        
        private string FindClumsyPath()
        {
            // Common locations for clumsy
            var paths = new[]
            {
                @"C:\Program Files\clumsy\clumsy.exe",
                @"C:\Program Files (x86)\clumsy\clumsy.exe",
                @"C:\Tools\clumsy\clumsy.exe",
                @".\clumsy\clumsy.exe"
            };
            
            foreach (var path in paths)
            {
                if (System.IO.File.Exists(path))
                    return path;
            }
            
            return string.Empty;
        }
        
        private async Task<string> ExecuteShellCommand(string command, string arguments)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            if (process.ExitCode != 0 && !string.IsNullOrEmpty(error))
            {
                throw new Exception($"Command failed: {error}");
            }
            
            return output;
        }
    }
    
    /// <summary>
    /// Pre-defined network condition profiles for common scenarios.
    /// </summary>
    public static class NetworkProfiles
    {
        public static NetworkCondition Perfect => new()
        {
            Name = "perfect",
            LatencyMs = 0,
            JitterMs = 0,
            PacketLoss = 0.0,
            Bandwidth = 0 // Unlimited
        };
        
        public static NetworkCondition LAN => new()
        {
            Name = "lan",
            LatencyMs = 1,
            JitterMs = 0,
            PacketLoss = 0.0,
            Bandwidth = 1_000_000_000 // 1 Gbps
        };
        
        public static NetworkCondition WiFi => new()
        {
            Name = "wifi",
            LatencyMs = 5,
            JitterMs = 2,
            PacketLoss = 0.001, // 0.1%
            Bandwidth = 100_000_000 // 100 Mbps
        };
        
        public static NetworkCondition Regional => new()
        {
            Name = "regional",
            LatencyMs = 30,
            JitterMs = 5,
            PacketLoss = 0.001, // 0.1%
            Bandwidth = 100_000_000 // 100 Mbps
        };
        
        public static NetworkCondition CrossCountry => new()
        {
            Name = "cross-country",
            LatencyMs = 80,
            JitterMs = 10,
            PacketLoss = 0.005, // 0.5%
            Bandwidth = 50_000_000 // 50 Mbps
        };
        
        public static NetworkCondition International => new()
        {
            Name = "international",
            LatencyMs = 150,
            JitterMs = 20,
            PacketLoss = 0.01, // 1%
            Bandwidth = 25_000_000 // 25 Mbps
        };
        
        public static NetworkCondition Mobile4G => new()
        {
            Name = "mobile-4g",
            LatencyMs = 50,
            JitterMs = 15,
            PacketLoss = 0.02, // 2%
            Bandwidth = 10_000_000 // 10 Mbps
        };
        
        public static NetworkCondition Mobile3G => new()
        {
            Name = "mobile-3g",
            LatencyMs = 120,
            JitterMs = 30,
            PacketLoss = 0.05, // 5%
            Bandwidth = 2_000_000 // 2 Mbps
        };
        
        public static NetworkCondition Congested => new()
        {
            Name = "congested",
            LatencyMs = 200,
            JitterMs = 50,
            PacketLoss = 0.1, // 10%
            Bandwidth = 1_000_000 // 1 Mbps
        };
        
        public static NetworkCondition Satellite => new()
        {
            Name = "satellite",
            LatencyMs = 600,
            JitterMs = 100,
            PacketLoss = 0.03, // 3%
            Bandwidth = 5_000_000 // 5 Mbps
        };
        
        /// <summary>
        /// Gets all predefined profiles.
        /// </summary>
        public static NetworkCondition[] GetAll() => new[]
        {
            Perfect,
            LAN,
            WiFi,
            Regional,
            CrossCountry,
            International,
            Mobile4G,
            Mobile3G,
            Congested,
            Satellite
        };
        
        /// <summary>
        /// Gets a profile by name.
        /// </summary>
        public static NetworkCondition? GetByName(string name)
        {
            return name?.ToLowerInvariant() switch
            {
                "perfect" => Perfect,
                "lan" => LAN,
                "wifi" => WiFi,
                "regional" => Regional,
                "cross-country" => CrossCountry,
                "international" => International,
                "mobile-4g" or "4g" => Mobile4G,
                "mobile-3g" or "3g" => Mobile3G,
                "congested" => Congested,
                "satellite" => Satellite,
                _ => null
            };
        }
    }
}