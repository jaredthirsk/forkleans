## ADDED Requirements

### Requirement: Session Expiry

The system SHALL automatically expire sessions after a configurable timeout (default 4 hours).

#### Scenario: Expired session rejected
- **WHEN** a client uses an expired session key
- **THEN** the request fails with authentication error
- **AND** the client must re-authenticate

### Requirement: Session Revocation

The system SHALL support explicit session revocation via API.

#### Scenario: Logout revokes session
- **WHEN** a user logs out via the logout endpoint
- **THEN** their session is immediately revoked
- **AND** subsequent requests with that session key fail

#### Scenario: Admin revokes session
- **WHEN** an admin revokes a user's session
- **THEN** the session is immediately invalidated
- **AND** a security event is logged with the admin's identity

### Requirement: Concurrent Session Limits

The system SHALL limit the number of concurrent sessions per user (default 5).

#### Scenario: Limit enforced
- **WHEN** a user has 5 active sessions
- **AND** attempts to create a 6th session
- **THEN** either the oldest session is revoked OR the new session is rejected (configurable)

### Requirement: Session Activity Tracking

The system SHALL track last activity time for each session.

#### Scenario: Activity updated
- **WHEN** an RPC request is processed with a valid session
- **THEN** the session's last activity timestamp is updated

#### Scenario: Idle session expires
- **WHEN** a session has no activity for the idle timeout period
- **THEN** the session expires even before the absolute timeout

### Requirement: Session Listing

The system SHALL provide an API to list active sessions for a user (admin only).

#### Scenario: Admin lists sessions
- **WHEN** an admin requests active sessions for a user
- **THEN** a list of sessions is returned with creation time, last activity, and connection info
