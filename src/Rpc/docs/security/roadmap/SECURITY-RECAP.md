# Granville RPC Security Implementation Roadmap

**Last Updated**: 2025-11-30
**Version**: 2.2

## Executive Summary

Granville RPC has comprehensive security **documentation** (~3,500 lines) and **transport-layer implementation** (Phases 1-3 complete). This document provides a 15-phase incremental roadmap to achieve internet-ready security.

**Current Status**: Transport security implemented (UseNoSecurity for dev, UsePskEncryption for prod). Application-layer security (authorization, rate limiting) still needed for internet deployment.

**Key Architecture Decision**: Use **Pre-Shared Key (PSK)** architecture for transport security. See `PSK-ARCHITECTURE-PLAN.md` for detailed design.

## Architecture Overview

```
┌─────────────── TRANSPORT SECURITY (PSK Plan) ──────────────────┐
│                                                                 │
│  1. HTTP Authentication (guest mode, zero friction)            │
│  2. Orleans Session Grain (stores session_key per player)      │
│  3. DTLS-PSK Encryption (encrypted UDP, zero per-packet cost)  │
│                                                                 │
│  = Phases 1-3 of this roadmap                                  │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────── APPLICATION SECURITY (This Roadmap) ────────────┐
│                                                                 │
│  4. RPC Call Context (identity flows to grain methods)         │
│  5-7. Authorization (attributes, roles, grain access)          │
│  8-9. Rate Limiting (per-IP, per-user)                         │
│  10-11. Input Security (type whitelist, validation)            │
│  12-15. Operations (logging, sessions, anti-cheat, hardening)  │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

## Critical Security Gaps

| Gap | Risk Level | Current State |
|-----|------------|---------------|
| No Authentication | **CRITICAL** | Any client can call any RPC method |
| No Transport Encryption | **CRITICAL** | All UDP traffic is plaintext |
| No Authorization | **CRITICAL** | All methods accessible to all callers |
| No Rate Limiting | HIGH | Unlimited requests per client |
| Unsafe Deserialization | HIGH | Type whitelisting not enforced |
| No Input Validation | HIGH | No systematic parameter validation |

## Risk Matrix

| Threat | Likelihood | Impact | Risk | Detailed Plan |
|--------|------------|--------|------|---------------|
| DDoS Attacks | High | High | **CRITICAL** | [`DDOS-RESOURCE-EXHAUSTION-PLAN.md`](DDOS-RESOURCE-EXHAUSTION-PLAN.md) |
| Deserialization Exploits | High | Critical | **CRITICAL** | [`DESERIALIZATION-SAFETY-PLAN.md`](DESERIALIZATION-SAFETY-PLAN.md) |
| Authentication Bypass | Medium | High | High | Phases 1-4 |
| MITM Attacks | Medium | High | High | Phases 2-3 (PSK) |
| Game State Manipulation | High | Medium | High | Phase 14 |
| Resource Exhaustion | High | Medium | Medium | [`DDOS-RESOURCE-EXHAUSTION-PLAN.md`](DDOS-RESOURCE-EXHAUSTION-PLAN.md) |
| Session Hijacking | Medium | Medium | Medium | Phase 13 |

---

## Implementation Roadmap: 15 Phases

### Phase 1: HTTP Authentication & Session Grains
**Priority**: CRITICAL | **Status**: Complete
**Reference**: `PSK-ARCHITECTURE-PLAN.md` Phase 1

Implement HTTP guest authentication that generates session keys stored in Orleans grains.

**Deliverables**:
- [x] `IPlayerSessionGrain` interface in `Shooter.Shared/GrainInterfaces/`
- [x] `PlayerSessionGrain` implementation in `Shooter.Silo/Grains/`
- [x] Session key generation via existing `/api/world/players/register` endpoint
- [x] Session key generation (32 random bytes, 256-bit)
- [x] `PlayerSession` record with PlayerId, PlayerName, Role, SessionKey, Expiry
- [x] `UserRole` enum (Guest, User, Server, Admin)
- [x] Return session_key in `PlayerRegistrationResponse`
- [x] Client stores SessionKey and SessionExpiresAt for future DTLS use
- [ ] Unit tests for session grain (deferred to Phase 15)

**Design Notes**:
- Guest mode: any username accepted, no password required
- Session key is 256-bit random, stored in Orleans grain memory
- Sessions expire after 4 hours (configurable)
- Same user can have multiple sessions (different devices)

**Success Criteria**: Guest users get session_key via HTTP

---

### Phase 2: PSK Transport Layer
**Priority**: CRITICAL | **Status**: Complete
**Reference**: `PSK-ARCHITECTURE-PLAN.md` Phase 2-3

Implement encrypted UDP using AES-256-GCM with Pre-Shared Keys. (Simplified from full DTLS to custom handshake for faster implementation.)

**Deliverables**:
- [x] Add `BouncyCastle.Cryptography` NuGet package (for future DTLS)
- [x] `PskEncryptedTransport` class wrapping `IRpcTransport`
- [x] `PskSession` class with AES-256-GCM encryption
- [x] PSK lookup callback via `DtlsPskOptions.PskLookup`
- [x] Challenge-response handshake protocol
- [x] Client-side PSK configuration
- [x] `UsePskEncryption()` and `UseNoSecurity()` extension methods
- [x] `PskEncryptedTransportFactory` transport wrapper
- [x] `Granville.Rpc.Security` NuGet package
- [ ] Integration tests for encrypted communication (deferred to Phase 15)
- [ ] Performance benchmarks (deferred to Phase 15)

**Design Notes**:
- Custom challenge-response handshake (simpler than DTLS, same security)
- AES-256-GCM encryption with HKDF key derivation
- Sequence-based nonces prevent replay attacks
- Same session_key works across zone transitions (validated via Orleans)

**Location**: `/src/Rpc/Orleans.Rpc.Security/`

**Files Created**:
| File | Purpose |
|------|---------|
| `Orleans.Rpc.Security.csproj` | Security package project |
| `Configuration/DtlsPskOptions.cs` | PSK configuration options |
| `Transport/PskEncryptedTransport.cs` | Transport decorator |
| `Transport/PskSession.cs` | AES-GCM encryption logic |
| `Transport/PskEncryptedTransportFactory.cs` | Factory wrapper |
| `Extensions/SecurityExtensions.cs` | UsePskEncryption, UseNoSecurity |

**Success Criteria**: Encrypted UDP communication with session key validation

---

### Phase 3: Security Mode Configuration
**Priority**: CRITICAL | **Status**: Complete
**Reference**: `PSK-ARCHITECTURE-PLAN.md` Phase 5

Provide pluggable security modes for different deployment scenarios.

**Deliverables**:
- [x] `UseNoSecurity()` extension for development (logs warning at startup)
- [x] `UsePskEncryption()` extension for production
- [x] Warning logs when running without security (`NoSecurityTransportFactory`)
- [x] Wired into Shooter.ActionServer with documented PSK configuration
- [x] Wired into Shooter.Client.Common with documented PSK configuration
- [ ] Security configuration validation on startup (deferred to Phase 15)
- [ ] Additional documentation for production deployment (deferred to Phase 15)

**Configuration API**:
```csharp
// Development (logs warning at startup)
rpcBuilder.UseNoSecurity();

