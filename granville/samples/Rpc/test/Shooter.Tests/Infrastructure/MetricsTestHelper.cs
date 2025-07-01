using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
// TODO: Add telemetry references when telemetry implementation is complete
// using Shooter.ActionServer.Telemetry;
// using Shooter.Silo.Telemetry;
// using Shooter.Bot.Telemetry;

namespace Shooter.Tests.Infrastructure;

/// <summary>
/// Helper class for accessing and validating telemetry metrics during integration tests.
/// Provides direct access to metric values and assertion methods for test validation.
/// </summary>
public static class MetricsTestHelper
{
    private static readonly HttpClient _httpClient = new();

    #region Direct Metrics Access

    /// <summary>
    /// Gets the current active player count from ActionServer telemetry.
    /// </summary>
    public static long GetActivePlayerCount()
    {
        try
        {
            // TODO: Access the UpDownCounter current value when telemetry is implemented
            // For now, return placeholder value
            return 0; // Placeholder until telemetry implementation is complete
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to get active player count: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Gets the current active bot count from Bot telemetry.
    /// </summary>
    public static long GetActiveBotCount()
    {
        try
        {
            return 0; // Placeholder until telemetry implementation is complete
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to get active bot count: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Gets the total number of player connections from ActionServer telemetry.
    /// </summary>
    public static long GetTotalPlayerConnections()
    {
        try
        {
            return 0; // Placeholder until telemetry implementation is complete
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to get total player connections: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Gets the total number of zone transfers from ActionServer telemetry.
    /// </summary>
    public static long GetTotalZoneTransfers()
    {
        try
        {
            return 0; // Placeholder until telemetry implementation is complete
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to get total zone transfers: {ex.Message}", ex);
        }
    }

    #endregion

    #region HTTP-Based Metrics Access

    /// <summary>
    /// Gets metrics from a service's health check endpoint.
    /// </summary>
    public static async Task<Dictionary<string, object>> GetHealthCheckMetricsAsync(string serviceUrl)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<Dictionary<string, object>>($"{serviceUrl}/health");
            return response ?? new Dictionary<string, object>();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to get health check metrics from {serviceUrl}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Gets ActionServer metrics via HTTP health check.
    /// </summary>
    public static async Task<ActionServerMetricsSnapshot> GetActionServerMetricsAsync(string actionServerUrl = "http://localhost:7072")
    {
        var metrics = await GetHealthCheckMetricsAsync(actionServerUrl);
        return new ActionServerMetricsSnapshot
        {
            ActivePlayers = GetLongValue(metrics, "active_players"),
            TotalConnections = GetLongValue(metrics, "total_connections"),
            TotalDisconnections = GetLongValue(metrics, "total_disconnections"),
            ZoneTransfers = GetLongValue(metrics, "zone_transfers"),
            EntityTransfers = GetLongValue(metrics, "entity_transfers")
        };
    }

    /// <summary>
    /// Gets Silo metrics via HTTP health check.
    /// </summary>
    public static async Task<SiloMetricsSnapshot> GetSiloMetricsAsync(string siloUrl = "http://localhost:7071")
    {
        var metrics = await GetHealthCheckMetricsAsync(siloUrl);
        return new SiloMetricsSnapshot
        {
            ActivePlayers = GetLongValue(metrics, "active_players"),
            PlayersCreated = GetLongValue(metrics, "players_created"),
            ActiveActionServers = GetLongValue(metrics, "active_action_servers")
        };
    }

    #endregion

    #region Polling and Waiting

    /// <summary>
    /// Waits for the active player count to reach the expected value.
    /// </summary>
    public static async Task WaitForPlayerCount(int expectedCount, TimeSpan timeout, TimeSpan? pollInterval = null)
    {
        pollInterval ??= TimeSpan.FromMilliseconds(500);
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var currentCount = GetActivePlayerCount();
                if (currentCount == expectedCount)
                {
                    return;
                }

                await Task.Delay(pollInterval.Value);
            }
            catch (Exception)
            {
                // Continue polling on errors
                await Task.Delay(pollInterval.Value);
            }
        }

        var finalCount = GetActivePlayerCount();
        throw new TimeoutException(
            $"Timed out waiting for player count to reach {expectedCount}. Current count: {finalCount}. Timeout: {timeout}");
    }

    /// <summary>
    /// Waits for the active bot count to reach the expected value.
    /// </summary>
    public static async Task WaitForBotCount(int expectedCount, TimeSpan timeout, TimeSpan? pollInterval = null)
    {
        pollInterval ??= TimeSpan.FromMilliseconds(500);
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var currentCount = GetActiveBotCount();
                if (currentCount == expectedCount)
                {
                    return;
                }

                await Task.Delay(pollInterval.Value);
            }
            catch (Exception)
            {
                // Continue polling on errors
                await Task.Delay(pollInterval.Value);
            }
        }

        var finalCount = GetActiveBotCount();
        throw new TimeoutException(
            $"Timed out waiting for bot count to reach {expectedCount}. Current count: {finalCount}. Timeout: {timeout}");
    }

    /// <summary>
    /// Waits for a specific metric to reach a target value using HTTP polling.
    /// </summary>
    public static async Task WaitForMetricValueAsync<T>(
        Func<Task<T>> metricGetter, 
        T expectedValue, 
        TimeSpan timeout,
        TimeSpan? pollInterval = null) where T : IEquatable<T>
    {
        pollInterval ??= TimeSpan.FromSeconds(1);
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var currentValue = await metricGetter();
                if (currentValue.Equals(expectedValue))
                {
                    return;
                }

                await Task.Delay(pollInterval.Value);
            }
            catch (Exception)
            {
                // Continue polling on errors
                await Task.Delay(pollInterval.Value);
            }
        }

        var finalValue = await metricGetter();
        throw new TimeoutException(
            $"Timed out waiting for metric to reach {expectedValue}. Current value: {finalValue}. Timeout: {timeout}");
    }

