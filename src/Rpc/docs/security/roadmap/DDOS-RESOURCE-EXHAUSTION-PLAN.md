# DDoS and Resource Exhaustion Mitigation Plan

**Last Updated**: 2025-11-30
**Version**: 1.0
**Status**: Planning
**Related Phases**: 8 (Per-IP Rate Limiting), 9 (Per-User Rate Limiting)

## Executive Summary

This document provides a detailed, defense-in-depth plan for mitigating Distributed Denial of Service (DDoS) attacks and resource exhaustion vulnerabilities in Granville RPC. These threats are rated **CRITICAL** (DDoS) and **MEDIUM** (Resource Exhaustion) in the threat model.

**Current State**: Zero protection. Any client can send unlimited requests, consume unlimited memory, and exhaust server resources.

**Goal**: Multi-layered protection that gracefully handles malicious traffic while maintaining performance for legitimate users.

---

## Threat Analysis

### DDoS Attack Vectors

| Vector | Current Vulnerability | Risk |
|--------|----------------------|------|
| **Connection Flooding** | `OnConnectionRequest` accepts all "RpcConnection" keys | CRITICAL |
| **Packet Flooding** | No rate limiting in `OnDataReceived` | CRITICAL |
| **Amplification Attacks** | Handshake response includes large manifest (~KB) | HIGH |
| **Slowloris-style** | No connection timeout enforcement | HIGH |
| **Deserialization Bombs** | No message size limits enforced | CRITICAL |

### Resource Exhaustion Vectors

| Vector | Current Vulnerability | Risk |
|--------|----------------------|------|
| **Memory Exhaustion** | Large messages accepted without limits | HIGH |
| **Connection Table Exhaustion** | `_connections` dictionary grows unbounded | HIGH |
| **Thread Pool Starvation** | `async void OnDataReceived` can queue unlimited work | HIGH |
| **CPU Exhaustion** | Complex deserialization without timeout | MEDIUM |
| **Grain Activation Storm** | Unlimited grain creation per client | HIGH |

### Current Code Vulnerabilities

```csharp
// LiteNetLibTransport.cs:323-337 - No IP-based filtering
public void OnConnectionRequest(ConnectionRequest request)
{
    if (_isServer)
    {
        // VULNERABILITY: Accepts any connection with correct key
        if (string.IsNullOrEmpty(connectionKey) || connectionKey == "RpcConnection")
        {
            request.Accept();  // No rate limiting, no IP blocking
        }
    }
}

// RpcServer.cs:139-199 - No message rate limiting
private async void OnDataReceived(object sender, RpcDataReceivedEventArgs e)
{
    // VULNERABILITY: Processes any message without:
    // - Rate limiting
    // - Message size limits
    // - Per-IP quotas
    var message = messageSerializer.DeserializeMessage(e.Data);  // Can be expensive
    // ...
}

// RpcServerOptions.cs:23 - MaxConnections not enforced
public int MaxConnections { get; set; } = 1000;  // Currently unused!
```

---

## Defense Architecture