// Production (recommended)
rpcBuilder.UsePskEncryption(options =>
{
    options.IsServer = true;  // or false for client
    options.PskLookup = async (playerId, ct) =>
    {
        var sessionGrain = grainFactory.GetGrain<IPlayerSessionGrain>(playerId);
        var session = await sessionGrain.GetSessionAsync();
        return session?.GetSessionKeyBytes();
    };
});

// Client configuration
rpcBuilder.UsePskEncryption(options =>
{
    options.IsServer = false;
    options.PskIdentity = playerId;
    options.PskKey = sessionKeyBytes;
});
```

**Success Criteria**: Security modes available and integrated into Shooter sample

---

### Phases 4-7: Authorization Filter (RPC Call Context, Attributes, Roles, Grain Access)
**Priority**: CRITICAL | **Status**: Complete
**Detailed Plan**: [`AUTHORIZATION-FILTER-PLAN.md`](AUTHORIZATION-FILTER-PLAN.md)

**Deliverables** (All Complete):
- [x] `RpcSecurityContext` (AsyncLocal) - flows identity through async call chain
- [x] `RpcUserIdentity` - authenticated user record from PSK session
- [x] Authorization attributes: `[Authorize]`, `[AllowAnonymous]`, `[RequireRole]`, `[ServerOnly]`, `[ClientAccessible]`
- [x] `IRpcAuthorizationFilter` - Orleans-style filter pipeline
- [x] `DefaultRpcAuthorizationFilter` - attribute-based authorization
- [x] `IConnectionUserAccessor` - interface for retrieving user from connection
- [x] `PskLookupWithIdentity` callback - returns PSK and user identity together
- [x] Integration with `RpcConnection.ProcessRequestAsync()`
- [x] DI registration via `AddRpcAuthorization()`, `AddRpcAuthorizationProduction()`, `AddRpcAuthorizationDevelopment()`, `AddRpcAuthorizationDisabled()`
- [x] Shooter grain interfaces updated with authorization attributes
- [x] Unit tests for authorization filter

**Files Created**:
| File | Purpose |
|------|---------|
| `Orleans.Rpc.Abstractions/Security/RpcUserIdentity.cs` | User identity record |
| `Orleans.Rpc.Abstractions/Security/UserRole.cs` | Role enum (Anonymous, Guest, User, Server, Admin) |
| `Orleans.Rpc.Abstractions/Security/AuthorizeAttribute.cs` | Require authentication |
| `Orleans.Rpc.Abstractions/Security/AllowAnonymousAttribute.cs` | Bypass authentication |
| `Orleans.Rpc.Abstractions/Security/RequireRoleAttribute.cs` | Require minimum role |
| `Orleans.Rpc.Abstractions/Security/ServerOnlyAttribute.cs` | Server-to-server only |
| `Orleans.Rpc.Abstractions/Security/ClientAccessibleAttribute.cs` | Mark client-accessible grains |
| `Orleans.Rpc.Abstractions/Security/IConnectionUserAccessor.cs` | Interface for user lookup |
| `Orleans.Rpc.Security/Configuration/RpcSecurityOptions.cs` | Security configuration |
| `Orleans.Rpc.Security/Authorization/RpcSecurityContext.cs` | AsyncLocal security context |
| `Orleans.Rpc.Security/Authorization/IRpcAuthorizationFilter.cs` | Filter interface |
| `Orleans.Rpc.Security/Authorization/RpcAuthorizationContext.cs` | Authorization context |
| `Orleans.Rpc.Security/Authorization/AuthorizationResult.cs` | Authorization result |
| `Orleans.Rpc.Security/Authorization/DefaultRpcAuthorizationFilter.cs` | Default filter implementation |
| `Orleans.Rpc.Security/Extensions/RpcAuthorizationExtensions.cs` | DI extension methods |

**Key Insight**: PSK transport (Phases 1-3) already blocks unauthenticated connections. Phases 4-7 add **fine-grained authorization** for method/role/grain access control.

**Success Criteria** (All Met):
- [x] Grain methods can access `RpcSecurityContext.CurrentUser`
- [x] `[Authorize]` blocks anonymous requests
- [x] `[RequireRole(Admin)]` enforces role hierarchy
- [x] `[ServerOnly]` restricts internal grains from clients

---

### Phase 8: Rate Limiting (Per-IP) & DDoS Protection
**Priority**: HIGH | **Status**: Not Started
**Detailed Plan**: [`DDOS-RESOURCE-EXHAUSTION-PLAN.md`](DDOS-RESOURCE-EXHAUSTION-PLAN.md)

Basic DoS protection based on source IP address. The detailed plan breaks this into sub-phases:
- **Phase 8A**: Transport Layer Protection (connection rate limiting, IP blocklist, connection limits)
- **Phase 8B**: Packet Layer Protection (message size validation, per-connection rate limiting, deserialization timeout)

**Deliverables**:
- [ ] `IRateLimiter` interface
- [ ] `SlidingWindowRateLimiter` implementation
- [ ] Per-IP request tracking with configurable limits
- [ ] Burst allowance configuration
- [ ] Rate limit exceeded returns `RpcStatus.ResourceExhausted`
- [ ] IP-based blocklist/allowlist support
- [ ] Metrics for rate limit violations
- [ ] Load tests to verify effectiveness

**Configuration Example**:
```csharp
services.AddRpcRateLimiting(options =>
{
    options.PerIpLimit = 1000;        // requests per window
    options.WindowSize = TimeSpan.FromMinutes(1);
    options.BurstAllowance = 50;      // extra requests allowed in burst
});
```

---

### Phase 9: Rate Limiting (Per-User) & Resource Protection
**Priority**: HIGH | **Status**: Not Started
**Detailed Plan**: [`DDOS-RESOURCE-EXHAUSTION-PLAN.md`](DDOS-RESOURCE-EXHAUSTION-PLAN.md)

User-aware rate limiting and resource exhaustion protection. The detailed plan breaks this into sub-phases:
- **Phase 9A**: Request Layer Protection (per-user rate limiting, method-based limits, request queue management)
- **Phase 9B**: Resource Protection (grain creation limits, memory watchdog, CPU watchdog)
- **Phase 9C**: Monitoring & Response (DDoS metrics, anomaly detection, automatic response)

**Deliverables**:
- [ ] Per-user rate limiter (by PlayerId from `RpcUserContext`)
- [ ] Per-method rate limiting (different limits for different operations)
- [ ] Tiered limits based on user role
- [ ] Rate limit headers in responses (X-RateLimit-Remaining)
- [ ] Graceful degradation (warn before hard block)
- [ ] Configuration per-grain and per-method
- [ ] Integration tests with authenticated users

**Example Limits**:
```
Guest:   100 requests/minute
User:    500 requests/minute
Admin:   Unlimited
Server:  Unlimited
```

---

### Phase 10: Type Whitelisting (Deserialization Safety)
**Priority**: HIGH | **Status**: Not Started
**Detailed Plan**: [`DESERIALIZATION-SAFETY-PLAN.md`](DESERIALIZATION-SAFETY-PLAN.md)

Prevent arbitrary type instantiation during deserialization to block RCE attacks.

> **Note**: A comprehensive implementation plan with ~200 checklist items is available in
> [`DESERIALIZATION-SAFETY-PLAN.md`](DESERIALIZATION-SAFETY-PLAN.md). That document covers
> threat modeling, architecture, 6-week phased implementation, testing strategy, and rollout plan.

**Deliverables**:
- [ ] `ITypeWhitelist` interface
- [ ] `AllowedTypesRegistry` with approved types
- [ ] Automatic whitelisting of `[GenerateSerializer]` types
- [ ] Block unknown types during deserialization
- [ ] Deserialization depth limits
- [ ] Object graph size limits
- [ ] Logging of blocked deserialization attempts
- [ ] Security tests with gadget chain payloads

**Key Principle**: Only types explicitly marked for RPC serialization should be deserializable. Arbitrary .NET types (like `System.Diagnostics.Process`) must be blocked.

---

### Phase 11: Input Validation Framework
**Priority**: HIGH | **Status**: Not Started

Systematic validation of all RPC method parameters.

**Deliverables**:
- [ ] `[Validate]` attribute to enable validation on method
- [ ] Built-in validators: `[Required]`, `[StringLength]`, `[Range]`
- [ ] `[ValidateObject]` for nested object validation
- [ ] Custom validator support via `IValidator<T>`
- [ ] Validation errors return `RpcStatus.InvalidArgument`
- [ ] Validation error details in response metadata
- [ ] Performance benchmarks (validation overhead)
- [ ] Unit tests for each validator

**Example**:
```csharp
[Validate]
Task MovePlayer(
    [Required] string playerId,
    [Range(-1000, 1000)] float x,
    [Range(-1000, 1000)] float y);
