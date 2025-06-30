using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Granville.Rpc.Telemetry;

/// <summary>
/// Telemetry for RPC Client operations with conditional instrumentation for performance.
/// </summary>
public static class RpcClientTelemetry
{
    public static readonly ActivitySource ActivitySource = new("Granville.Rpc.Client", "1.0.0");
    public static readonly Meter Meter = new("Granville.Rpc.Client", "1.0.0");

    // Activity names
    public const string ConnectionActivity = "RpcClient.Connection";
    public const string HandshakeActivity = "RpcClient.Handshake";
    public const string RequestActivity = "RpcClient.Request";
    public const string AsyncEnumerableActivity = "RpcClient.AsyncEnumerable";
    public const string ReconnectionActivity = "RpcClient.Reconnection";

    // High-value, always-on metrics
    public static readonly Counter<long> ConnectionAttempts = Meter.CreateCounter<long>(
        "rpc_client_connection_attempts_total",
        description: "Total number of connection attempts");

    public static readonly Counter<long> ConnectionsEstablished = Meter.CreateCounter<long>(
        "rpc_client_connections_established_total",
        description: "Total number of successful connections");

    public static readonly Counter<long> ConnectionsFailed = Meter.CreateCounter<long>(
        "rpc_client_connections_failed_total",
        description: "Total number of failed connections");

    public static readonly Counter<long> ConnectionsLost = Meter.CreateCounter<long>(
        "rpc_client_connections_lost_total",
        description: "Total number of lost connections");

    public static readonly Counter<long> RequestsSent = Meter.CreateCounter<long>(
        "rpc_client_requests_sent_total",
        description: "Total number of RPC requests sent");

    public static readonly Counter<long> ResponsesReceived = Meter.CreateCounter<long>(
        "rpc_client_responses_received_total",
        description: "Total number of RPC responses received");

    public static readonly Counter<long> RequestTimeouts = Meter.CreateCounter<long>(
        "rpc_client_request_timeouts_total",
        description: "Total number of RPC request timeouts");

    public static readonly Counter<long> TransportErrors = Meter.CreateCounter<long>(
        "rpc_client_transport_errors_total",
        description: "Total number of transport-level errors");

    // Performance metrics
    public static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(
        "rpc_client_request_duration_ms",
        description: "Duration of RPC requests from send to response in milliseconds");

    public static readonly Histogram<double> ConnectionDuration = Meter.CreateHistogram<double>(
        "rpc_client_connection_duration_ms",
        description: "Duration of connection establishment in milliseconds");

    public static readonly Histogram<double> HandshakeDuration = Meter.CreateHistogram<double>(
        "rpc_client_handshake_duration_ms",
        description: "Duration of handshake process in milliseconds");

    public static readonly Histogram<int> MessageSize = Meter.CreateHistogram<int>(
        "rpc_client_message_size_bytes",
        description: "Size of RPC messages in bytes");

    // Current state gauges
    public static readonly UpDownCounter<long> ActiveConnections = Meter.CreateUpDownCounter<long>(
        "rpc_client_active_connections",
        description: "Current number of active server connections");

    public static readonly UpDownCounter<long> PendingRequests = Meter.CreateUpDownCounter<long>(
        "rpc_client_pending_requests", 
        description: "Current number of pending RPC requests");

    public static readonly ObservableGauge<int> ConnectedServers = Meter.CreateObservableGauge<int>(
        "rpc_client_connected_servers",
        description: "Number of servers currently connected",
        observeValue: () => 0); // Will be updated based on active connections

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
    public static void RecordRequest(string grainType, string methodName, double durationMs, bool success, bool timedOut = false, string? errorType = null)
    {
        // Always record basic metrics
        RequestsSent.Add(1,
            new KeyValuePair<string, object?>("grain.type", grainType),
            new KeyValuePair<string, object?>("method", methodName));

        if (success)
        {
            ResponsesReceived.Add(1,
                new KeyValuePair<string, object?>("grain.type", grainType),
                new KeyValuePair<string, object?>("method", methodName));
        }
        else if (timedOut)
        {
            RequestTimeouts.Add(1,
                new KeyValuePair<string, object?>("grain.type", grainType),
                new KeyValuePair<string, object?>("method", methodName));
        }

        RequestDuration.Record(durationMs,
            new KeyValuePair<string, object?>("grain.type", grainType),
            new KeyValuePair<string, object?>("method", methodName),
            new KeyValuePair<string, object?>("success", success.ToString()),
            new KeyValuePair<string, object?>("timed_out", timedOut.ToString()));
    }

    /// <summary>
    /// Records connection event metrics.
    /// </summary>
    public static void RecordConnection(string operation, bool success, double durationMs = 0, string? serverId = null, string? errorType = null)
    {
        switch (operation.ToLowerInvariant())
        {
            case "attempt":
                ConnectionAttempts.Add(1, new KeyValuePair<string, object?>("server.id", serverId ?? "unknown"));
                break;
                
            case "established":
                if (success)
                {
                    ConnectionsEstablished.Add(1, new KeyValuePair<string, object?>("server.id", serverId ?? "unknown"));
                    ActiveConnections.Add(1);
                    if (durationMs > 0)
                    {
                        ConnectionDuration.Record(durationMs, new KeyValuePair<string, object?>("server.id", serverId ?? "unknown"));
                    }
                }
                else
                {
                    ConnectionsFailed.Add(1,
                        new KeyValuePair<string, object?>("server.id", serverId ?? "unknown"),
                        new KeyValuePair<string, object?>("error.type", errorType ?? "unknown"));
                }
                break;
                
            case "lost":
                ConnectionsLost.Add(1,
                    new KeyValuePair<string, object?>("server.id", serverId ?? "unknown"),
                    new KeyValuePair<string, object?>("error.type", errorType ?? "network"));
                ActiveConnections.Add(-1);
                break;
        }
    }

    /// <summary>
    /// Records handshake metrics.
    /// </summary>
    public static void RecordHandshake(bool success, double durationMs, string? serverId = null, int? zoneId = null, string? errorType = null)
    {
        HandshakeDuration.Record(durationMs,
            new KeyValuePair<string, object?>("success", success.ToString()),
            new KeyValuePair<string, object?>("server.id", serverId ?? "unknown"));

        // Connected servers count will be available via the observable gauge
    }

    /// <summary>
    /// Records pending request changes for queue depth monitoring.
    /// </summary>
    public static void RecordPendingRequest(bool added)
    {
        PendingRequests.Add(added ? 1 : -1);
    }
}