```
┌────────────────────────────────────────────────────────────────────────────┐
│                        EXTERNAL PROTECTION (Recommended)                   │
│  ┌─────────────┐  ┌──────────────┐  ┌────────────────┐                    │
│  │ CDN/DDoS    │  │ Firewall/WAF │  │ Geographic     │                    │
│  │ Protection  │  │ (iptables)   │  │ IP Filtering   │                    │
│  └─────────────┘  └──────────────┘  └────────────────┘                    │
└────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────────┐
│                   LAYER 1: TRANSPORT PROTECTION                            │
│                   (LiteNetLibTransport / IRpcTransport)                    │
│                                                                            │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐            │
│  │ Connection      │  │ IP-Based        │  │ Connection      │            │
│  │ Rate Limiter    │  │ Blocklist       │  │ Limit Enforcer  │            │
│  │                 │  │                 │  │                 │            │
│  │ • Max 10 conn/  │  │ • Permanent     │  │ • MaxConnections│            │
│  │   sec per IP    │  │   blocklist     │  │   enforcement   │            │
│  │ • Backoff on    │  │ • Temp blocks   │  │ • LRU eviction  │            │
│  │   violation     │  │   (15 min)      │  │   under pressure│            │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘            │
│                                                                            │
│  Entry Point: OnConnectionRequest()                                        │
└────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────────┐
│                   LAYER 2: PACKET PROTECTION                               │
│                   (RpcServer.OnDataReceived)                               │
│                                                                            │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐            │
│  │ Message Size    │  │ Per-Connection  │  │ Deserialization │            │
│  │ Validator       │  │ Rate Limiter    │  │ Timeout         │            │
│  │                 │  │                 │  │                 │            │
│  │ • Max 64KB      │  │ • 1000 msg/min  │  │ • 100ms timeout │            │
│  │   per message   │  │ • Burst: 50     │  │ • Cancel on     │            │
│  │ • Drop oversized│  │ • Sliding window│  │   timeout       │            │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘            │
│                                                                            │
│  Entry Point: OnDataReceived()                                             │
└────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────────┐
│                   LAYER 3: REQUEST PROTECTION                              │
│                   (RpcServer.HandleRequest)                                │
│                                                                            │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐            │
│  │ Per-User        │  │ Method-Based    │  │ Request Queue   │            │
│  │ Rate Limiter    │  │ Rate Limiter    │  │ Management      │            │
│  │                 │  │                 │  │                 │            │
│  │ • By PlayerId   │  │ • Per endpoint  │  │ • Max pending:  │            │
│  │ • Role-based    │  │   limits        │  │   100 per conn  │            │
│  │   tiers         │  │ • Expensive ops │  │ • Backpressure  │            │
│  │                 │  │   throttled     │  │                 │            │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘            │
│                                                                            │
│  Entry Points: HandleRequest(), HandleHandshake()                          │
└────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────────┐
│                   LAYER 4: RESOURCE PROTECTION                             │
│                   (Grain Activation, Memory, CPU)                          │
│                                                                            │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐            │
│  │ Grain Creation  │  │ Memory          │  │ CPU Time        │            │
│  │ Limiter         │  │ Watchdog        │  │ Watchdog        │            │
│  │                 │  │                 │  │                 │            │
│  │ • Max 100 new   │  │ • Max heap per  │  │ • Per-request   │            │
│  │   grains/min    │  │   connection    │  │   timeout       │            │
│  │   per client    │  │ • Force GC on   │  │ • Long-running  │            │
│  │ • ClientCreat-  │  │   pressure      │  │   detection     │            │
│  │   able check    │  │                 │  │                 │            │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘            │
│                                                                            │
│  Entry Points: RpcCatalog, RpcConnection                                   │
└────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────────┐
│                   MONITORING & RESPONSE                                    │
│                                                                            │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐            │
│  │ Metrics         │  │ Anomaly         │  │ Automatic       │            │
│  │ Collection      │  │ Detection       │  │ Response        │            │
│  │                 │  │                 │  │                 │            │
│  │ • Per-IP stats  │  │ • Traffic       │  │ • Auto-block    │            │
│  │ • Rate limit    │  │   pattern       │  │   on threshold  │            │
│  │   violations    │  │   analysis      │  │ • Graceful      │            │
│  │ • Resource use  │  │ • Threshold     │  │   degradation   │            │
│  │                 │  │   alerts        │  │ • Recovery      │            │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘            │
│                                                                            │
│  Integration: RpcServerTelemetry, ISecurityEventLogger                     │
└────────────────────────────────────────────────────────────────────────────┘
```

---

## Implementation Plan

### Phase 8A: Transport Layer Protection (Foundation)

**Priority**: CRITICAL
**Estimated Effort**: 3-5 days
**Dependencies**: None

#### 8A.1: Connection Rate Limiter

```csharp
// Location: Orleans.Rpc.Security/Protection/ConnectionRateLimiter.cs

public interface IConnectionRateLimiter
{
    /// <summary>
    /// Check if a connection from this IP should be allowed.
    /// </summary>
    bool ShouldAllowConnection(IPAddress ipAddress);

    /// <summary>
    /// Record a connection attempt (successful or not).
    /// </summary>
    void RecordConnectionAttempt(IPAddress ipAddress, bool accepted);

    /// <summary>
    /// Get current status for an IP.
    /// </summary>
    ConnectionRateLimitStatus GetStatus(IPAddress ipAddress);
}

public class SlidingWindowConnectionRateLimiter : IConnectionRateLimiter
{
    private readonly ConcurrentDictionary<IPAddress, ConnectionTracker> _trackers;
    private readonly ConnectionRateLimitOptions _options;

    // Default: 10 connections per second per IP, burst of 20
}

public class ConnectionRateLimitOptions
{
    public int ConnectionsPerSecond { get; set; } = 10;
    public int BurstAllowance { get; set; } = 20;
    public TimeSpan WindowSize { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan BlockDuration { get; set; } = TimeSpan.FromMinutes(15);
    public int ViolationsBeforeBlock { get; set; } = 5;
}
```

