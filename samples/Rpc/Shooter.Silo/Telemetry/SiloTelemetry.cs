using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Collections.Concurrent;

namespace Shooter.Silo.Telemetry;

public static class SiloTelemetry
{
    public static readonly ActivitySource ActivitySource = new("Shooter.Silo", "1.0.0");
    public static readonly Meter Meter = new("Shooter.Silo", "1.0.0");

    // Activity names
    public const string PlayerCreationActivity = "Silo.PlayerCreation";
    public const string WorldUpdateActivity = "Silo.WorldUpdate";
    public const string ActionServerRegistrationActivity = "Silo.ActionServerRegistration";
    public const string ZoneAssignmentActivity = "Silo.ZoneAssignment";

    // Metrics
    public static readonly Counter<long> PlayersCreated = Meter.CreateCounter<long>(
        "silo_players_created_total",
        description: "Total number of players created");

    public static readonly Counter<long> ActionServersRegistered = Meter.CreateCounter<long>(
        "silo_action_servers_registered_total",
        description: "Total number of action servers registered");

    public static readonly Counter<long> ZoneAssignments = Meter.CreateCounter<long>(
        "silo_zone_assignments_total",
        description: "Total number of zone assignments made");

    public static readonly Histogram<double> GrainCallDuration = Meter.CreateHistogram<double>(
        "silo_grain_call_duration_ms",
        description: "Duration of grain calls in milliseconds");

    public static readonly UpDownCounter<long> ActivePlayers = Meter.CreateUpDownCounter<long>(
        "silo_active_players",
        description: "Current number of active players");

    public static readonly UpDownCounter<long> ActiveActionServers = Meter.CreateUpDownCounter<long>(
        "silo_active_action_servers",
        description: "Current number of active action servers");

    public static readonly ObservableGauge<int> CoveredZones = Meter.CreateObservableGauge<int>(
        "silo_covered_zones",
        description: "Number of zones covered by action servers",
        observeValue: () => 0); // Will be updated based on active action servers

    // Log message metrics
    public static readonly Counter<long> LogMessages = Meter.CreateCounter<long>(
        "silo_log_messages_total",
        description: "Total number of log messages by level");
    
    public static readonly ObservableGauge<double> LogRate = Meter.CreateObservableGauge<double>(
        "silo_log_rate_per_minute",
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