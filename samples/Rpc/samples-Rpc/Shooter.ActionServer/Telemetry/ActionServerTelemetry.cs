using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Collections.Concurrent;

namespace Shooter.ActionServer.Telemetry;

public static class ActionServerTelemetry
{
    public static readonly ActivitySource ActivitySource = new("Shooter.ActionServer", "1.0.0");
    public static readonly Meter Meter = new("Shooter.ActionServer", "1.0.0");

    // Activity names
    public const string PlayerConnectionActivity = "ActionServer.PlayerConnection";
    public const string ZoneTransferActivity = "ActionServer.ZoneTransfer";
    public const string WorldUpdateActivity = "ActionServer.WorldUpdate";
    public const string RpcCallActivity = "ActionServer.RpcCall";
    public const string EntityTransferActivity = "ActionServer.EntityTransfer";

    // Metrics
    public static readonly Counter<long> PlayerConnections = Meter.CreateCounter<long>(
        "actionserver_player_connections_total",
        description: "Total number of player connections");

    public static readonly Counter<long> PlayerDisconnections = Meter.CreateCounter<long>(
        "actionserver_player_disconnections_total", 
        description: "Total number of player disconnections");

    public static readonly Counter<long> ZoneTransfers = Meter.CreateCounter<long>(
        "actionserver_zone_transfers_total",
        description: "Total number of zone transfers initiated");

    public static readonly Counter<long> EntityTransfers = Meter.CreateCounter<long>(
        "actionserver_entity_transfers_total",
        description: "Total number of entity transfers");

    public static readonly Counter<long> RpcCalls = Meter.CreateCounter<long>(
        "actionserver_rpc_calls_total",
        description: "Total number of RPC calls made");

    public static readonly Histogram<double> WorldUpdateDuration = Meter.CreateHistogram<double>(
        "actionserver_world_update_duration_ms",
        description: "Duration of world simulation updates in milliseconds");

    public static readonly Histogram<double> ZoneTransferDuration = Meter.CreateHistogram<double>(
        "actionserver_zone_transfer_duration_ms", 
        description: "Duration of zone transfers in milliseconds");

    public static readonly Histogram<double> RpcCallDuration = Meter.CreateHistogram<double>(
        "actionserver_rpc_call_duration_ms",
        description: "Duration of RPC calls in milliseconds");

    public static readonly UpDownCounter<long> ActivePlayers = Meter.CreateUpDownCounter<long>(
        "actionserver_active_players",
        description: "Current number of active players in the zone");

    public static readonly UpDownCounter<long> ActiveEntities = Meter.CreateUpDownCounter<long>(
        "actionserver_active_entities",
        description: "Current number of entities in the zone");

    public static readonly ObservableGauge<int> ZoneX = Meter.CreateObservableGauge<int>(
        "actionserver_zone_x",
        description: "X coordinate of assigned zone",
        observeValue: () => 0); // Will be updated when zone is assigned

    public static readonly ObservableGauge<int> ZoneY = Meter.CreateObservableGauge<int>(
        "actionserver_zone_y", 
        description: "Y coordinate of assigned zone",
        observeValue: () => 0); // Will be updated when zone is assigned

    // Log message metrics
    public static readonly Counter<long> LogMessages = Meter.CreateCounter<long>(
        "actionserver_log_messages_total",
        description: "Total number of log messages by level");
    
    public static readonly ObservableGauge<double> LogRate = Meter.CreateObservableGauge<double>(
        "actionserver_log_rate_per_minute",
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