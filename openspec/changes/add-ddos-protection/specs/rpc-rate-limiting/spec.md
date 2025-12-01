## ADDED Requirements

### Requirement: Connection Rate Limiting

The system SHALL limit the rate of new connections per source IP address to prevent connection flooding attacks.

#### Scenario: Normal connection rate allowed
- **WHEN** a client connects at less than 10 connections per second from a single IP
- **THEN** all connections are accepted normally

#### Scenario: Excessive connection rate blocked
- **WHEN** a client attempts more than 10 connections per second from a single IP
- **THEN** excess connections are rejected
- **AND** the IP is temporarily blocked for 15 minutes

### Requirement: IP Blocklist

The system SHALL support both permanent and temporary IP blocklists to block known malicious sources.

#### Scenario: Permanent block enforced
- **WHEN** an IP address is on the permanent blocklist
- **THEN** all connections from that IP are immediately rejected

#### Scenario: Temporary block expires
- **WHEN** an IP is temporarily blocked for rate limit violations
- **AND** 15 minutes have passed
- **THEN** the IP is automatically unblocked

### Requirement: Maximum Connections Enforcement

The system SHALL enforce a maximum total connection limit to prevent resource exhaustion.

#### Scenario: Connection limit reached
- **WHEN** the server has `MaxConnections` active connections
- **AND** a new connection is attempted
- **THEN** the new connection is rejected with a "server full" response

### Requirement: Message Size Validation

The system SHALL reject messages larger than the configured maximum size (default 64KB) to prevent memory exhaustion.

#### Scenario: Oversized message rejected
- **WHEN** a UDP packet larger than 64KB is received
- **THEN** the packet is dropped without processing
- **AND** a warning is logged

### Requirement: Per-Connection Message Rate Limiting

The system SHALL limit the rate of messages per connection to prevent packet flooding.

#### Scenario: Normal message rate processed
- **WHEN** a connection sends fewer than 1000 messages per minute
- **THEN** all messages are processed normally

#### Scenario: Excessive message rate throttled
- **WHEN** a connection exceeds 1000 messages per minute
- **THEN** excess messages are dropped
- **AND** a warning is logged with connection ID

### Requirement: Per-User Rate Limiting

The system SHALL limit the rate of RPC requests per authenticated user to prevent abuse.

#### Scenario: Guest user rate limited
- **WHEN** a Guest user sends more than 100 requests per minute
- **THEN** excess requests return `RpcStatus.ResourceExhausted`
- **AND** response includes `RetryAfter` metadata

#### Scenario: Regular user higher limit
- **WHEN** a User role sends more than 500 requests per minute
- **THEN** excess requests return `RpcStatus.ResourceExhausted`

#### Scenario: Server role unlimited
- **WHEN** a Server role sends requests
- **THEN** no rate limiting is applied

### Requirement: Per-Method Rate Limiting

The system SHALL support configurable rate limits for specific expensive operations.

#### Scenario: Expensive method limited
- **WHEN** a method is configured with a specific rate limit
- **AND** that limit is exceeded
- **THEN** the request returns `RpcStatus.ResourceExhausted`

### Requirement: Request Queue Management

The system SHALL limit pending requests per connection to prevent queue exhaustion.

#### Scenario: Queue limit enforced
- **WHEN** a connection has 100 pending requests
- **AND** another request arrives
- **THEN** the new request is rejected with `RpcStatus.ResourceExhausted`

### Requirement: Grain Creation Limiting

The system SHALL limit the rate of grain activations per client to prevent activation storms.

#### Scenario: Grain creation rate exceeded
- **WHEN** a client triggers more than 100 new grain activations per minute
- **THEN** further grain activations are rejected
- **AND** the client receives `RpcStatus.ResourceExhausted`

### Requirement: Memory Pressure Protection

The system SHALL reject new requests when under severe memory pressure to prevent crashes.

#### Scenario: Memory pressure detected
- **WHEN** the GC reports memory pressure above threshold
- **THEN** new requests are rejected with `RpcStatus.Unavailable`
- **AND** existing requests continue processing

### Requirement: Rate Limit Metrics

The system SHALL expose metrics for rate limiting events for monitoring and alerting.

#### Scenario: Metrics available
- **WHEN** rate limiting occurs
- **THEN** counters are incremented for the specific limit type
- **AND** metrics include: IP, user ID (if authenticated), limit name

### Requirement: Rate Limit Configuration

The system SHALL support configuration of all rate limits without code changes.

#### Scenario: Limits configurable
- **WHEN** rate limits are specified in configuration
- **THEN** the system uses configured values instead of defaults

#### Scenario: Development mode permissive
- **WHEN** `AddDDoSProtectionDevelopment()` is used
- **THEN** rate limits are set to permissive defaults suitable for development
