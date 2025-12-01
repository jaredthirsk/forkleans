## ADDED Requirements

### Requirement: Security Configuration Validation

The system SHALL validate security configuration at startup and fail fast if misconfigured.

#### Scenario: Missing security fails startup
- **WHEN** the application starts
- **AND** neither PSK nor NoSecurity is explicitly configured
- **THEN** startup fails with a clear error message

#### Scenario: NoSecurity warning in non-dev
- **WHEN** NoSecurity mode is used
- **AND** the environment is not Development
- **THEN** a prominent warning is logged at startup

### Requirement: Key Rotation Support

The system SHALL support rotating session keys without disconnecting active clients.

#### Scenario: Graceful key rotation
- **WHEN** a key rotation is triggered
- **THEN** both old and new keys are valid during a transition window
- **AND** new sessions use only the new key
- **AND** after the window, only the new key is valid

### Requirement: HTTP Security Headers

The system SHALL include security headers in HTTP responses for the web client.

#### Scenario: Security headers present
- **WHEN** an HTTP response is sent
- **THEN** it includes Content-Security-Policy, X-Frame-Options, X-Content-Type-Options headers

### Requirement: Error Response Sanitization

The system SHALL not expose sensitive information in error responses in production.

#### Scenario: Stack traces hidden
- **WHEN** an error occurs in production
- **THEN** the error response includes a RequestId but not stack traces or internal details

#### Scenario: Development details allowed
- **WHEN** an error occurs in development
- **THEN** detailed error information may be included for debugging

### Requirement: Rate Limiting on Auth Endpoints

The system SHALL rate limit HTTP authentication endpoints to prevent brute force attacks.

#### Scenario: Auth rate limited
- **WHEN** a client makes more than 10 authentication attempts per minute
- **THEN** further attempts are rejected with 429 Too Many Requests