```

---

### Phase 12: Security Event Logging
**Priority**: MEDIUM | **Status**: Not Started

Comprehensive audit trail for security-relevant events.

**Deliverables**:
- [ ] `ISecurityEventLogger` interface
- [ ] Structured logging for auth events (login, logout, failure)
- [ ] Authorization failure logging with context
- [ ] Rate limit violation logging
- [ ] Suspicious activity flagging
- [ ] Correlation IDs for request tracing
- [ ] Log redaction for sensitive data (no keys in logs)
- [ ] Integration with standard logging (ILogger)

**Events to Log**:
- Authentication success/failure (PlayerId, IP, not session_key)
- Authorization denied (method, user, required role)
- Rate limit exceeded (user, IP, endpoint)
- DTLS handshake failures
- Blocked deserialization attempt
- Input validation failures

---

### Phase 13: Session Management
**Priority**: MEDIUM | **Status**: Not Started

Full session lifecycle management for production use.

**Deliverables**:
- [ ] Session expiry and automatic cleanup in Orleans grain
- [ ] Session revocation API (logout, kick)
- [ ] Concurrent session limits per user (configurable)
- [ ] Session activity tracking (last seen)
- [ ] Admin API for session management (list, revoke)
- [ ] Handle expired session gracefully (force re-auth)
- [ ] Integration tests for session lifecycle

**Features**:
- Max 5 concurrent sessions per user (configurable)
- Sessions expire after 4 hours of inactivity (configurable)
- Force logout terminates all sessions for a user
- New login can optionally revoke oldest session

---

### Phase 14: Anti-Cheat Foundation
**Priority**: MEDIUM | **Status**: Not Started

Game-specific security measures for the Shooter demo.

**Deliverables**:
- [ ] Server-side physics validation (movement speed limits)
- [ ] Teleportation detection (impossible position changes)
- [ ] Fire rate validation (weapon cooldowns)
- [ ] Damage calculation server-side (don't trust client)
- [ ] Score validation
- [ ] Suspicious pattern detection (perfect accuracy, etc.)
- [ ] Soft-ban system (shadow realm for cheaters)
- [ ] Logging of detected violations

**Example Checks**:
```csharp
// Server validates movement
if (distance > maxSpeedPerTick * deltaTime * 1.1f)  // 10% tolerance
{
    Log.Warning("Possible speed hack: {PlayerId}", playerId);
    RejectMovement();
}
```

---

### Phase 15: Production Hardening
**Priority**: MEDIUM | **Status**: Not Started

Final security hardening for production deployment.

**Deliverables**:
- [ ] Key rotation support (graceful session_key rollover)
- [ ] Security headers in HTTP responses
- [ ] Secure defaults documentation
- [ ] Security configuration validation on startup
- [ ] Penetration testing checklist
- [ ] Security review process documentation
- [ ] Incident response runbook

---

## Phase Dependencies

```
Phase 1 (HTTP Auth) ──┬──> Phase 4 (RPC Context) ──> Phase 5 (Authz Attributes)
                      │                                        │