#### 8A.2: IP Blocklist Manager

```csharp
// Location: Orleans.Rpc.Security/Protection/IpBlocklistManager.cs

public interface IIpBlocklistManager
{
    bool IsBlocked(IPAddress ipAddress);
    void AddToBlocklist(IPAddress ipAddress, TimeSpan? duration = null, string reason = null);
    void RemoveFromBlocklist(IPAddress ipAddress);
    IReadOnlyList<BlockedIpEntry> GetBlockedIps();
}

public class BlockedIpEntry
{
    public IPAddress IpAddress { get; set; }
    public DateTime BlockedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }  // null = permanent
    public string Reason { get; set; }
    public int ViolationCount { get; set; }
}
```

#### 8A.3: Connection Limit Enforcement

```csharp
// Location: Orleans.Rpc.Security/Protection/ConnectionLimitEnforcer.cs

public interface IConnectionLimitEnforcer
{
    bool CanAcceptConnection(IPAddress ipAddress);
    void OnConnectionAccepted(string connectionId, IPAddress ipAddress);
    void OnConnectionClosed(string connectionId);

    int CurrentConnectionCount { get; }
    int ConnectionCountForIp(IPAddress ipAddress);
}

public class ConnectionLimitOptions
{
    public int MaxTotalConnections { get; set; } = 1000;
    public int MaxConnectionsPerIp { get; set; } = 10;
    public bool EnableLruEviction { get; set; } = true;
    public TimeSpan IdleConnectionTimeout { get; set; } = TimeSpan.FromMinutes(5);
}
```

#### 8A.4: Transport Integration

Modify `LiteNetLibTransport.OnConnectionRequest`:

```csharp
public void OnConnectionRequest(ConnectionRequest request)
{
    var ipAddress = request.RemoteEndPoint.Address;

    // Layer 1: Check blocklist
    if (_blocklistManager.IsBlocked(ipAddress))
    {
        _logger.LogWarning("Rejecting blocked IP: {IP}", ipAddress);
        request.Reject();
        _telemetry.RecordBlockedConnection(ipAddress, "blocklist");
        return;
    }

    // Layer 2: Check connection rate limit
    if (!_connectionRateLimiter.ShouldAllowConnection(ipAddress))
    {
        _logger.LogWarning("Rate limiting connection from: {IP}", ipAddress);
        _connectionRateLimiter.RecordConnectionAttempt(ipAddress, false);
        request.Reject();
        _telemetry.RecordBlockedConnection(ipAddress, "rate_limit");
        return;
    }

    // Layer 3: Check connection limits
    if (!_connectionLimitEnforcer.CanAcceptConnection(ipAddress))
    {
        _logger.LogWarning("Connection limit exceeded for: {IP}", ipAddress);
        request.Reject();
        _telemetry.RecordBlockedConnection(ipAddress, "connection_limit");
        return;
    }

    // Accept connection
    if (_isServer && (string.IsNullOrEmpty(connectionKey) || connectionKey == "RpcConnection"))
    {
        request.Accept();
        _connectionRateLimiter.RecordConnectionAttempt(ipAddress, true);
    }
    else
    {
        request.Reject();
    }
}
```

**Deliverables**:
- [ ] `IConnectionRateLimiter` interface and implementation
- [ ] `IIpBlocklistManager` interface and implementation
- [ ] `IConnectionLimitEnforcer` interface and implementation
- [ ] Transport integration in `LiteNetLibTransport`
- [ ] Configuration options classes
- [ ] Unit tests for each component
- [ ] Integration tests for connection scenarios

---

### Phase 8B: Packet Layer Protection

**Priority**: CRITICAL
**Estimated Effort**: 3-4 days
**Dependencies**: Phase 8A

#### 8B.1: Message Size Validator

