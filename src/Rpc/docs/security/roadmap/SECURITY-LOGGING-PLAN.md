# Granville RPC Security Logging and Monitoring Plan

**Document Version**: 1.0
**Created**: 2025-11-30
**Status**: Planning
**Priority**: HIGH (Observability & Incident Response)

## Executive Summary

This document specifies comprehensive security event logging, audit trails, and monitoring for Granville RPC. Effective logging enables threat detection, incident investigation, compliance audits, and performance analysis.

### Current State

- **Challenge**: Security events scattered across multiple layers (transport, auth, handlers, grains)
- **Tracing Problem**: No correlation across RPC call chains
- **Data Loss Risk**: Valuable security data not persisted
- **Blind Spots**: No visibility into attack patterns or suspicious activity

### Target State

- Structured logging of all security-critical events
- Distributed request tracing (RequestId correlation)
- Audit trail with immutable event records
- Real-time monitoring and alerting
- Forensic analysis capability
- SIEM integration ready

---

## Table of Contents

1. [Security Event Categories](#1-security-event-categories)
2. [Logging Architecture](#2-logging-architecture)
3. [Structured Logging](#3-structured-logging)
4. [Distributed Tracing](#4-distributed-tracing)
5. [Audit Trail](#5-audit-trail)
6. [Monitoring & Alerting](#6-monitoring--alerting)
7. [Implementation](#7-implementation)
8. [Privacy & Compliance](#8-privacy--compliance)
9. [Testing Strategy](#9-testing-strategy)
10. [Rollout Plan](#10-rollout-plan)

---

## 1. Security Event Categories

### 1.1 Authentication Events

Logged when session creation, validation, or authentication changes:

```
✓ AUTHENTICATION_SUCCESS
  - Player registers and creates session
  - Fields: PlayerId, Name, IP, SessionKey (fingerprint only), Timestamp
  - Level: INFO

✗ AUTHENTICATION_FAILURE
  - Invalid credentials or session validation fails
  - Fields: PlayerId (attempted), Reason, IP, AttemptCount, Timestamp
  - Level: WARNING

⏰ SESSION_CREATED
  - Session initialized (HTTP POST /register)
  - Fields: PlayerId, ExpiresAt, Role, IP, Timestamp
  - Level: INFO

❌ SESSION_EXPIRED
  - Session expired (checked at RPC time)
  - Fields: PlayerId, Duration, LastActivityAt, Timestamp
  - Level: INFO

🔴 SESSION_REVOKED
  - Explicit logout or admin revocation
  - Fields: PlayerId, Reason, RevokedBy, Timestamp
  - Level: WARNING
```

### 1.2 Authorization Events

Logged when authorization checks pass or fail:

```
✓ AUTHORIZATION_ALLOWED
  - RPC handler invoked with proper permissions
  - Fields: HandlerName, PlayerId, Role, Resource, Timestamp
  - Level: DEBUG (verbose)

✗ AUTHORIZATION_DENIED
  - User lacks required role or permission
  - Fields: HandlerName, PlayerId, Role, Required, Reason, Timestamp
  - Level: WARNING

⚠️ AUTHORIZATION_ANOMALY
  - Suspicious authorization pattern detected
  - Fields: PlayerId, Pattern (e.g., "rapid admin calls"), Count, Timestamp
  - Level: WARNING
```

### 1.3 Deserialization Events

Logged when type validation or resource limits trigger:

```
✗ DESERIALIZATION_BLOCKED
  - Type not in whitelist or gadget chain detected
  - Fields: AttemptedType, PlayerId, Handler, Reason, Timestamp
  - Level: WARNING

⚠️ RESOURCE_LIMIT_EXCEEDED
  - Payload too large, depth too deep, etc.
  - Fields: LimitType, Attempted, Maximum, PlayerId, Timestamp
  - Level: WARNING
```

### 1.4 Rate Limiting Events

Logged when DDoS protections trigger:

```
🚫 RATE_LIMIT_EXCEEDED
  - Per-user or per-IP rate limit hit
  - Fields: PlayerId, Limit, Window, Requests, Timestamp
  - Level: WARNING

🚫 CONNECTION_LIMIT_EXCEEDED
  - Too many concurrent connections from client
  - Fields: PlayerId/IP, Limit, Current, Timestamp
  - Level: WARNING
```

### 1.5 Data Access Events

Logged for sensitive operations (for compliance/audit):

```
📋 DATA_ACCESS
  - Player accessed own or shared resource
  - Fields: PlayerId, ResourceId, Action, Timestamp
  - Level: DEBUG

🔐 DATA_MODIFICATION
  - Player modified resource
  - Fields: PlayerId, ResourceId, Changes, Timestamp
  - Level: INFO
```

### 1.6 Administrative Events

Logged for admin actions:

```
👮 ADMIN_ACTION
  - Admin performed privileged action (ban, revoke, etc.)
  - Fields: AdminId, Action, TargetPlayerId, Reason, Timestamp
  - Level: WARNING
```

---

## 2. Logging Architecture

### 2.1 Layered Logging Approach

```
┌─────────────────────────────────────────────────────────────────┐
│                     APPLICATION LOGGERS                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ├─ Transport Layer                                             │
│  │  └─ DTLS-PSK, UDP packet handling                            │
│  │     Fields: RemoteEndpoint, BytesReceived, HandshakeTime     │
│  │                                                               │
│  ├─ Serialization Layer                                         │
│  │  └─ Type whitelisting, deserialization                       │
│  │     Fields: TypeName, PlayerId, AllowedBy                    │
│  │                                                               │
│  ├─ Handler Layer                                               │
│  │  └─ RPC handler invocation and errors                        │
│  │     Fields: HandlerName, RequestId, Duration, Result         │
│  │                                                               │
│  ├─ Grain Layer                                                 │
│  │  └─ Orleans grain method calls                               │
│  │     Fields: GrainType, MethodName, PlayerId, Duration        │
│  │                                                               │
│  └─ Security Layer (This Plan)                                  │
│     └─ Auth, authz, anomalies                                   │
│        Fields: EventType, PlayerId, Reason, Severity            │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────────┐
│                     STRUCTURED LOGGING                          │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  Serilog with JSON output                                       │
│  ├─ Console sink (dev)                                          │
│  ├─ File sink (local retention)                                 │
│  └─ HTTP sink → Log aggregation service                         │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────────┐
│                   LOG AGGREGATION (ELK / Loki)                  │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  Elasticsearch or Grafana Loki                                  │
│  ├─ Index: granville-rpc-security-{date}                        │
│  ├─ Retention: 90 days (configurable)                           │
│  └─ Search/analysis capability                                  │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────────┐
│                MONITORING & ALERTING (Prometheus)               │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  Metrics scraped from logs                                      │
│  ├─ Counters: blocked_attempts, authz_denied, etc.              │
│  ├─ Histograms: auth_latency, validation_time                   │
│  └─ Alerts: rate limit spikes, patterns of attacks              │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### 2.2 Log Flow Example

```
HTTP Authentication Request:
  ↓
PlayerAuthController.RegisterPlayerAsync()
  ├─ Log: "Player registration: {PlayerId}"
  ├─ Generate SessionKey
  ├─ Call: IPlayerSessionGrain.CreateSession()
  │  └─ Grain logs: "Session created for {PlayerId}: expires {Time}"
  └─ Log: "AUTHENTICATION_SUCCESS PlayerId={PlayerId} IP={IP}"
         (structured fields: level=INFO, category=Auth, ip, timestamp)
  ↓
Response sent to client via HTTPS

--------

UDP Game Connection:
  ↓
RpcServerTransport.HandleIncomingPacketAsync()
  ├─ Parse DTLS ClientHello
  ├─ Extract PlayerId from PSK identity
  ├─ Call: IPlayerSessionGrain.ValidateSessionKey()
  │  ├─ Grain logs: "Session validation for {PlayerId}"
  │  └─ Log: "SessionKeyValid=true" (structured, not in message)
  ├─ Create: RpcSecurityContext (RequestId=guid)
  ├─ Log: "DTLS_HANDSHAKE_SUCCESS PlayerId={PlayerId} RequestId={RequestId}"
  └─ Invoke: RPC handler with context
  ↓
Handler execution with RequestId propagated:
  ├─ Log: "RPC_HANDLER_START Handler=MoveAsync RequestId={RequestId} PlayerId={PlayerId}"
  ├─ Authorization check
  │  └─ Log: "AUTHORIZATION_ALLOWED Handler=MoveAsync PlayerId={PlayerId} RequestId={RequestId}"
  ├─ Grain method execution
  │  ├─ Log: "GRAIN_METHOD_CALL Grain=PlayerGrain Method=MoveAsync"
  │  └─ Access player state
  │     └─ Log: "DATA_ACCESS PlayerId={PlayerId} Resource=PlayerState"
  └─ Log: "RPC_HANDLER_SUCCESS Handler=MoveAsync Duration=15ms RequestId={RequestId}"
  ↓
Logs aggregated with RequestId for full trace:
  [RequestId: req-abc123]
  - DTLS_HANDSHAKE_SUCCESS
  - RPC_HANDLER_START
  - AUTHORIZATION_ALLOWED
  - GRAIN_METHOD_CALL
  - DATA_ACCESS
  - RPC_HANDLER_SUCCESS
```

---

## 3. Structured Logging

### 3.1 Structured Event Format

All security events follow this format:

```csharp
/// <summary>
/// Structured security event logged to central system.
/// </summary>
[GenerateSerializer]
public record SecurityEvent
{
    /// <summary>
    /// Event classification (e.g., "AUTHORIZATION_DENIED").
    /// </summary>
    [Id(0)] public required string EventType { get; init; }

    /// <summary>
    /// Severity level (Trace, Debug, Information, Warning, Error, Fatal).
    /// </summary>
    [Id(1)] public required string Severity { get; init; }

    /// <summary>
    /// Unique request identifier (from RpcSecurityContext).
    /// Enables correlation across services.
    /// </summary>
    [Id(2)] public required string RequestId { get; init; }

    /// <summary>
    /// Player identifier (if authenticated).
    /// </summary>
    [Id(3)] public string? PlayerId { get; init; }

    /// <summary>
    /// Remote IP address.
    /// </summary>
    [Id(4)] public string? RemoteIp { get; init; }

    /// <summary>
    /// Handler or method name.
    /// </summary>
    [Id(5)] public string? Handler { get; init; }

    /// <summary>
    /// Detailed reason for event.
    /// </summary>
    [Id(6)] public string? Reason { get; init; }

    /// <summary>
    /// Elapsed time for this event (if applicable).
    /// </summary>
    [Id(7)] public long? ElapsedMs { get; init; }

    /// <summary>
    /// Additional structured data (key-value pairs).
    /// </summary>
    [Id(8)] public Dictionary<string, object>? Data { get; init; }

    /// <summary>
    /// When event occurred (UTC).
    /// </summary>
    [Id(9)] public DateTime Timestamp { get; init; }
}
```

### 3.2 Serilog Configuration

**File**: `appsettings.json` (or Serilog config)

```json
{
  "Serilog": {
    "MinimumLevel": "Information",
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "theme": "Ansi",
          "outputTemplate": "[{Timestamp:HH:mm:ss.fff} {Level:u3}] [{RequestId}] {Message:lj}{NewLine}{Exception}"
        }
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/security-events-.txt",
          "rollingInterval": "Day",
          "rollOnFileSizeLimit": true,
          "fileSizeLimitBytes": 1073741824,
          "retainedFileCountLimit": 90,
          "outputTemplate": "{Timestamp:o} [{Level:u3}] [{RequestId}] {Message} {Properties:j}{NewLine}{Exception}"
        }
      },
      {
        "Name": "Seq",
        "Args": {
          "serverUrl": "http://seq-server:5341",
          "apiKey": "YOUR_API_KEY"
        }
      }
    ],
    "Enrich": [
      "FromLogContext",
      "WithMachineName",
      "WithThreadId"
    ],
    "Properties": {
      "Application": "Granville.RPC",
      "Environment": "{ASPNETCORE_ENVIRONMENT}"
    }
  }
}
```

### 3.3 Security Event Logger

**File**: `/src/Rpc/Orleans.Rpc.Security/Logging/RpcSecurityEventLogger.cs` (NEW)

```csharp
using Microsoft.Extensions.Logging;
using System;
using Granville.Rpc.Security.Context;

namespace Granville.Rpc.Security.Logging;

/// <summary>
/// Centralized security event logging.
/// </summary>
public interface IRpcSecurityEventLogger
{
    // Authentication events
    void LogAuthenticationSuccess(string playerId, string? ip);
    void LogAuthenticationFailure(string attemptedPlayerId, string reason, string? ip, int? attemptCount = null);
    void LogSessionCreated(string playerId, DateTime expiresAt, UserRole role);
    void LogSessionExpired(string playerId, TimeSpan duration);
    void LogSessionRevoked(string playerId, string reason);

    // Authorization events
    void LogAuthorizationAllowed(string handlerName, string playerId, UserRole role);
    void LogAuthorizationDenied(string handlerName, string playerId, UserRole role, string requiredRole, string reason);
    void LogAuthorizationAnomaly(string playerId, string pattern, int count);

    // Deserialization events
    void LogDeserializationBlocked(string attemptedType, string playerId, string handler, string reason);
    void LogResourceLimitExceeded(string limitType, long attempted, long maximum, string playerId);

    // Rate limiting events
    void LogRateLimitExceeded(string playerId, int limit, int window, int requests);
    void LogConnectionLimitExceeded(string playerId, int limit, int current);

    // Data access events
    void LogDataAccess(string playerId, string resourceId, string action);
    void LogDataModification(string playerId, string resourceId, string changes);

    // Administrative events
    void LogAdminAction(string adminId, string action, string targetPlayerId, string reason);
}

/// <summary>
/// Implementation using ILogger.
/// </summary>
public class RpcSecurityEventLogger : IRpcSecurityEventLogger
{
    private readonly ILogger _logger;

    public RpcSecurityEventLogger(ILogger<RpcSecurityEventLogger> logger)
    {
        _logger = logger;
    }

    public void LogAuthenticationSuccess(string playerId, string? ip)
    {
        _logger.LogInformation(
            "[SECURITY] AUTHENTICATION_SUCCESS PlayerId={PlayerId} IP={IP} RequestId={RequestId}",
            playerId, ip ?? "unknown", RpcSecurityContext.Current?.RequestId ?? "N/A");
    }

    public void LogAuthenticationFailure(
        string attemptedPlayerId,
        string reason,
        string? ip,
        int? attemptCount = null)
    {
        _logger.LogWarning(
            "[SECURITY] AUTHENTICATION_FAILURE PlayerId={PlayerId} Reason={Reason} " +
            "IP={IP} Attempts={Attempts} RequestId={RequestId}",
            attemptedPlayerId, reason, ip ?? "unknown", attemptCount ?? 1,
            RpcSecurityContext.Current?.RequestId ?? "N/A");
    }

    public void LogSessionCreated(string playerId, DateTime expiresAt, UserRole role)
    {
        _logger.LogInformation(
            "[SECURITY] SESSION_CREATED PlayerId={PlayerId} ExpiresAt={ExpiresAt} " +
            "Role={Role} RequestId={RequestId}",
            playerId, expiresAt, role,
            RpcSecurityContext.Current?.RequestId ?? "N/A");
    }

    public void LogSessionExpired(string playerId, TimeSpan duration)
    {
        _logger.LogInformation(
            "[SECURITY] SESSION_EXPIRED PlayerId={PlayerId} Duration={Hours}h RequestId={RequestId}",
            playerId, duration.TotalHours,
            RpcSecurityContext.Current?.RequestId ?? "N/A");
    }

    public void LogSessionRevoked(string playerId, string reason)
    {
        _logger.LogWarning(
            "[SECURITY] SESSION_REVOKED PlayerId={PlayerId} Reason={Reason} RequestId={RequestId}",
            playerId, reason,
            RpcSecurityContext.Current?.RequestId ?? "N/A");
    }

    public void LogAuthorizationAllowed(string handlerName, string playerId, UserRole role)
    {
        _logger.LogDebug(
            "[SECURITY] AUTHORIZATION_ALLOWED Handler={Handler} PlayerId={PlayerId} " +
            "Role={Role} RequestId={RequestId}",
            handlerName, playerId, role,
            RpcSecurityContext.Current?.RequestId ?? "N/A");
    }

    public void LogAuthorizationDenied(
        string handlerName,
        string playerId,
        UserRole role,
        string requiredRole,
        string reason)
    {
        _logger.LogWarning(
            "[SECURITY] AUTHORIZATION_DENIED Handler={Handler} PlayerId={PlayerId} " +
            "Role={CurrentRole} Required={RequiredRole} Reason={Reason} RequestId={RequestId}",
            handlerName, playerId, role, requiredRole, reason,
            RpcSecurityContext.Current?.RequestId ?? "N/A");
    }

    public void LogAuthorizationAnomaly(string playerId, string pattern, int count)
    {
        _logger.LogWarning(
            "[SECURITY] AUTHORIZATION_ANOMALY PlayerId={PlayerId} Pattern={Pattern} " +
            "Count={Count} RequestId={RequestId}",
            playerId, pattern, count,
            RpcSecurityContext.Current?.RequestId ?? "N/A");
    }

    public void LogDeserializationBlocked(string attemptedType, string playerId, string handler, string reason)
    {
        _logger.LogWarning(
            "[SECURITY] DESERIALIZATION_BLOCKED Type={Type} PlayerId={PlayerId} " +
            "Handler={Handler} Reason={Reason} RequestId={RequestId}",
            attemptedType, playerId, handler, reason,
            RpcSecurityContext.Current?.RequestId ?? "N/A");
    }

    public void LogResourceLimitExceeded(string limitType, long attempted, long maximum, string playerId)
    {
        _logger.LogWarning(
            "[SECURITY] RESOURCE_LIMIT_EXCEEDED LimitType={LimitType} " +
            "Attempted={Attempted} Maximum={Maximum} PlayerId={PlayerId} RequestId={RequestId}",
            limitType, attempted, maximum, playerId,
            RpcSecurityContext.Current?.RequestId ?? "N/A");
    }

    public void LogRateLimitExceeded(string playerId, int limit, int window, int requests)
    {
        _logger.LogWarning(
            "[SECURITY] RATE_LIMIT_EXCEEDED PlayerId={PlayerId} Limit={Limit}/sec " +
            "Requests={Requests} Window={WindowSec}s RequestId={RequestId}",
            playerId, limit, requests, window,
            RpcSecurityContext.Current?.RequestId ?? "N/A");
    }

    public void LogConnectionLimitExceeded(string playerId, int limit, int current)
    {
        _logger.LogWarning(
            "[SECURITY] CONNECTION_LIMIT_EXCEEDED PlayerId={PlayerId} Limit={Limit} " +
            "Current={Current} RequestId={RequestId}",
            playerId, limit, current,
            RpcSecurityContext.Current?.RequestId ?? "N/A");
    }

    public void LogDataAccess(string playerId, string resourceId, string action)
    {
        _logger.LogDebug(
            "[SECURITY] DATA_ACCESS PlayerId={PlayerId} ResourceId={ResourceId} " +
            "Action={Action} RequestId={RequestId}",
            playerId, resourceId, action,
            RpcSecurityContext.Current?.RequestId ?? "N/A");
    }

    public void LogDataModification(string playerId, string resourceId, string changes)
    {
        _logger.LogInformation(
            "[SECURITY] DATA_MODIFICATION PlayerId={PlayerId} ResourceId={ResourceId} " +
            "Changes={Changes} RequestId={RequestId}",
            playerId, resourceId, changes,
            RpcSecurityContext.Current?.RequestId ?? "N/A");
    }

    public void LogAdminAction(string adminId, string action, string targetPlayerId, string reason)
    {
        _logger.LogWarning(
            "[SECURITY] ADMIN_ACTION AdminId={AdminId} Action={Action} " +
            "TargetPlayerId={TargetPlayerId} Reason={Reason} RequestId={RequestId}",
            adminId, action, targetPlayerId, reason,
            RpcSecurityContext.Current?.RequestId ?? "N/A");
    }
}
```

---

## 4. Distributed Tracing

### 4.1 RequestId Propagation

RequestId flows through entire call chain:

```
HTTP Request
  ├─ Set: RequestId = Guid.NewGuid()
  ├─ Log: "Request started RequestId={RequestId}"
  └─ Store in: RpcSecurityContext.RequestId

RPC Call
  ├─ Use: context.RequestId from AsyncLocal<T>
  ├─ Log: "Handler invoked RequestId={RequestId}"
  └─ Pass to: Grain calls (in RpcSecurityContext)

Grain Method
  ├─ Access: RpcSecurityContext.Current.RequestId
  ├─ Log: "Grain method RequestId={RequestId}"
  └─ Pass to: Nested grain calls

Response
  ├─ Log: "Response ready RequestId={RequestId}"
  └─ Return: { RequestId, Result }
```

### 4.2 Tracing Query

To trace a single request:

```sql
-- Elasticsearch query
GET granville-rpc-security-*/_search
{
  "query": {
    "match": { "RequestId": "req-abc123" }
  },
  "sort": [
    { "Timestamp": "asc" }
  ]
}
```

Results show all events for that request, in order:
```
[RequestId: req-abc123]
1. 10:15:32.100 - DTLS_HANDSHAKE_SUCCESS
2. 10:15:32.102 - RPC_HANDLER_START
3. 10:15:32.103 - AUTHORIZATION_ALLOWED
4. 10:15:32.105 - GRAIN_METHOD_CALL
5. 10:15:32.110 - RPC_HANDLER_SUCCESS
```

---

## 5. Audit Trail

### 5.1 Immutable Event Storage

For compliance, sensitive events should be immutable:

```csharp
public class AuditTrailStore
{
    /// <summary>
    /// Append-only event store (e.g., using Event Sourcing).
    /// Events are written once and never deleted.
    /// </summary>
    private readonly IEventStore _eventStore;

    public async Task RecordAuditEventAsync(
        string eventType,
        string playerId,
        string action,
        string details)
    {
        var auditEvent = new AuditEvent
        {
            EventId = Guid.NewGuid(),
            EventType = eventType,
            PlayerId = playerId,
            Action = action,
            Details = details,
            Timestamp = DateTime.UtcNow,
            RequestId = RpcSecurityContext.Current?.RequestId
        };

        // Append to immutable store
        await _eventStore.AppendAsync(auditEvent);

        // Also log for real-time alerting
        _logger.LogWarning("[AUDIT] {@AuditEvent}", auditEvent);
    }
}

[GenerateSerializer]
public record AuditEvent
{
    [Id(0)] public required Guid EventId { get; init; }
    [Id(1)] public required string EventType { get; init; }
    [Id(2)] public required string PlayerId { get; init; }
    [Id(3)] public required string Action { get; init; }
    [Id(4)] public string Details { get; init; } = string.Empty;
    [Id(5)] public DateTime Timestamp { get; init; }
    [Id(6)] public string? RequestId { get; init; }
}
```

---

## 6. Monitoring & Alerting

### 6.1 Prometheus Metrics from Logs

Extract metrics from security logs:

```csharp
public class RpcSecurityMetrics
{
    private readonly Counter _authSuccessCount;
    private readonly Counter _authFailureCount;
    private readonly Counter _authzDeniedCount;
    private readonly Counter _blockDeserializationCount;
    private readonly Counter _rateLimitExceededCount;
    private readonly Histogram _authLatency;

    public RpcSecurityMetrics(ICollectorRegistry registry)
    {
        _authSuccessCount = Metrics.CreateCounter(
            "granville_rpc_authentication_success_total",
            "Total successful authentications",
            new CounterConfiguration { Registry = registry });

        _authFailureCount = Metrics.CreateCounter(
            "granville_rpc_authentication_failure_total",
            "Total failed authentication attempts",
            new CounterConfiguration
            {
                LabelNames = new[] { "reason" },
                Registry = registry
            });

        _authzDeniedCount = Metrics.CreateCounter(
            "granville_rpc_authorization_denied_total",
            "Total denied authorization checks",
            new CounterConfiguration
            {
                LabelNames = new[] { "handler", "reason" },
                Registry = registry
            });

        _blockDeserializationCount = Metrics.CreateCounter(
            "granville_rpc_deserialization_blocked_total",
            "Total blocked deserialization attempts",
            new CounterConfiguration
            {
                LabelNames = new[] { "type", "reason" },
                Registry = registry
            });

        _rateLimitExceededCount = Metrics.CreateCounter(
            "granville_rpc_rate_limit_exceeded_total",
            "Total rate limit exceeded events",
            new CounterConfiguration { Registry = registry });

        _authLatency = Metrics.CreateHistogram(
            "granville_rpc_authentication_latency_ms",
            "Authentication latency in milliseconds",
            new HistogramConfiguration
            {
                Buckets = new[] { 1.0, 5.0, 10.0, 50.0, 100.0, 500.0 },
                Registry = registry
            });
    }

    public void RecordAuthenticationSuccess() => _authSuccessCount.Inc();
    public void RecordAuthenticationFailure(string reason) => _authFailureCount.Labels(reason).Inc();
    public void RecordAuthorizationDenied(string handler, string reason) => _authzDeniedCount.Labels(handler, reason).Inc();
    public void RecordDeserializationBlocked(string type, string reason) => _blockDeserializationCount.Labels(type, reason).Inc();
    public void RecordRateLimitExceeded() => _rateLimitExceededCount.Inc();
    public void RecordAuthLatency(double latencyMs) => _authLatency.Observe(latencyMs);
}
```

### 6.2 Alert Rules

**File**: `prometheus-alerts.yml` (Prometheus AlertManager)

```yaml
groups:
  - name: granville_rpc_security
    interval: 30s
    rules:
      # Alert on authentication failure spike
      - alert: AuthenticationFailureSpike
        expr: rate(granville_rpc_authentication_failure_total[5m]) > 0.5
        for: 2m
        annotations:
          summary: "Authentication failures spiking"
          description: "{{ $value }} failures/sec detected"

      # Alert on authorization denial pattern
      - alert: AuthorizationDenialPattern
        expr: rate(granville_rpc_authorization_denied_total[5m]) > 0.2
        for: 5m
        annotations:
          summary: "Unusual authorization denial pattern"
          description: "{{ $value }} denials/sec from same user"

      # Alert on deserialization attacks
      - alert: DeserializationAttack
        expr: increase(granville_rpc_deserialization_blocked_total[1m]) > 10
        for: 1m
        annotations:
          summary: "Potential deserialization attack detected"
          description: "{{ $value }} blocked attempts in 1 minute"

      # Alert on sustained rate limiting
      - alert: RateLimitingDetected
        expr: rate(granville_rpc_rate_limit_exceeded_total[5m]) > 0.1
        for: 10m
        annotations:
          summary: "Sustained rate limiting activity"
          description: "Rate limits triggered {{ $value }} times/sec"
```

---

## 7. Implementation

### 7.1 Integration with RPC Handler

**File**: `/src/Rpc/Orleans.Rpc.Server/RpcServerHandler.cs` (MODIFY)

```csharp
public class RpcServerHandler
{
    private readonly IRpcSecurityEventLogger _securityLogger;

    public async Task<RpcResponse> HandleMessageAsync(
        RpcRequest request,
        IPEndPoint remoteEndpoint,
        CancellationToken ct)
    {
        // 1. Create context with RequestId
        var context = new RpcSecurityContext
        {
            PlayerId = playerId,
            RequestId = request.RequestId,
            RemoteEndpoint = remoteEndpoint.ToString(),
            // ...
        };

        using (RpcSecurityContext.SetCurrent(context))
        {
            try
            {
                // Log handler start
                _securityLogger.LogRpcHandlerStart(request.HandlerName);

                // Execute handler
                var result = await InvokeHandlerAsync(request, ct);

                // Log success
                _securityLogger.LogRpcHandlerSuccess(request.HandlerName, context.Elapsed);

                return result;
            }
            catch (RpcAuthorizationException authEx)
            {
                // Log authorization failure
                _securityLogger.LogAuthorizationDenied(
                    request.HandlerName,
                    context.PlayerId,
                    context.Role,
                    authEx.RequiredRole,
                    authEx.Message);

                throw;
            }
        }
    }
}
```

### 7.2 Integration with Authorization Filter

**File**: `/src/Rpc/Orleans.Rpc.Security/Filters/RpcAuthorizationFilter.cs` (NEW)

```csharp
public class RpcAuthorizationFilter : IRpcInvokeFilter
{
    private readonly IRpcSecurityEventLogger _securityLogger;

    public async Task OnInvokeAsync(
        IInvokeContext context,
        Func<IInvokeContext, Task> next)
    {
        var securityContext = RpcSecurityContext.Current;

        if (securityContext == null)
        {
            _securityLogger.LogAuthenticationRequired(context.Method.Name);
            throw new RpcAuthenticationRequiredException();
        }

        var auth = GetAuthorizationAttribute(context.Method);

        if (!auth.IsAuthorized(securityContext))
        {
            _securityLogger.LogAuthorizationDenied(
                context.Method.Name,
                securityContext.PlayerId,
                securityContext.Role,
                auth.RequiredRole.ToString(),
                $"User has {securityContext.Role}, requires {auth.RequiredRole}");

            throw new RpcAuthorizationException(auth.RequiredRole);
        }

        // Log allowed (debug level)
        _securityLogger.LogAuthorizationAllowed(
            context.Method.Name,
            securityContext.PlayerId,
            securityContext.Role);

        await next(context);
    }
}
```

---

## 8. Privacy & Compliance

### 8.1 Sensitive Data Protection

**NEVER LOG**:
- Session keys (use fingerprint/hash instead)
- Authentication tokens
- Passwords or credentials
- Personally identifiable information (PII)
- Credit card or payment data

**SAFE TO LOG**:
- PlayerId (application-assigned, not PII)
- IP address (necessary for security)
- Handler/method names (public API)
- EventType and severity (categorization)
- RequestId (for tracing)
- Timestamps (for analysis)

### 8.2 SessionKey Fingerprinting

```csharp
public static string GetSessionKeyFingerprint(byte[] sessionKey)
{
    // Hash the key so we can identify it without exposing it
    using var sha = System.Security.Cryptography.SHA256.Create();
    var hash = sha.ComputeHash(sessionKey);

    // Return first 8 bytes as hex (e.g., "a1b2c3d4e5f6g7h8")
    return Convert.ToHexString(hash.AsSpan(0, 8));
}

// In logs:
_logger.LogInformation(
    "Session validated: KeyFingerprint={KeyFp}",
    GetSessionKeyFingerprint(session.SessionKey));
    // Output: "KeyFingerprint=a1b2c3d4..."
```

### 8.3 PII Handling

If playing with real user data (future):

```csharp
public static class PiiMasking
{
    /// <summary>
    /// Mask email for logging (only show domain).
    /// </summary>
    public static string MaskEmail(string email)
    {
        var parts = email.Split('@');
        return parts.Length == 2
            ? $"***@{parts[1]}"
            : "***@***";
    }

    /// <summary>
    /// Mask IP address for privacy.
    /// </summary>
    public static string MaskIpAddress(string ip)
    {
        var parts = ip.Split('.');
        return parts.Length == 4
            ? $"{parts[0]}.{parts[1]}.***.*"
            : ip;
    }

    /// <summary>
    /// Mask phone number.
    /// </summary>
    public static string MaskPhoneNumber(string phone)
    {
        return phone.Length >= 4
            ? $"***{phone.Substring(phone.Length - 4)}"
            : "***";
    }
}
```

---

## 9. Testing Strategy

### 9.1 Unit Tests

**File**: `/granville/test/Rpc.Security.Tests/SecurityLoggingTests.cs` (NEW)

```csharp
public class SecurityEventLoggerTests
{
    private readonly Mock<ILogger> _mockLogger;
    private readonly RpcSecurityEventLogger _eventLogger;

    [Fact]
    public void LogAuthenticationSuccess_WritesStructuredLog()
    {
        // Arrange
        var playerId = "player1";
        var ip = "192.168.1.100";

        // Act
        _eventLogger.LogAuthenticationSuccess(playerId, ip);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v.ToString().Contains("AUTHENTICATION_SUCCESS") &&
                    v.ToString().Contains(playerId)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void LogAuthorizationDenied_IncludesRoleInfo()
    {
        // Arrange
        var context = new RpcSecurityContext
        {
            PlayerId = "player1",
            Role = UserRole.Guest,
            RequestId = "req-1"
        };

        using (RpcSecurityContext.SetCurrent(context))
        {
            // Act
            _eventLogger.LogAuthorizationDenied(
                "AdminCommand",
                context.PlayerId,
                context.Role,
                UserRole.Admin.ToString(),
                "User is not admin");

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) =>
                        v.ToString().Contains("AUTHORIZATION_DENIED") &&
                        v.ToString().Contains("Guest") &&
                        v.ToString().Contains("Admin")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
    }
}
```

### 9.2 Integration Tests

Test that logs are properly aggregated:

```csharp
public class SecurityLoggingIntegrationTests : RpcTestBase
{
    [Fact]
    public async Task RequestIdCorrelatesLogsAcrossChainAsync()
    {
        // Arrange
        var requestId = "req-test-123";
        var playerId = "player1";

        // Act: Make RPC call
        var context = new RpcSecurityContext
        {
            PlayerId = playerId,
            RequestId = requestId,
            Role = UserRole.Guest
        };

        using (RpcSecurityContext.SetCurrent(context))
        {
            var grain = GrainFactory.GetGrain<IPlayerGrain>(playerId);
            await grain.MoveAsync(100, 200, CancellationToken.None);
        }

        // Assert: Query logs by RequestId
        var logs = await _logAggregator.QueryByRequestIdAsync(requestId);

        Assert.NotEmpty(logs);
        Assert.All(logs, log => Assert.Equal(requestId, log.RequestId));
        Assert.Contains(logs, l => l.EventType == "RPC_HANDLER_START");
        Assert.Contains(logs, l => l.EventType == "GRAIN_METHOD_CALL");
        Assert.Contains(logs, l => l.EventType == "RPC_HANDLER_SUCCESS");
    }
}
```

---

## 10. Rollout Plan

### Phase 1: Core Logging Infrastructure (Week 1)
- [ ] Implement IRpcSecurityEventLogger
- [ ] Configure Serilog with JSON output
- [ ] Set up file and console sinks
- [ ] Test structured logging output

### Phase 2: RequestId Propagation (Week 2)
- [ ] Add RequestId to RpcSecurityContext
- [ ] Wire RequestId through handler → grain → nested calls
- [ ] Implement distributed tracing query
- [ ] Test request correlation

### Phase 3: Event Logging (Week 2-3)
- [ ] Log authentication events
- [ ] Log authorization events
- [ ] Log deserialization blocks
- [ ] Log rate limiting triggers
- [ ] Integration tests

### Phase 4: Metrics & Alerting (Week 3-4)
- [ ] Set up Prometheus metrics
- [ ] Configure alert rules in AlertManager
- [ ] Test alert firing
- [ ] Create Grafana dashboard

### Phase 5: Log Aggregation (Week 4)
- [ ] Deploy log aggregation service (ELK or Loki)
- [ ] Configure log shipping from ActionServers
- [ ] Implement audit trail store
- [ ] Test log searchability

### Phase 6: Production Deployment (Week 5)
- [ ] Deploy to staging
- [ ] Verify log generation and aggregation
- [ ] Test alerting
- [ ] Deploy to production

---

## Summary

**Security Logging** provides:
- ✅ Structured events for all security-critical operations
- ✅ Distributed request tracing via RequestId
- ✅ Immutable audit trails for compliance
- ✅ Real-time alerting on suspicious patterns
- ✅ Forensic analysis capability
- ✅ SIEM integration ready

**Dependencies**: Requires RPC-CONTEXT-FLOW-PLAN (RequestId/context) and integration with authorization/DDoS layers