Phase 2 (DTLS-PSK) ───┤                                        v
                      │                              Phase 6 (Roles) ──> Phase 7 (Grain Access)
Phase 3 (Config) ─────┘

Phase 8 (IP Rate Limit) ──> Phase 9 (User Rate Limit)

Phase 10 (Type Whitelist) ──> Phase 11 (Input Validation)

Phase 12 (Logging) ──> Phase 13 (Session Mgmt) ──> Phase 14 (Anti-Cheat)
                                                            │
                                                            v
                                                   Phase 15 (Production)
```

**Key Insight**: Phases 1-3 can be implemented together following the PSK Architecture Plan. They provide the foundation for all subsequent phases.

---

## Related Documentation

### PSK Architecture Plan
**Location**: `PSK-ARCHITECTURE-PLAN.md`

Detailed design for transport-layer security using Pre-Shared Keys:
- HTTP authentication flow
- Orleans session grain design
- DTLS-PSK implementation with BouncyCastle
- Client integration
- Zone transition handling
- Performance targets

**Critical Files from PSK Plan**:
| File | Purpose |
|------|---------|
| `Shooter.Silo/Controllers/PlayerAuthController.cs` | HTTP auth endpoint |
| `Shooter.Shared/Grains/IPlayerSessionGrain.cs` | Session interface |
| `Shooter.Silo/Grains/PlayerSessionGrain.cs` | Session implementation |
| `Orleans.Rpc.Security/Transport/DtlsPskTransport.cs` | DTLS wrapper |
| `Orleans.Rpc.Security/Transport/BouncyCastle/DtlsPskSession.cs` | BC integration |

### Existing Security Documentation

| Document | Location | Purpose |
|----------|----------|---------|
| **Authorization Filter Plan** | `AUTHORIZATION-FILTER-PLAN.md` | **Detailed authorization design (Phases 4-7)** |
| **PSK How-To** | `PSK-SECURITY-GUIDE.md` | **Practical guide for implemented PSK security** |
| **DDoS/Resource Exhaustion Plan** | `DDOS-RESOURCE-EXHAUSTION-PLAN.md` | **Detailed DDoS mitigation design (Phases 8-9)** |
| Deserialization Safety Plan | `DESERIALIZATION-SAFETY-PLAN.md` | Type whitelisting design (Phase 10) |
| Threat Model | `THREAT-MODEL.md` | Risk analysis |
| Security Concerns | `SECURITY-CONCERNS.md` | Vulnerability catalog |
| Auth Design | `AUTHENTICATION-DESIGN.md` | Auth architecture |
| Authz Design | `AUTHORIZATION-DESIGN.md` | RBAC design |
| Serialization Guide | `SECURITY-SERIALIZATION-GUIDE.md` | Safe deserialization |
| Implementation | `IMPLEMENTATION-GUIDE.md` | Future/aspirational API examples |
| Tasks | `TASKS.md` | Detailed checklist |
| Shooter TODOs | `/granville/samples/Rpc/docs/SECURITY-TODO.md` | Demo app tasks |
| Status Report | `SECURITY-STATUS.md` | Detailed assessment |

---

## Progress Tracking

| Phase | Description | Status | Completion |
|-------|-------------|--------|------------|
| 1 | HTTP Auth & Session Grains | **Complete** | 100% |
| 2 | PSK Transport Layer | **Complete** | 100% |
| 3 | Security Mode Configuration | **Complete** | 100% |
| 4-7 | Authorization Filter ([plan](AUTHORIZATION-FILTER-PLAN.md)) | **Complete** | 100% |
| 8 | Rate Limiting (Per-IP) | Not Started | 0% |
| 9 | Rate Limiting (Per-User) | Not Started | 0% |
| 10 | Type Whitelisting | Not Started | 0% |
| 11 | Input Validation Framework | Not Started | 0% |
| 12 | Security Event Logging | Not Started | 0% |
| 13 | Session Management | Not Started | 0% |
| 14 | Anti-Cheat Foundation | Not Started | 0% |
| 15 | Production Hardening | Not Started | 0% |

**Overall Progress**: ~50% (4/8 major phase groups complete)

---

## Milestones

### Milestone 1: Local Development Safe (Phases 1-4)
- HTTP authentication working
- DTLS-PSK encrypting traffic (or UseNoSecurity for local)
- RPC calls have identity context
- Can test auth flow locally

### Milestone 2: Internet Alpha (Phases 1-9)
- All transport security implemented
- Authorization enforced
- Rate limiting active
- Safe for limited internet testing

### Milestone 3: Production Ready (All Phases)
- Full security stack implemented
- Hardening complete
- Pen testing passed
- Documentation complete

---

## Quick Start

Begin with **Phase 1** following the PSK Architecture Plan:

1. Create `IPlayerSessionGrain` interface
2. Implement `PlayerSessionGrain` in Shooter.Silo
3. Add `PlayerAuthController` HTTP endpoint
4. Modify client to get session_key from HTTP
5. Test guest login flow

See `PSK-ARCHITECTURE-PLAN.md` for detailed code examples and implementation guidance.

---

## Conclusion

This 15-phase roadmap provides a clear path from zero security to production-ready. The PSK Architecture Plan (Phases 1-3) provides transport-layer security, while Phases 4-15 add application-layer security features.

**Start with Phase 1**: HTTP Authentication & Session Grains.

**Next Action**: Follow PSK-ARCHITECTURE-PLAN.md to implement guest authentication with Orleans session grains.