```csharp
// Location: Orleans.Rpc.Security/Protection/MessageSizeValidator.cs

public interface IMessageSizeValidator
{
    bool IsValidSize(int messageSize);
    int MaxMessageSize { get; }
}

public class MessageSizeOptions
{
    public int MaxMessageSizeBytes { get; set; } = 65536;  // 64KB default
    public int MaxHandshakeResponseSize { get; set; } = 131072;  // 128KB (manifest can be large)
    public bool LogOversizedMessages { get; set; } = true;
}
```

#### 8B.2: Per-Connection Rate Limiter

```csharp
// Location: Orleans.Rpc.Security/Protection/ConnectionMessageRateLimiter.cs

public interface IConnectionMessageRateLimiter
{
    bool ShouldAllowMessage(string connectionId);
    void RecordMessage(string connectionId, int messageSize);
    MessageRateLimitStatus GetStatus(string connectionId);
}

public class MessageRateLimitOptions
{
    public int MessagesPerMinute { get; set; } = 1000;
    public int BurstAllowance { get; set; } = 50;
    public int BytesPerMinute { get; set; } = 10_000_000;  // 10MB/min
    public TimeSpan WindowSize { get; set; } = TimeSpan.FromMinutes(1);
}
```

#### 8B.3: Deserialization Timeout

```csharp
// Location: Orleans.Rpc.Security/Protection/DeserializationGuard.cs

public interface IDeserializationGuard
{
    Task<TMessage> DeserializeWithTimeoutAsync<TMessage>(
        ReadOnlyMemory<byte> data,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public class DeserializationOptions
{
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromMilliseconds(100);
    public TimeSpan MaxTimeout { get; set; } = TimeSpan.FromSeconds(1);
    public bool ThrowOnTimeout { get; set; } = false;  // false = return null
}
```

#### 8B.4: Server Integration

Modify `RpcServer.OnDataReceived`:

```csharp
private async void OnDataReceived(object sender, RpcDataReceivedEventArgs e)
{
    try
    {
        // Layer 1: Message size validation
        if (!_messageSizeValidator.IsValidSize(e.Data.Length))
        {
            _logger.LogWarning("Oversized message ({Size} bytes) from {Endpoint}",
                e.Data.Length, e.RemoteEndPoint);
            _telemetry.RecordOversizedMessage(e.RemoteEndPoint, e.Data.Length);
            return;  // Silently drop
        }

        // Layer 2: Per-connection rate limiting
        if (!_messageRateLimiter.ShouldAllowMessage(e.ConnectionId))
        {
            _logger.LogWarning("Rate limit exceeded for connection {ConnectionId}", e.ConnectionId);
            _telemetry.RecordRateLimitViolation(e.ConnectionId, "message_rate");

            // Consider escalating to IP block after repeated violations
            return;
        }

        _messageRateLimiter.RecordMessage(e.ConnectionId, e.Data.Length);

        // Layer 3: Protected deserialization
        var message = await _deserializationGuard.DeserializeWithTimeoutAsync<object>(
            e.Data,
            _deserializationOptions.DefaultTimeout,
            CancellationToken.None);

        if (message == null)
        {
            _logger.LogWarning("Deserialization timeout or failure from {Endpoint}", e.RemoteEndPoint);
            _telemetry.RecordDeserializationFailure(e.RemoteEndPoint);
            return;
        }

        // Continue with normal message handling...
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error processing received data from {Endpoint}", e.RemoteEndPoint);
    }
}
```

**Deliverables**:
- [ ] `IMessageSizeValidator` interface and implementation
- [ ] `IConnectionMessageRateLimiter` interface and implementation
- [ ] `IDeserializationGuard` interface and implementation
- [ ] Server integration in `RpcServer.OnDataReceived`
- [ ] Unit tests for each component
- [ ] Performance benchmarks for overhead

---

### Phase 9A: Request Layer Protection (Per-User)

**Priority**: HIGH
**Estimated Effort**: 4-5 days
**Dependencies**: Phase 8B, Phase 4 (RPC Call Context)

#### 9A.1: Per-User Rate Limiter

```csharp
// Location: Orleans.Rpc.Security/Protection/UserRateLimiter.cs

public interface IUserRateLimiter
{
    bool ShouldAllowRequest(string userId, string methodName);
    void RecordRequest(string userId, string methodName);
    UserRateLimitStatus GetStatus(string userId);
}

public class UserRateLimitOptions
{
    public Dictionary<string, int> RoleRequestsPerMinute { get; set; } = new()
    {
        ["guest"] = 100,
        ["user"] = 500,
        ["server"] = int.MaxValue,  // Unlimited for server-to-server
        ["admin"] = int.MaxValue
    };

    public int DefaultRequestsPerMinute { get; set; } = 100;
    public int BurstAllowance { get; set; } = 20;
}
```

