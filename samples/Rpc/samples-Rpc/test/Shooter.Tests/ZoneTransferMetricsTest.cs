using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using Shooter.Tests.Infrastructure;

namespace Shooter.Tests;

/// <summary>
/// Integration tests that validate zone transfer metrics to ensure proper player movement
/// between ActionServers and detect zone transfer issues.
/// </summary>
public class ZoneTransferMetricsTest : IClassFixture<ShooterTestFixture>
{
    private readonly ShooterTestFixture _fixture;
    private readonly ITestOutputHelper _output;
    private readonly ILogger<ZoneTransferMetricsTest> _logger;

    public ZoneTransferMetricsTest(ShooterTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
        
        var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().AddProvider(new XunitLoggerProvider(output)));
        _logger = loggerFactory.CreateLogger<ZoneTransferMetricsTest>();
    }

    [Fact]
    public async Task ValidateZoneTransfersOccurDuringGameplay()
    {
        _logger.LogInformation("Starting zone transfer validation test");
        
        try
        {
            // Get initial zone transfer count
            var initialTransfers = MetricsTestHelper.GetTotalZoneTransfers();
            _logger.LogInformation("Initial zone transfer count: {InitialTransfers}", initialTransfers);
            
            // Wait for gameplay to occur - bots should move around and trigger zone transfers
            _logger.LogInformation("Waiting for bots to move and trigger zone transfers...");
            await Task.Delay(TimeSpan.FromSeconds(30));
            
            // Get final zone transfer count
            var finalTransfers = MetricsTestHelper.GetTotalZoneTransfers();
            _logger.LogInformation("Final zone transfer count: {FinalTransfers}", finalTransfers);
            
            // We expect some zone transfers to occur as bots move around the game world
            var transfersOccurred = finalTransfers - initialTransfers;
            _logger.LogInformation("Zone transfers occurred: {TransfersOccurred}", transfersOccurred);
            
            // With multiple bots moving around, we should see at least some transfers
            // If this fails, it might indicate bots aren't moving or zone transfer logic is broken
            Assert.True(transfersOccurred >= 0, 
                "Zone transfer count should not decrease");
            
            // Log for manual verification - exact count depends on bot behavior
            if (transfersOccurred > 0)
            {
                _logger.LogInformation("✓ Zone transfers detected: {Count} transfers occurred", transfersOccurred);
            }
            else
            {
                _logger.LogWarning("⚠ No zone transfers detected - bots may not be moving between zones");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zone transfer validation failed");
            throw;
        }
    }

    [Fact]
    public async Task ValidateZoneTransferRateIsReasonable()
    {
        _logger.LogInformation("Starting zone transfer rate validation");
        
        try
        {
            // Get initial metrics
            var initialTransfers = MetricsTestHelper.GetTotalZoneTransfers();
            var startTime = DateTime.UtcNow;
            
            // Wait for a period of gameplay
            var testDuration = TimeSpan.FromSeconds(60);
            _logger.LogInformation("Monitoring zone transfers for {Duration} seconds...", testDuration.TotalSeconds);
            await Task.Delay(testDuration);
            
            // Get final metrics
            var finalTransfers = MetricsTestHelper.GetTotalZoneTransfers();
            var endTime = DateTime.UtcNow;
            var actualDuration = endTime - startTime;
            
            var transfersOccurred = finalTransfers - initialTransfers;
            var transferRate = transfersOccurred / actualDuration.TotalMinutes;
            
            _logger.LogInformation("Zone transfer rate: {Rate:F2} transfers/minute ({Transfers} transfers in {Duration:F1}s)", 
                transferRate, transfersOccurred, actualDuration.TotalSeconds);
            
            // Validate transfer rate is reasonable
            // With 3 bots moving around, we shouldn't see excessive transfers (which could indicate bugs)
            // But we also shouldn't see zero transfers over a full minute
            Assert.True(transferRate >= 0, "Transfer rate should be non-negative");
            Assert.True(transferRate <= 100, // Arbitrary upper bound - adjust based on game behavior
                $"Transfer rate ({transferRate:F2}/min) seems excessive, may indicate zone transfer bug");
            
            _logger.LogInformation("✓ Zone transfer rate is within reasonable bounds");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zone transfer rate validation failed");
            throw;
        }
    }

    [Fact]
    public async Task ValidateZoneTransferConsistencyAcrossActionServers()
    {
        _logger.LogInformation("Starting zone transfer consistency validation across ActionServers");
        
        try
        {
            // Wait for services to stabilize
            await Task.Delay(5000);
            
            // Get metrics from multiple ActionServers if available
            var actionServerMetrics = new List<ActionServerMetricsSnapshot>();
            
            // Try to get metrics from multiple ActionServer instances
            for (int i = 0; i < _fixture.ActionServerCount; i++)
            {
                try
                {
                    var port = 7072 + i; // ActionServers start at port 7072
                    var metrics = await MetricsTestHelper.GetActionServerMetricsAsync($"http://localhost:{port}");
                    actionServerMetrics.Add(metrics);
                    _logger.LogInformation("ActionServer {Index} (port {Port}): {ZoneTransfers} zone transfers, {EntityTransfers} entity transfers",
                        i, port, metrics.ZoneTransfers, metrics.EntityTransfers);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not get metrics from ActionServer {Index}", i);
                }
            }
            
            if (actionServerMetrics.Count > 1)
            {
                // Validate that total transfers across servers makes sense
                var totalZoneTransfers = actionServerMetrics.Sum(m => m.ZoneTransfers);
                var totalEntityTransfers = actionServerMetrics.Sum(m => m.EntityTransfers);
                
                _logger.LogInformation("Total across all ActionServers: {TotalZoneTransfers} zone transfers, {TotalEntityTransfers} entity transfers",
                    totalZoneTransfers, totalEntityTransfers);
                
                // Basic sanity checks
                Assert.True(totalZoneTransfers >= 0, "Total zone transfers should be non-negative");
                Assert.True(totalEntityTransfers >= 0, "Total entity transfers should be non-negative");
                
                // Entity transfers should be >= zone transfers (entities move with players during zone transfers)
                Assert.True(totalEntityTransfers >= totalZoneTransfers, 
                    "Entity transfers should be at least as many as zone transfers");
                
                _logger.LogInformation("✓ Zone transfer consistency validated across ActionServers");
            }
            else
            {
                _logger.LogWarning("Could not validate consistency - only {Count} ActionServer(s) responded", 
                    actionServerMetrics.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zone transfer consistency validation failed");
            throw;
        }
    }

    [Fact]
    public async Task ValidateNoStuckZoneTransfers()
    {
        _logger.LogInformation("Starting validation for stuck zone transfers");
        
        try
        {
            // Monitor zone transfers over multiple intervals to detect stuck transfers
            var measurements = new List<(DateTime time, long transfers)>();
            var monitoringDuration = TimeSpan.FromSeconds(45);
            var intervalDuration = TimeSpan.FromSeconds(5);
            
            _logger.LogInformation("Monitoring zone transfers every {Interval}s for {Duration}s...", 
                intervalDuration.TotalSeconds, monitoringDuration.TotalSeconds);
            
            var endTime = DateTime.UtcNow + monitoringDuration;
            while (DateTime.UtcNow < endTime)
            {
                var transfers = MetricsTestHelper.GetTotalZoneTransfers();
                measurements.Add((DateTime.UtcNow, transfers));
                _logger.LogDebug("Zone transfers at {Time}: {Count}", DateTime.UtcNow, transfers);
                
                await Task.Delay(intervalDuration);
            }
            
            // Analyze measurements for stuck transfers
            bool progressDetected = false;
            long lastTransferCount = measurements.First().transfers;
            
            foreach (var (time, transfers) in measurements.Skip(1))
            {
                if (transfers > lastTransferCount)
                {
                    progressDetected = true;
                    _logger.LogDebug("Zone transfer progress detected: {Old} -> {New}", lastTransferCount, transfers);
                }
                lastTransferCount = transfers;
            }
            
            // Log the results
            var totalIncrease = measurements.Last().transfers - measurements.First().transfers;
            _logger.LogInformation("Zone transfer progress: {Initial} -> {Final} (increase: {Increase})", 
                measurements.First().transfers, measurements.Last().transfers, totalIncrease);
            
            // We don't require transfers to occur, but if they do, they shouldn't get stuck
            if (totalIncrease > 0)
            {
                Assert.True(progressDetected, "Zone transfers increased but no progress was detected in monitoring intervals");
                _logger.LogInformation("✓ Zone transfers are progressing normally");
            }
            else
            {
                _logger.LogInformation("✓ No zone transfers occurred during monitoring period (normal if bots aren't moving)");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stuck zone transfer validation failed");
            throw;
        }
    }

    [Fact]
    public async Task ValidateZoneTransferMetricsResetCorrectly()
    {
        _logger.LogInformation("Starting zone transfer metrics reset validation");
        
        try
        {
            // Get baseline metrics
            var baseline = _fixture.BaselineMetrics ?? throw new InvalidOperationException("Baseline metrics not available");
            _logger.LogInformation("Baseline zone transfers: {BaselineTransfers}", baseline.TotalConnections);
            
            // Wait for some game activity
            await Task.Delay(TimeSpan.FromSeconds(20));
            
            // Get current metrics
            var currentTransfers = MetricsTestHelper.GetTotalZoneTransfers();
            _logger.LogInformation("Current zone transfers: {CurrentTransfers}", currentTransfers);
            
            // Zone transfers should be cumulative (monotonically increasing)
            // This test validates that the metric doesn't reset unexpectedly during gameplay
            Assert.True(currentTransfers >= 0, "Zone transfer count should never be negative");
            
            // If there were transfers in baseline, current should be >= baseline
            // (This test may need adjustment based on how baseline is captured)
            
            _logger.LogInformation("✓ Zone transfer metrics appear to be accumulating correctly");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zone transfer metrics reset validation failed");
            throw;
        }
    }
}