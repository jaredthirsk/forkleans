## Context

Production security requires not just preventive controls (auth, rate limiting) but also detective controls (logging, monitoring) and operational capabilities (session management). This design covers the operational aspects of security for the Granville RPC middleware.

**Stakeholders**: Operations teams, security auditors, game developers

**Constraints**:
- Logging must not impact game performance (async, buffered)
- Session management must work across multiple ActionServers (Orleans-backed)
- Production hardening must not break development workflows

## Goals / Non-Goals

**Goals**:
- Complete audit trail of security-relevant events
- Robust session lifecycle with proper cleanup
- Secure configuration defaults for production

**Non-Goals**:
- Anti-cheat (application-specific, belongs in game code)
- Real-time threat intelligence integration
- Compliance certifications (SOC2, etc.) - future work

## Decisions

### Decision 1: Structured JSON Logging

**What**: All security events logged as structured JSON with consistent schema.

**Why**: Machine-parseable for SIEM ingestion, easy to filter/aggregate, standardized fields.

**Schema**:
```json
{
  "timestamp": "ISO8601",
  "eventType": "AUTHENTICATION_SUCCESS",
  "level": "INFO",
  "requestId": "guid",
  "userId": "player123",
  "ip": "redacted",
  "details": { ... }
}
```

### Decision 2: Orleans Grain for Session Storage

**What**: Continue using `IPlayerSessionGrain` for session state, add lifecycle methods.

**Why**: Already implemented for PSK, provides distributed state, supports zone transitions.

**Extensions**: Add `RevokeSessionAsync()`, `GetActiveSessionsAsync()`, `ExtendSessionAsync()`.

### Decision 3: Configuration Validation at Startup

**What**: Validate all security configuration on startup, fail fast if insecure.

**Why**: Prevents deploying with misconfigured security. Better to fail startup than run insecure.

## Risks / Trade-offs

| Risk | Mitigation |
|------|------------|
| Logging overhead impacts performance | Async logging, buffering, sampling for high-volume events |
| Session cleanup races | Orleans grain single-threaded, timer-based cleanup |
| Key rotation complexity | Dual-key support during rotation window |

## Migration Plan

1. **Phase 12**: Add security logging (non-breaking, additive)
2. **Phase 13**: Add session management APIs (non-breaking)
3. **Phase 15**: Enable production hardening checks
4. **Rollback**: Each phase independently configurable

**Note**: Phase 14 (Anti-Cheat) is not included - it's application-specific and should be implemented in game code (e.g., Shooter sample).

## Open Questions

- Should session revocation disconnect immediately or on next request? → Next request (simpler)
