using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Collections.Concurrent;

namespace Shooter.Bot.Telemetry;

public static class BotTelemetry
{
    public static readonly ActivitySource ActivitySource = new("Shooter.Bot", "1.0.0");
    public static readonly Meter Meter = new("Shooter.Bot", "1.0.0");

    // Activity names
    public const string BotConnectionActivity = "Bot.Connection";
    public const string BotMovementActivity = "Bot.Movement";
    public const string BotShootingActivity = "Bot.Shooting";
    public const string BotZoneTransferActivity = "Bot.ZoneTransfer";

    // Metrics
    public static readonly Counter<long> BotConnections = Meter.CreateCounter<long>(
        "bot_connections_total",
        description: "Total number of bot connections");

    public static readonly Counter<long> BotDisconnections = Meter.CreateCounter<long>(
        "bot_disconnections_total",
        description: "Total number of bot disconnections");

    public static readonly Counter<long> BotActions = Meter.CreateCounter<long>(
        "bot_actions_total",
        description: "Total number of bot actions (move, shoot, etc.)");

    public static readonly Counter<long> BotZoneTransfers = Meter.CreateCounter<long>(
        "bot_zone_transfers_total",
        description: "Total number of bot zone transfers");

    public static readonly Histogram<double> ResponseTime = Meter.CreateHistogram<double>(
        "bot_response_time_ms",
        description: "Bot response time to server updates in milliseconds");

    public static readonly UpDownCounter<long> ActiveBots = Meter.CreateUpDownCounter<long>(
        "bot_active_count",
        description: "Current number of active bots");

    public static readonly ObservableGauge<int> CurrentZoneX = Meter.CreateObservableGauge<int>(
        "bot_current_zone_x",
        description: "Current zone X coordinate",
        observeValue: () => 0); // Will be updated based on bot position

    public static readonly ObservableGauge<int> CurrentZoneY = Meter.CreateObservableGauge<int>(
        "bot_current_zone_y",
        description: "Current zone Y coordinate",
        observeValue: () => 0); // Will be updated based on bot position

    // Log message metrics
    public static readonly Counter<long> LogMessages = Meter.CreateCounter<long>(
        "bot_log_messages_total",
        description: "Total number of log messages by level");
    
    public static readonly ObservableGauge<double> LogRate = Meter.CreateObservableGauge<double>(
        "bot_log_rate_per_minute",
        unit: "messages/minute",
        description: "Current log message rate per minute",
        observeValue: () => GetCurrentLogRate());

    // Log rate tracking
    private static readonly ConcurrentQueue<DateTime> _logTimestamps = new();
    private static readonly object _logRateLock = new();

    /// <summary>
    /// Records a log message for metrics tracking.
    /// </summary>
    public static void RecordLogMessage(Microsoft.Extensions.Logging.LogLevel level)
    {
        LogMessages.Add(1, new KeyValuePair<string, object?>("level", level.ToString()));
        
        lock (_logRateLock)
        {
            _logTimestamps.Enqueue(DateTime.UtcNow);
        }
    }

    /// <summary>
    /// Gets the current log message rate per minute.
    /// </summary>
    private static double GetCurrentLogRate()
    {
        lock (_logRateLock)
        {
            var now = DateTime.UtcNow;
            var cutoff = now.AddMinutes(-1);
            
            // Remove old timestamps
            while (_logTimestamps.TryPeek(out var oldest) && oldest < cutoff)
            {
                _logTimestamps.TryDequeue(out _);
            }
            
            return _logTimestamps.Count;
        }
    }
}