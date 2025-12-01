## 1. Security Event Logging Infrastructure

- [ ] 1.1 Create `Logging/ISecurityEventLogger.cs` interface
- [ ] 1.2 Create `Logging/SecurityEvent.cs` base record with common fields
- [ ] 1.3 Create `Logging/SecurityEventType.cs` enum (auth, authz, rate limit, etc.)
- [ ] 1.4 Create `Logging/DefaultSecurityEventLogger.cs` using ILogger
- [ ] 1.5 Implement async buffered logging to avoid blocking requests

## 2. Security Event Types

- [ ] 2.1 Create `AuthenticationSuccessEvent` with UserId, IP, SessionFingerprint
- [ ] 2.2 Create `AuthenticationFailureEvent` with AttemptedUserId, Reason, IP
- [ ] 2.3 Create `AuthorizationDeniedEvent` with Method, UserId, RequiredRole
- [ ] 2.4 Create `RateLimitExceededEvent` with LimitType, UserId/IP, Limit
- [ ] 2.5 Create `TypeRejectedEvent` with TypeName, Reason
- [ ] 2.6 Create `ValidationFailedEvent` with Method, ParameterErrors
- [ ] 2.7 Create `SessionRevokedEvent` with UserId, Reason, RevokedBy

## 3. Request Correlation

- [ ] 3.1 Add RequestId to `RpcSecurityContext`
- [ ] 3.2 Propagate RequestId through all security events
- [ ] 3.3 Include RequestId in error responses for troubleshooting
- [ ] 3.4 Create `CorrelationIdMiddleware` for HTTP requests

## 4. Sensitive Data Redaction

- [ ] 4.1 Never log session keys or PSK values
- [ ] 4.2 Hash/fingerprint session keys for correlation only
- [ ] 4.3 Optionally redact IP addresses (GDPR compliance mode)
- [ ] 4.4 Create `ILogRedactor` interface for custom redaction rules

## 5. Session Lifecycle (IPlayerSessionGrain Extensions)

- [ ] 5.1 Add `RevokeSessionAsync(reason)` method to revoke session
- [ ] 5.2 Add `ExtendSessionAsync()` method to refresh expiry on activity
- [ ] 5.3 Add `GetActiveSessionCountAsync()` for concurrent session tracking
- [ ] 5.4 Implement session expiry check in `ValidateSessionAsync()`
- [ ] 5.5 Add timer-based cleanup of expired sessions

## 6. Session Management API

- [ ] 6.1 Create `ISessionManager` interface for admin operations
- [ ] 6.2 Implement `ListActiveSessionsAsync(userId)` for admin view
- [ ] 6.3 Implement `RevokeAllSessionsAsync(userId)` for force logout
- [ ] 6.4 Implement `RevokeSessionAsync(sessionId)` for specific session
- [ ] 6.5 Add HTTP endpoints for session management (admin only)

## 7. Concurrent Session Limits

- [ ] 7.1 Add `MaxConcurrentSessions` configuration (default 5)
- [ ] 7.2 Check limit in `CreateSessionAsync()`
- [ ] 7.3 Option: reject new session or revoke oldest
- [ ] 7.4 Log when limit enforced

## 8. Production Hardening: Key Rotation

- [ ] 8.1 Design dual-key support during rotation window
- [ ] 8.2 Implement graceful key rotation without disconnecting clients
- [ ] 8.3 Add key rotation trigger (admin API or scheduled)
- [ ] 8.4 Log key rotation events

## 9. Production Hardening: Startup Validation

- [ ] 9.1 Create `ISecurityConfigValidator` interface
- [ ] 9.2 Check: PSK or NoSecurity explicitly configured
- [ ] 9.3 Check: Authorization filter registered
- [ ] 9.4 Check: Rate limiting configured
- [ ] 9.5 Warn: NoSecurity used in non-development environment
- [ ] 9.6 Fail startup if critical security misconfiguration detected

## 10. Production Hardening: HTTP Security

- [ ] 10.1 Add security headers middleware (CSP, X-Frame-Options, etc.)
- [ ] 10.2 Ensure HTTPS enforcement for HTTP endpoints
- [ ] 10.3 Add rate limiting to HTTP auth endpoints
- [ ] 10.4 Remove sensitive info from error responses in production

## 11. Shooter Sample Integration

- [ ] 11.1 Wire up security event logging
- [ ] 11.2 Add session management admin endpoints
- [ ] 11.3 Enable startup validation

## 12. Testing

- [ ] 12.1 Unit tests for security event logging
- [ ] 12.2 Unit tests for session lifecycle (create, extend, revoke, expire)
- [ ] 12.3 Unit tests for concurrent session limits
- [ ] 12.4 Integration test: full session lifecycle

## 13. Documentation

- [ ] 13.1 Document security event types and schemas
- [ ] 13.2 Document session management APIs
- [ ] 13.3 Create incident response runbook
- [ ] 13.4 Update SECURITY-RECAP.md to mark Phases 12-13, 15 complete
