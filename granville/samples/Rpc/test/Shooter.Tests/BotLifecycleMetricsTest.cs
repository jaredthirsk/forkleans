using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using Shooter.Tests.Infrastructure;

namespace Shooter.Tests;

/// <summary>
/// Integration tests that validate bot lifecycle metrics to ensure proper bot connection,
/// disconnection, and state tracking throughout the game session.
/// </summary>
public class BotLifecycleMetricsTest : IClassFixture<ShooterTestFixture>
{
    private readonly ShooterTestFixture _fixture;
    private readonly ITestOutputHelper _output;
    private readonly ILogger<BotLifecycleMetricsTest> _logger;

    public BotLifecycleMetricsTest(ShooterTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
        
        var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().AddProvider(new XunitLoggerProvider(output)));
        _logger = loggerFactory.CreateLogger<BotLifecycleMetricsTest>();
    }

    [Fact]
    public async Task ValidateAllBotsConnectSuccessfully()
    {
        _logger.LogInformation("Starting bot connection validation for {BotCount} bots", _fixture.BotCount);
        
        try
        {
            // Wait for all bots to connect with timeout
            var timeout = TimeSpan.FromMinutes(3);
            _logger.LogInformation("Waiting up to {Timeout} for all bots to connect...", timeout);
            
            await MetricsTestHelper.WaitForBotCount(_fixture.BotCount, timeout);
            
            // Verify final bot count
            var activeBots = MetricsTestHelper.GetActiveBotCount();
            _logger.LogInformation("Active bot count after connection: {ActiveBots}", activeBots);
            
            Assert.Equal(_fixture.BotCount, activeBots);
            
            _logger.LogInformation("✓ All {BotCount} bots connected successfully", _fixture.BotCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bot connection validation failed");
            throw;
        }
    }

    [Fact]
    public async Task ValidateBotConnectionMetrics()
    {
        _logger.LogInformation("Starting bot connection metrics validation");
        
        try
        {
            // Wait for system to stabilize
            await Task.Delay(5000);
            
            // Get connection metrics
            var totalConnections = MetricsTestHelper.GetTotalPlayerConnections();
            var activeBots = MetricsTestHelper.GetActiveBotCount();
            
            _logger.LogInformation("Total connections: {TotalConnections}, Active bots: {ActiveBots}", 
                totalConnections, activeBots);
            
            // Basic validations
            Assert.True(totalConnections >= activeBots, 
                "Total connections should be at least as many as active bots");
            
            Assert.True(activeBots >= 0, "Active bot count should be non-negative");
            Assert.True(activeBots <= _fixture.BotCount, 
                $"Active bot count ({activeBots}) should not exceed configured bot count ({_fixture.BotCount})");
            
            _logger.LogInformation("✓ Bot connection metrics are valid");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bot connection metrics validation failed");
            throw;
        }
    }

    [Fact]
    public async Task ValidateBotStabilityOverTime()
    {
        _logger.LogInformation("Starting bot stability validation over time");
        
        try
        {
            // Get initial bot count
            var initialBots = MetricsTestHelper.GetActiveBotCount();
            _logger.LogInformation("Initial active bot count: {InitialBots}", initialBots);
            
            // Monitor bot count over time to ensure stability
            var monitoringDuration = TimeSpan.FromMinutes(2);
            var checkInterval = TimeSpan.FromSeconds(10);
            var measurements = new List<(DateTime time, long botCount)>();
            
            _logger.LogInformation("Monitoring bot stability for {Duration} minutes...", monitoringDuration.TotalMinutes);
            
            var endTime = DateTime.UtcNow + monitoringDuration;
            while (DateTime.UtcNow < endTime)
            {
                var botCount = MetricsTestHelper.GetActiveBotCount();
                measurements.Add((DateTime.UtcNow, botCount));
                _logger.LogDebug("Bot count at {Time}: {Count}", DateTime.UtcNow, botCount);
                
                await Task.Delay(checkInterval);
            }
            
            // Analyze measurements for stability
            var botCounts = measurements.Select(m => m.botCount).ToList();
            var minBots = botCounts.Min();
            var maxBots = botCounts.Max();
            var avgBots = botCounts.Average();
            
            _logger.LogInformation("Bot count stability: Min={Min}, Max={Max}, Avg={Avg:F1}", 
                minBots, maxBots, avgBots);
            
            // Bot count should remain stable (allowing for brief disconnections/reconnections)
            var variance = maxBots - minBots;
            Assert.True(variance <= 1, 
                $"Bot count variance ({variance}) too high, indicating connection instability");
            
            // Final count should match initial (bots shouldn't permanently disconnect)
            var finalBots = measurements.Last().botCount;
            Assert.Equal(initialBots, finalBots);
            
            _logger.LogInformation("✓ Bot count remained stable over {Duration} minutes", monitoringDuration.TotalMinutes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bot stability validation failed");
            throw;
        }
    }

    [Fact]
    public async Task ValidateBotMetricsConsistencyWithPlayerMetrics()
    {
        _logger.LogInformation("Starting bot/player metrics consistency validation");
        
        try
        {
            // Wait for system to stabilize
            await Task.Delay(5000);
            
            // Get both bot and player metrics
            var activeBots = MetricsTestHelper.GetActiveBotCount();
            var activePlayers = MetricsTestHelper.GetActivePlayerCount();
            
            _logger.LogInformation("Active bots: {ActiveBots}, Active players: {ActivePlayers}", 
                activeBots, activePlayers);
            
            // In this system, all players are bots, so counts should match
            Assert.Equal(activeBots, activePlayers);
            
            _logger.LogInformation("✓ Bot and player metrics are consistent");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bot/player metrics consistency validation failed");
            throw;
        }
    }

    [Fact]
    public async Task ValidateBotLifecycleEvents()
    {
        _logger.LogInformation("Starting bot lifecycle events validation");
        
        try
        {
            // Get baseline metrics
            var baseline = _fixture.BaselineMetrics ?? throw new InvalidOperationException("Baseline metrics not available");
            _logger.LogInformation("Baseline bot count: {BaselineBots}", baseline.ActiveBots);
            
            // Wait for bots to complete their connection process
            await Task.Delay(TimeSpan.FromSeconds(15));
            
            // Get current metrics
            var currentBots = MetricsTestHelper.GetActiveBotCount();
            var totalConnections = MetricsTestHelper.GetTotalPlayerConnections();
            
            _logger.LogInformation("Current bots: {CurrentBots}, Total connections: {TotalConnections}", 
                currentBots, totalConnections);
            
            // Validate lifecycle progression
            Assert.True(currentBots >= baseline.ActiveBots, 
                "Bot count should have increased or remained stable from baseline");
            
            Assert.True(totalConnections > 0, 
                "Should have recorded some connection events");
            
            // In steady state, we should have expected number of bots
            if (currentBots == _fixture.BotCount)
            {
                _logger.LogInformation("✓ All bots have completed their lifecycle successfully");
            }
            else
            {
                _logger.LogWarning("⚠ Bot count ({Current}) doesn't match expected ({Expected})", 
                    currentBots, _fixture.BotCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bot lifecycle events validation failed");
            throw;
        }
    }

    [Fact]
    public async Task ValidateNoBotResourceLeaks()
    {
        _logger.LogInformation("Starting bot resource leak detection");
        
        try
        {
            // Get baseline
            var baseline = _fixture.BaselineMetrics ?? throw new InvalidOperationException("Baseline metrics not available");
            
            // Wait for extended gameplay period
            var testDuration = TimeSpan.FromMinutes(1);
            _logger.LogInformation("Running bot leak detection test for {Duration} minutes...", testDuration.TotalMinutes);
            await Task.Delay(testDuration);
            
            // Get current metrics
            var current = MetricsSnapshot.Current;
            
            _logger.LogInformation("Baseline bots: {BaselineBots}, Current bots: {CurrentBots}", 
                baseline.ActiveBots, current.ActiveBots);
            
            // Check for bot-specific leaks
            if (current.ActiveBots > baseline.ActiveBots)
            {
                var botLeak = current.ActiveBots - baseline.ActiveBots;
                _logger.LogWarning("Potential bot count leak detected: {Leak} additional bots", botLeak);
                
                // Allow for small variations but catch significant leaks
                Assert.True(botLeak <= 1, 
                    $"Bot count leak detected: {current.ActiveBots} > {baseline.ActiveBots}");
            }
            
            // Ensure bot count hasn't dropped unexpectedly
            Assert.True(current.ActiveBots >= baseline.ActiveBots - 1, 
                "Bot count should not drop significantly below baseline");
            
            _logger.LogInformation("✓ No significant bot resource leaks detected");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bot resource leak detection failed");
            throw;
        }
    }

    [Fact]
    public async Task ValidateBotConnectionRobustness()
    {
        _logger.LogInformation("Starting bot connection robustness validation");
        
        try
        {
            // Take multiple measurements to check connection robustness
            var measurements = new List<long>();
            var measurementCount = 6;
            var measurementInterval = TimeSpan.FromSeconds(10);
            
            _logger.LogInformation("Taking {Count} measurements at {Interval}s intervals...", 
                measurementCount, measurementInterval.TotalSeconds);
            
            for (int i = 0; i < measurementCount; i++)
            {
                var botCount = MetricsTestHelper.GetActiveBotCount();
                measurements.Add(botCount);
                _logger.LogDebug("Measurement {Index}: {BotCount} bots", i + 1, botCount);
                
                if (i < measurementCount - 1) // Don't wait after last measurement
                {
                    await Task.Delay(measurementInterval);
                }
            }
            
            // Analyze robustness
            var distinctCounts = measurements.Distinct().ToList();
            var maxDeviation = measurements.Max() - measurements.Min();
            
            _logger.LogInformation("Bot count measurements: [{Measurements}]", string.Join(", ", measurements));
            _logger.LogInformation("Distinct counts: {DistinctCounts}, Max deviation: {MaxDeviation}", 
                distinctCounts.Count, maxDeviation);
            
            // Connection should be robust - minimal variation
            Assert.True(maxDeviation <= 1, 
                $"Bot connection too unstable - max deviation: {maxDeviation}");
            
            // Most measurements should show full bot count
            var expectedBotMeasurements = measurements.Count(c => c == _fixture.BotCount);
            var robustnessPercentage = (expectedBotMeasurements * 100.0) / measurements.Count;
            
            _logger.LogInformation("Connection robustness: {Percentage:F1}% of measurements at expected count", 
                robustnessPercentage);
            
            // Expect at least 80% of measurements to show full bot count
            Assert.True(robustnessPercentage >= 80, 
                $"Bot connections not robust enough: only {robustnessPercentage:F1}% at expected count");
            
            _logger.LogInformation("✓ Bot connections are robust ({Percentage:F1}% stability)", robustnessPercentage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bot connection robustness validation failed");
            throw;
        }
    }
}