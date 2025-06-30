using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using Shooter.Tests.Infrastructure;

namespace Shooter.Tests;

/// <summary>
/// Integration tests that validate player count metrics to catch bugs like inflated player counts.
/// Addresses the issue where "actionserver_active_players at 14" appeared too high.
/// </summary>
public class PlayerCountValidationTest : IClassFixture<ShooterTestFixture>
{
    private readonly ShooterTestFixture _fixture;
    private readonly ITestOutputHelper _output;
    private readonly ILogger<PlayerCountValidationTest> _logger;

    public PlayerCountValidationTest(ShooterTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
        
        var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().AddProvider(new XunitLoggerProvider(output)));
        _logger = loggerFactory.CreateLogger<PlayerCountValidationTest>();
    }

    [Fact]
    public async Task ValidatePlayerCountsAreReasonable()
    {
        _logger.LogInformation("Starting player count validation test");
        
        // Wait for services to stabilize
        await Task.Delay(5000);
        
        try
        {
            // Get current active player count
            var activePlayerCount = MetricsTestHelper.GetActivePlayerCount();
            _logger.LogInformation("Current active player count: {ActivePlayerCount}", activePlayerCount);
            
            // With the default configuration (3 bots), we should never see more than 3 active players
            // The bug showed 14 players, which is clearly wrong
            Assert.True(activePlayerCount <= _fixture.BotCount, 
                $"Active player count ({activePlayerCount}) should not exceed bot count ({_fixture.BotCount})");
            
            // Player count should be non-negative
            Assert.True(activePlayerCount >= 0, 
                $"Active player count should be non-negative, got {activePlayerCount}");
            
            _logger.LogInformation("✓ Player count validation passed: {ActivePlayerCount} <= {MaxExpected}", 
                activePlayerCount, _fixture.BotCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Player count validation failed");
            throw;
        }
    }

    [Fact]
    public async Task ValidatePlayerCountConsistencyBetweenServices()
    {
        _logger.LogInformation("Starting player count consistency validation");
        
        // Wait for services to stabilize
        await Task.Delay(5000);
        
        try
        {
            // Get metrics from both ActionServer and Silo
            var actionServerMetrics = await MetricsTestHelper.GetActionServerMetricsAsync();
            var siloMetrics = await MetricsTestHelper.GetSiloMetricsAsync();
            
            _logger.LogInformation("ActionServer reports {ActionServerPlayers} active players", 
                actionServerMetrics.ActivePlayers);
            _logger.LogInformation("Silo reports {SiloPlayers} active players", 
                siloMetrics.ActivePlayers);
            
            // Player counts should be consistent between services
            Assert.Equal(actionServerMetrics.ActivePlayers, siloMetrics.ActivePlayers);
            
            _logger.LogInformation("✓ Player count consistency validated between services");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Player count consistency validation failed");
            throw;
        }
    }

    [Fact]
    public async Task ValidatePlayerCountsAfterBotConnections()
    {
        _logger.LogInformation("Starting player count validation after bot connections");
        
        try
        {
            // Wait for all bots to connect and stabilize
            _logger.LogInformation("Waiting for {BotCount} bots to connect...", _fixture.BotCount);
            await MetricsTestHelper.WaitForPlayerCount(_fixture.BotCount, TimeSpan.FromMinutes(2));
            
            var activePlayerCount = MetricsTestHelper.GetActivePlayerCount();
            _logger.LogInformation("Final active player count: {ActivePlayerCount}", activePlayerCount);
            
            // Should have exactly the number of bots as active players
            Assert.Equal(_fixture.BotCount, activePlayerCount);
            
            _logger.LogInformation("✓ Player count after bot connections validated: {ExpectedCount} players active", 
                _fixture.BotCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Player count validation after bot connections failed");
            throw;
        }
    }

    [Fact]
    public async Task ValidateNoPlayerCountLeaksOverTime()
    {
        _logger.LogInformation("Starting player count leak detection test");
        
        try
        {
            // Get baseline metrics
            var baseline = _fixture.BaselineMetrics ?? throw new InvalidOperationException("Baseline metrics not available");
            _logger.LogInformation("Baseline player count: {BaselineCount}", baseline.ActivePlayers);
            
            // Wait for system to run for a while
            await Task.Delay(TimeSpan.FromSeconds(30));
            
            // Get current metrics
            var current = MetricsSnapshot.Current;
            _logger.LogInformation("Current player count: {CurrentCount}", current.ActivePlayers);
            
            // Check for resource leaks
            MetricsTestHelper.AssertNoResourceLeaks(baseline, current, 
                "Player count should not leak over time during normal operation");
            
            _logger.LogInformation("✓ No player count leaks detected");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Player count leak detection failed");
            throw;
        }
    }

    [Fact]
    public async Task ValidatePlayerMetricsUnderLoad()
    {
        _logger.LogInformation("Starting player metrics validation under load");
        
        try
        {
            // Get initial count
            var initialCount = MetricsTestHelper.GetActivePlayerCount();
            _logger.LogInformation("Initial player count: {InitialCount}", initialCount);
            
            // Wait for game activity to generate load
            await Task.Delay(TimeSpan.FromSeconds(15));
            
            // Player count should remain stable during gameplay
            var finalCount = MetricsTestHelper.GetActivePlayerCount();
            _logger.LogInformation("Final player count: {FinalCount}", finalCount);
            
            // Allow for small variations but catch major discrepancies
            var difference = Math.Abs(finalCount - initialCount);
            Assert.True(difference <= 1, 
                $"Player count changed by {difference} during load test, which may indicate a counting bug");
            
            // Ensure count is still reasonable
            Assert.True(finalCount <= _fixture.BotCount + 1, 
                $"Player count under load ({finalCount}) exceeded reasonable limit");
            
            _logger.LogInformation("✓ Player metrics remained stable under load");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Player metrics validation under load failed");
            throw;
        }
    }
}

/// <summary>
/// Custom logger provider for XUnit test output.
/// </summary>
public class XunitLoggerProvider : ILoggerProvider
{
    private readonly ITestOutputHelper _output;

    public XunitLoggerProvider(ITestOutputHelper output)
    {
        _output = output;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new XunitLogger(_output, categoryName);
    }

    public void Dispose() { }
}

/// <summary>
/// Custom logger that writes to XUnit test output.
/// </summary>
public class XunitLogger : ILogger
{
    private readonly ITestOutputHelper _output;
    private readonly string _categoryName;

    public XunitLogger(ITestOutputHelper output, string categoryName)
    {
        _output = output;
        _categoryName = categoryName;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        try
        {
            var message = formatter(state, exception);
            _output.WriteLine($"[{logLevel}] {_categoryName}: {message}");
            if (exception != null)
            {
                _output.WriteLine(exception.ToString());
            }
        }
        catch
        {
            // Ignore errors writing to test output
        }
    }
}