#### 9A.2: Method-Based Rate Limiter

```csharp
// Location: Orleans.Rpc.Security/Protection/MethodRateLimiter.cs

public interface IMethodRateLimiter
{
    bool ShouldAllowMethod(string userId, string grainType, string methodName);
}

[AttributeUsage(AttributeTargets.Method)]
public class RateLimitAttribute : Attribute
{
    public int RequestsPerMinute { get; set; } = 100;
    public int BurstAllowance { get; set; } = 10;
}

// Example usage:
public interface IExpensiveGrain : IGrainWithStringKey
{
    [RateLimit(RequestsPerMinute = 10, BurstAllowance = 2)]
    Task<Report> GenerateReport();

    [RateLimit(RequestsPerMinute = 1)]
    Task<byte[]> ExportAllData();
}
```

#### 9A.3: Request Queue Management

```csharp
// Location: Orleans.Rpc.Security/Protection/RequestQueueManager.cs

public interface IRequestQueueManager
{
    bool TryEnqueueRequest(string connectionId, Guid requestId);
    void DequeueRequest(string connectionId, Guid requestId);
    int GetPendingCount(string connectionId);
}

public class RequestQueueOptions
{
    public int MaxPendingRequestsPerConnection { get; set; } = 100;
    public int MaxTotalPendingRequests { get; set; } = 10000;
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
```

**Deliverables**:
- [ ] `IUserRateLimiter` interface and implementation
- [ ] `IMethodRateLimiter` interface and implementation
- [ ] `[RateLimit]` attribute and enforcement
- [ ] `IRequestQueueManager` interface and implementation
- [ ] Integration with `RpcServer.HandleRequest`
- [ ] Unit tests and integration tests

---

### Phase 9B: Resource Protection

**Priority**: HIGH
**Estimated Effort**: 3-4 days
**Dependencies**: Phase 9A

#### 9B.1: Grain Creation Limiter

```csharp
// Location: Orleans.Rpc.Security/Protection/GrainCreationLimiter.cs

public interface IGrainCreationLimiter
{
    bool CanCreateGrain(string userId, string grainType);
    void RecordGrainCreation(string userId, string grainType);
}

public class GrainCreationOptions
{
    public int MaxGrainCreationsPerMinute { get; set; } = 100;
    public int MaxActiveGrainsPerUser { get; set; } = 1000;
    public HashSet<string> ExemptGrainTypes { get; set; } = new();  // System grains
}
```

#### 9B.2: Memory Watchdog

```csharp
// Location: Orleans.Rpc.Security/Protection/MemoryWatchdog.cs

public interface IMemoryWatchdog
{
    bool IsMemoryPressureHigh { get; }
    void CheckAndRespond();
}

public class MemoryWatchdogOptions
{
    public long WarningThresholdBytes { get; set; } = 1_000_000_000;  // 1GB
    public long CriticalThresholdBytes { get; set; } = 1_500_000_000;  // 1.5GB
    public bool ForceGcOnCritical { get; set; } = true;
    public bool RejectNewConnectionsOnCritical { get; set; } = true;
}
```

#### 9B.3: CPU Time Watchdog

```csharp
// Location: Orleans.Rpc.Security/Protection/CpuWatchdog.cs

public interface ICpuWatchdog
{
    Task<T> ExecuteWithTimeoutAsync<T>(Func<Task<T>> operation, TimeSpan timeout);
}

public class CpuWatchdogOptions
{
    public TimeSpan DefaultRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan MaxRequestTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public bool CancelOnTimeout { get; set; } = true;
}
```

**Deliverables**:
- [ ] `IGrainCreationLimiter` interface and implementation
- [ ] `IMemoryWatchdog` interface and implementation
- [ ] `ICpuWatchdog` interface and implementation
- [ ] Integration with grain activation pipeline
- [ ] Health check endpoints
- [ ] Unit tests

---

### Phase 9C: Monitoring and Response

**Priority**: HIGH
**Estimated Effort**: 2-3 days
**Dependencies**: Phases 8A-9B

#### 9C.1: DDoS Metrics

