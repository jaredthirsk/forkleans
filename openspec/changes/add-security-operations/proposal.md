# Change: Add Security Operations (Logging, Sessions, Hardening)

## Why

With core security features (authorization, rate limiting, input validation) in place, the system needs operational capabilities for production deployment:
1. **Security logging**: No audit trail for security events (login, authorization, rate limits)
2. **Session management**: Basic sessions exist but lack lifecycle management (expiry, revocation, limits)
3. **Production hardening**: Missing key rotation, security headers, startup validation

These are rated **MEDIUM** priority but required for production-ready status.

## What Changes

### Phase 12: Security Event Logging
- Structured logging of all security events (auth, authz, rate limits, blocked types)
- Request correlation IDs for distributed tracing
- Sensitive data redaction (no keys in logs)
- SIEM-ready log format

### Phase 13: Session Lifecycle Management
- Session expiry enforcement and cleanup
- Session revocation API (logout, admin kick)
- Concurrent session limits per user
- Activity tracking (last seen)

### Phase 15: Production Hardening
- Key rotation support for session keys
- Security configuration validation on startup
- Security headers in HTTP responses
- Penetration testing checklist

**Note**: Phase 14 (Anti-Cheat) is application-specific and will be implemented in the Shooter sample, not in the Granville RPC middleware.

## Impact

- **Affected specs**: Three new capabilities (rpc-security-logging, rpc-session-management, rpc-production-hardening)
- **Affected code**:
  - `/src/Rpc/Orleans.Rpc.Security/Logging/` - Security event logging
  - `/src/Rpc/Orleans.Rpc.Security/Sessions/` - Session lifecycle
- **Dependencies**: Phases 12-13 benefit from authorization context (Phase 4-7)
- **Breaking changes**: None - additive features

## References

- Security logging plan: `/src/Rpc/docs/security/roadmap/SECURITY-LOGGING-PLAN.md`
- Session lifecycle plan: `/src/Rpc/docs/security/roadmap/SESSION-LIFECYCLE-PLAN.md`
- Security roadmap Phases 12-15: `/src/Rpc/docs/security/roadmap/SECURITY-RECAP.md`