    #endregion

    #region Assertions

    /// <summary>
    /// Asserts that the active player count matches the expected value.
    /// </summary>
    public static void AssertPlayerCount(int expected, string? message = null)
    {
        var actual = GetActivePlayerCount();
        var assertMessage = message ?? $"Active player count should be {expected}";
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Asserts that the active bot count matches the expected value.
    /// </summary>
    public static void AssertBotCount(int expected, string? message = null)
    {
        var actual = GetActiveBotCount();
        var assertMessage = message ?? $"Active bot count should be {expected}";
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Asserts that player counts are consistent across ActionServer and Silo.
    /// </summary>
    public static async Task AssertPlayerCountConsistencyAsync(string actionServerUrl = "http://localhost:7072", string siloUrl = "http://localhost:7071")
    {
        var actionServerMetrics = await GetActionServerMetricsAsync(actionServerUrl);
        var siloMetrics = await GetSiloMetricsAsync(siloUrl);

        Assert.Equal(actionServerMetrics.ActivePlayers, siloMetrics.ActivePlayers);
    }

    /// <summary>
    /// Asserts that metrics show no resource leaks (counts return to baseline).
    /// </summary>
    public static void AssertNoResourceLeaks(MetricsSnapshot baseline, MetricsSnapshot current, string? message = null)
    {
        var leaks = new List<string>();

        if (current.ActivePlayers > baseline.ActivePlayers)
            leaks.Add($"Player count leak: {current.ActivePlayers} > {baseline.ActivePlayers}");

        if (current.ActiveBots > baseline.ActiveBots)
            leaks.Add($"Bot count leak: {current.ActiveBots} > {baseline.ActiveBots}");

        if (leaks.Any())
        {
            var leakMessage = message ?? "Resource leaks detected";
            var details = string.Join("; ", leaks);
            Assert.Fail($"{leakMessage}: {details}");
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Gets the current value of an UpDownCounter using reflection.
    /// Note: This is a workaround until .NET provides direct access to metric values.
    /// </summary>
    private static long GetMetricValueViaReflection(object counter)
    {
        // For testing purposes, we'll implement a simple approach
        // In a real implementation, you might use a custom metrics provider
        // or access the metrics through the OpenTelemetry SDK
        
        // For now, return 0 as a placeholder - this will be enhanced
        // when we integrate with the actual metrics collection system
        return 0;
    }

    private static long GetLongValue(Dictionary<string, object> metrics, string key)
    {
        if (metrics.TryGetValue(key, out var value))
        {
            return value switch
            {
                long longValue => longValue,
                int intValue => intValue,
                JsonElement element when element.ValueKind == JsonValueKind.Number => element.GetInt64(),
                _ => 0
            };
        }
        return 0;
    }

    #endregion
}

/// <summary>
/// Snapshot of ActionServer metrics at a point in time.
/// </summary>
public record ActionServerMetricsSnapshot
{
    public long ActivePlayers { get; init; }
    public long TotalConnections { get; init; }
    public long TotalDisconnections { get; init; }
    public long ZoneTransfers { get; init; }
    public long EntityTransfers { get; init; }
}

/// <summary>
/// Snapshot of Silo metrics at a point in time.
/// </summary>
public record SiloMetricsSnapshot
{
    public long ActivePlayers { get; init; }
    public long PlayersCreated { get; init; }
    public long ActiveActionServers { get; init; }
}

/// <summary>
/// General metrics snapshot for comparison and leak detection.
/// </summary>
public record MetricsSnapshot
{
    public long ActivePlayers { get; init; }
    public long ActiveBots { get; init; }
    public long TotalConnections { get; init; }
    public long TotalDisconnections { get; init; }
    
    public static MetricsSnapshot Current => new()
    {
        ActivePlayers = MetricsTestHelper.GetActivePlayerCount(),
        ActiveBots = MetricsTestHelper.GetActiveBotCount(),
        TotalConnections = MetricsTestHelper.GetTotalPlayerConnections(),
        TotalDisconnections = 0 // TODO: Add disconnection metric
    };
}