```csharp
// Location: Orleans.Rpc.Security/Telemetry/DDoSMetrics.cs

public static class DDoSMetrics
{
    // Connection metrics
    public static readonly Counter<long> ConnectionsAccepted;
    public static readonly Counter<long> ConnectionsRejected;
    public static readonly Counter<long> ConnectionsBlocklisted;

    // Rate limit metrics
    public static readonly Counter<long> RateLimitViolations;
    public static readonly Histogram<double> RequestsPerSecondPerIp;

    // Resource metrics
    public static readonly Gauge<long> ActiveConnections;
    public static readonly Gauge<long> PendingRequests;
    public static readonly Gauge<double> MemoryUsagePercent;

    // Attack detection
    public static readonly Counter<long> SuspiciousPatterns;
    public static readonly Counter<long> AutoBlocksTriggered;
}
```

#### 9C.2: Anomaly Detection

```csharp
// Location: Orleans.Rpc.Security/Protection/AnomalyDetector.cs

public interface IAnomalyDetector
{
    void RecordEvent(SecurityEvent securityEvent);
    bool IsAnomalous(IPAddress ipAddress);
    IReadOnlyList<AnomalyReport> GetRecentAnomalies();
}

public class AnomalyDetectionOptions
{
    // Traffic spike detection
    public double TrafficSpikeThreshold { get; set; } = 5.0;  // 5x normal

    // Pattern detection
    public int SuspiciousPatternThreshold { get; set; } = 100;  // violations
    public TimeSpan PatternWindow { get; set; } = TimeSpan.FromMinutes(5);

    // Auto-response
    public bool EnableAutoBlock { get; set; } = true;
    public TimeSpan AutoBlockDuration { get; set; } = TimeSpan.FromMinutes(30);
}
```

#### 9C.3: Automatic Response

```csharp
// Location: Orleans.Rpc.Security/Protection/AutomaticResponseHandler.cs

public interface IAutomaticResponseHandler
{
    void OnRateLimitViolation(IPAddress ipAddress, string violationType);
    void OnAnomalyDetected(AnomalyReport anomaly);
    void OnResourcePressure(ResourcePressureLevel level);
}

public enum ResponseAction
{
    None,
    TemporaryBlock,
    PermanentBlock,
    Throttle,
    GracefulDegrade,
    AlertOnly
}
```

**Deliverables**:
- [ ] DDoS-specific telemetry counters and gauges
- [ ] `IAnomalyDetector` interface and implementation
- [ ] `IAutomaticResponseHandler` interface and implementation
- [ ] Dashboard integration (Grafana templates)
- [ ] Alert rules for monitoring systems
- [ ] Unit tests

---

## Configuration API

### Unified Security Configuration

```csharp
// Usage in Program.cs or Startup.cs
services.AddGranvilleRpcSecurity(security =>
{
    // Transport layer protection
    security.AddConnectionRateLimiting(options =>
    {
        options.ConnectionsPerSecond = 10;
        options.BurstAllowance = 20;
        options.BlockDuration = TimeSpan.FromMinutes(15);
    });

    security.AddIpBlocklist(options =>
    {
        options.PermanentBlocklist = new[] { "192.168.1.100" };
        options.AllowlistOverrides = true;
    });

    security.AddConnectionLimits(options =>
    {
        options.MaxTotalConnections = 1000;
        options.MaxConnectionsPerIp = 10;
    });

    // Packet layer protection
    security.AddMessageSizeValidation(options =>
    {
        options.MaxMessageSizeBytes = 65536;
    });

    security.AddMessageRateLimiting(options =>
    {
        options.MessagesPerMinute = 1000;
        options.BytesPerMinute = 10_000_000;
    });

    // Request layer protection
    security.AddUserRateLimiting(options =>
    {
        options.RoleRequestsPerMinute["guest"] = 100;
        options.RoleRequestsPerMinute["user"] = 500;
    });

    // Resource protection
    security.AddResourceProtection(options =>
    {
        options.MaxPendingRequestsPerConnection = 100;
        options.MemoryWarningThreshold = 1_000_000_000;
    });

    // Monitoring
    security.AddAnomalyDetection(options =>
    {
        options.EnableAutoBlock = true;
        options.TrafficSpikeThreshold = 5.0;
    });
});
```

### Appsettings.json Configuration

