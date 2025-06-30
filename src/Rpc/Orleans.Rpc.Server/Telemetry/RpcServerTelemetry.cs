using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Granville.Rpc.Telemetry;

/// <summary>
/// Telemetry for RPC Server operations with conditional instrumentation for performance.
/// </summary>
public static class RpcServerTelemetry
{
    public static readonly ActivitySource ActivitySource = new("Granville.Rpc.Server", "1.0.0");
    public static readonly Meter Meter = new("Granville.Rpc.Server", "1.0.0");

    // Activity names
    public const string ConnectionActivity = "RpcServer.Connection";
    public const string HandshakeActivity = "RpcServer.Handshake";
    public const string RequestActivity = "RpcServer.Request";
    public const string AsyncEnumerableActivity = "RpcServer.AsyncEnumerable";
    public const string TransportActivity = "RpcServer.Transport";

    // High-value, always-on metrics
    public static readonly Counter<long> ConnectionsEstablished = Meter.CreateCounter<long>(
        "rpc_server_connections_established_total",
        description: "Total number of client connections established");

    public static readonly Counter<long> ConnectionsClosed = Meter.CreateCounter<long>(
        "rpc_server_connections_closed_total",
        description: "Total number of client connections closed");

    public static readonly Counter<long> HandshakesCompleted = Meter.CreateCounter<long>(
        "rpc_server_handshakes_completed_total",
        description: "Total number of successful handshakes");

    public static readonly Counter<long> HandshakesFailed = Meter.CreateCounter<long>(
        "rpc_server_handshakes_failed_total",
        description: "Total number of failed handshakes");

    public static readonly Counter<long> RequestsReceived = Meter.CreateCounter<long>(
        "rpc_server_requests_received_total",
        description: "Total number of RPC requests received");

    public static readonly Counter<long> RequestsProcessed = Meter.CreateCounter<long>(
        "rpc_server_requests_processed_total",
        description: "Total number of RPC requests processed");

    public static readonly Counter<long> RequestsFailed = Meter.CreateCounter<long>(
        "rpc_server_requests_failed_total",
        description: "Total number of RPC requests that failed");

    public static readonly Counter<long> TransportErrors = Meter.CreateCounter<long>(
        "rpc_server_transport_errors_total",
        description: "Total number of transport-level errors");

    // Performance metrics
    public static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(
        "rpc_server_request_duration_ms",
        description: "Duration of RPC request processing in milliseconds");

    public static readonly Histogram<double> HandshakeDuration = Meter.CreateHistogram<double>(
        "rpc_server_handshake_duration_ms",
        description: "Duration of handshake processing in milliseconds");

    public static readonly Histogram<int> MessageSize = Meter.CreateHistogram<int>(
        "rpc_server_message_size_bytes",
        description: "Size of RPC messages in bytes");

    // Current state gauges
    public static readonly UpDownCounter<long> ActiveConnections = Meter.CreateUpDownCounter<long>(
        "rpc_server_active_connections",
        description: "Current number of active client connections");

    public static readonly UpDownCounter<long> PendingRequests = Meter.CreateUpDownCounter<long>(
        "rpc_server_pending_requests",
        description: "Current number of pending RPC requests");

    public static readonly ObservableGauge<int> ZoneId = Meter.CreateObservableGauge<int>(
        "rpc_server_zone_id",
        description: "Zone ID assigned to this RPC server",
        observeValue: () => 0); // Will be updated when zone is assigned

    /// <summary>
    /// Conditionally starts an activity if listeners are present.
    /// Returns null if no listeners to avoid overhead.
    /// </summary>
    public static Activity? StartActivityIfEnabled(string name)
    {
        return ActivitySource.HasListeners() ? ActivitySource.StartActivity(name) : null;
    }

    /// <summary>
    /// Records request metrics with conditional detailed tracing.
    /// </summary>
    public static void RecordRequest(string grainType, string methodName, double durationMs, bool success, string? errorType = null)
    {
        // Always record basic metrics
        RequestsProcessed.Add(1, 
            new KeyValuePair<string, object?>("grain.type", grainType),
            new KeyValuePair<string, object?>("method", methodName),
            new KeyValuePair<string, object?>("success", success.ToString()));

        if (!success)
        {
            RequestsFailed.Add(1,
                new KeyValuePair<string, object?>("grain.type", grainType), 
                new KeyValuePair<string, object?>("method", methodName),
                new KeyValuePair<string, object?>("error.type", errorType ?? "unknown"));
        }

        RequestDuration.Record(durationMs,
            new KeyValuePair<string, object?>("grain.type", grainType),
            new KeyValuePair<string, object?>("method", methodName),
            new KeyValuePair<string, object?>("success", success.ToString()));
    }

    /// <summary>
    /// Records connection event metrics.
    /// </summary>
    public static void RecordConnection(bool established, string? remoteEndpoint = null, string? errorType = null)
    {
        if (established)
        {
            ConnectionsEstablished.Add(1, new KeyValuePair<string, object?>("remote.endpoint", remoteEndpoint ?? "unknown"));
            ActiveConnections.Add(1);
        }
        else
        {
            ConnectionsClosed.Add(1, 
                new KeyValuePair<string, object?>("remote.endpoint", remoteEndpoint ?? "unknown"),
                new KeyValuePair<string, object?>("error.type", errorType ?? "normal"));
            ActiveConnections.Add(-1);
        }
    }

    /// <summary>
    /// Records handshake metrics.
    /// </summary>
    public static void RecordHandshake(bool success, double durationMs, string? clientId = null, string? errorType = null)
    {
        if (success)
        {
            HandshakesCompleted.Add(1, new KeyValuePair<string, object?>("client.id", clientId ?? "unknown"));
        }
        else
        {
            HandshakesFailed.Add(1, 
                new KeyValuePair<string, object?>("client.id", clientId ?? "unknown"),
                new KeyValuePair<string, object?>("error.type", errorType ?? "unknown"));
        }

        HandshakeDuration.Record(durationMs, new KeyValuePair<string, object?>("success", success.ToString()));
    }
}