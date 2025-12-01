## ADDED Requirements

### Requirement: Security Event Logging

The system SHALL log all security-relevant events with structured data for audit and analysis.

#### Scenario: Authentication logged
- **WHEN** a user authenticates successfully or fails
- **THEN** an authentication event is logged with UserId, IP, timestamp, and result

#### Scenario: Authorization logged
- **WHEN** an authorization check denies access
- **THEN** an authorization event is logged with method, user, required role, and denial reason

### Requirement: Request Correlation

The system SHALL assign a unique RequestId to each RPC request for distributed tracing.

#### Scenario: RequestId propagated
- **WHEN** an RPC request is processed
- **THEN** all security events include the same RequestId
- **AND** error responses include the RequestId for troubleshooting

### Requirement: Sensitive Data Redaction

The system SHALL never log sensitive data such as session keys or passwords.

#### Scenario: Keys not logged
- **WHEN** a security event involves a session key
- **THEN** only a fingerprint (hash) of the key is logged, not the key itself

#### Scenario: IP redaction option
- **WHEN** GDPR compliance mode is enabled
- **THEN** IP addresses are redacted or anonymized in logs

### Requirement: Structured Log Format

The system SHALL emit security logs in structured JSON format compatible with SIEM systems.

#### Scenario: JSON log format
- **WHEN** a security event is logged
- **THEN** the log entry is valid JSON with standard fields (timestamp, eventType, level, details)