```json
{
  "GranvilleRpc": {
    "Security": {
      "DDoSProtection": {
        "Enabled": true,

        "ConnectionRateLimiting": {
          "ConnectionsPerSecond": 10,
          "BurstAllowance": 20,
          "BlockDuration": "00:15:00"
        },

        "IpBlocklist": {
          "PermanentBlocklist": [],
          "Allowlist": []
        },

        "ConnectionLimits": {
          "MaxTotalConnections": 1000,
          "MaxConnectionsPerIp": 10,
          "IdleConnectionTimeout": "00:05:00"
        },

        "MessageRateLimiting": {
          "MessagesPerMinute": 1000,
          "BytesPerMinute": 10000000,
          "MaxMessageSizeBytes": 65536
        },

        "UserRateLimiting": {
          "DefaultRequestsPerMinute": 100,
          "RoleOverrides": {
            "guest": 100,
            "user": 500,
            "admin": -1
          }
        },

        "ResourceProtection": {
          "MaxPendingRequestsPerConnection": 100,
          "MaxTotalPendingRequests": 10000,
          "MemoryWarningThresholdMB": 1000,
          "MemoryCriticalThresholdMB": 1500
        },

        "AnomalyDetection": {
          "Enabled": true,
          "EnableAutoBlock": true,
          "TrafficSpikeThreshold": 5.0,
          "AutoBlockDuration": "00:30:00"
        }
      }
    }
  }
}
```

---

## File Structure

```
src/Rpc/Orleans.Rpc.Security/
├── Configuration/
│   ├── DDoSProtectionOptions.cs
│   ├── ConnectionRateLimitOptions.cs
│   ├── MessageRateLimitOptions.cs
│   ├── UserRateLimitOptions.cs
│   └── ResourceProtectionOptions.cs
├── Protection/
│   ├── Transport/
│   │   ├── IConnectionRateLimiter.cs
│   │   ├── SlidingWindowConnectionRateLimiter.cs
│   │   ├── IIpBlocklistManager.cs
│   │   ├── InMemoryIpBlocklistManager.cs
│   │   ├── IConnectionLimitEnforcer.cs
│   │   └── ConnectionLimitEnforcer.cs
│   ├── Packet/
│   │   ├── IMessageSizeValidator.cs
│   │   ├── MessageSizeValidator.cs
│   │   ├── IConnectionMessageRateLimiter.cs
│   │   ├── ConnectionMessageRateLimiter.cs
│   │   ├── IDeserializationGuard.cs
│   │   └── DeserializationGuard.cs
│   ├── Request/
│   │   ├── IUserRateLimiter.cs
│   │   ├── UserRateLimiter.cs
│   │   ├── IMethodRateLimiter.cs
│   │   ├── MethodRateLimiter.cs
│   │   ├── RateLimitAttribute.cs
│   │   ├── IRequestQueueManager.cs
│   │   └── RequestQueueManager.cs
│   └── Resource/
│       ├── IGrainCreationLimiter.cs
│       ├── GrainCreationLimiter.cs
│       ├── IMemoryWatchdog.cs
│       ├── MemoryWatchdog.cs
│       ├── ICpuWatchdog.cs
│       └── CpuWatchdog.cs
├── Telemetry/
│   ├── DDoSMetrics.cs
│   ├── IAnomalyDetector.cs
│   ├── AnomalyDetector.cs
│   ├── IAutomaticResponseHandler.cs
│   └── AutomaticResponseHandler.cs
├── Extensions/
│   ├── DDoSProtectionExtensions.cs
│   └── ServiceCollectionExtensions.cs
└── Middleware/
    ├── TransportProtectionMiddleware.cs
    ├── PacketProtectionMiddleware.cs
    └── RequestProtectionMiddleware.cs
```

---

## Performance Considerations

### Overhead Targets

| Protection Layer | Max Added Latency | Memory Overhead |
|-----------------|-------------------|-----------------|
| Connection rate limiting | <1ms | ~100 bytes/IP |
| IP blocklist check | <0.1ms | ~50 bytes/entry |
| Connection limit check | <0.1ms | ~200 bytes/conn |
| Message size validation | <0.01ms | 0 |
| Message rate limiting | <0.5ms | ~500 bytes/conn |
| User rate limiting | <1ms | ~200 bytes/user |
| Total per-request | <3ms | - |

### Optimization Strategies

1. **Lock-free data structures**: Use `ConcurrentDictionary` with atomic operations
2. **Sliding window approximation**: Use token bucket instead of exact tracking
3. **Lazy evaluation**: Only compute expensive checks when needed
4. **Tiered checking**: Fast checks first, expensive checks only on edge cases
5. **Periodic cleanup**: Background task removes stale entries

### Benchmarks to Include

```csharp
// Benchmark: Connection rate limiter throughput
// Target: >1M checks/second

// Benchmark: Message rate limiter throughput
// Target: >500K checks/second

// Benchmark: End-to-end latency impact
// Target: <5% increase in P99 latency
```

---

## Testing Strategy

### Unit Tests

- [ ] Connection rate limiter sliding window accuracy
- [ ] IP blocklist add/remove/expire
- [ ] Connection limit enforcement
- [ ] Message size validation edge cases
- [ ] User rate limiting with role tiers
- [ ] Anomaly detection threshold triggering

### Integration Tests

- [ ] Transport protection with LiteNetLib
- [ ] Full request path with all protections enabled
- [ ] Graceful degradation under load
- [ ] Recovery after attack subsides

### Load Tests

- [ ] Connection flood simulation (10K connections/sec)
- [ ] Message flood simulation (100K messages/sec)
- [ ] Mixed attack patterns
- [ ] Legitimate traffic during attack

### Chaos Tests

- [ ] Random protection component failures
- [ ] Memory pressure scenarios
- [ ] Network partition scenarios

---

## Rollout Strategy

### Phase 1: Development Mode (Default Off)
- All protections available but disabled by default
- Logging-only mode for initial testing
- Easy toggle per-feature

### Phase 2: Monitoring Mode
- Enable protections in logging-only mode in production
- Tune thresholds based on real traffic patterns
- Build baseline metrics

### Phase 3: Enforcement Mode
- Enable enforcement with conservative limits
- Start with soft blocks (warn, then block)
- Gradual tightening of limits

### Phase 4: Full Protection
- All protections enabled with tuned limits
- Automatic response enabled
- Continuous monitoring and adjustment

---

## Success Criteria

### Quantitative

- [ ] Can handle 10x normal traffic without service degradation
- [ ] Connection flood blocked within 100ms of detection
- [ ] <5% increase in P99 latency for legitimate traffic
- [ ] Zero false positives for normal user patterns
- [ ] Memory overhead <100MB for 10K concurrent connections

### Qualitative

- [ ] Clear documentation for operators
- [ ] Easy configuration via appsettings.json
- [ ] Meaningful metrics in monitoring dashboards
- [ ] Automatic recovery after attack subsides
- [ ] Graceful degradation preserves core functionality

---

## Related Documents

- `SECURITY-RECAP.md` - Overall security roadmap
- `PSK-ARCHITECTURE-PLAN.md` - Transport security
- `THREAT-MODEL.md` - Threat analysis
- `SECURITY-CONCERNS.md` - Vulnerability catalog

---

## Appendix: Attack Scenarios and Responses

### Scenario 1: Connection Flood Attack

**Attack**: Attacker opens thousands of connections per second from multiple IPs.

**Detection**:
- Connection rate exceeds threshold per IP
- Total connection count rising rapidly
- Connection establishment time increasing

**Response**:
1. Rate limit triggers → temporary IP blocks
2. Connection limit enforced → excess connections rejected
3. If attack persists → anomaly detection triggers
4. Auto-response → extend block duration, alert operators

### Scenario 2: Slowloris Attack

**Attack**: Attacker opens connections but sends data very slowly.

**Detection**:
- Connections with low message rate
- High pending request count
- Connection age without activity

**Response**:
1. Idle connection timeout → connection closed
2. Pending request timeout → request cancelled
3. Repeated patterns → IP blocked

### Scenario 3: Amplification Attack

**Attack**: Attacker sends small requests that generate large responses.

**Detection**:
- Response size >> request size ratio
- Specific endpoints being targeted
- Unusual source IP patterns

**Response**:
1. Method rate limiting → expensive operations throttled
2. Response size limits where appropriate
3. Challenge-response for suspicious patterns

### Scenario 4: Resource Exhaustion

**Attack**: Attacker creates many grains or sends computationally expensive requests.

**Detection**:
- Memory usage increasing
- CPU usage sustained high
- Grain creation rate spiking

**Response**:
1. Grain creation limit → excess creations blocked
2. Memory watchdog → GC forced, new connections rejected
3. CPU watchdog → long requests cancelled
4. Graceful degradation → non-essential features disabled
