# Granville RPC Security Implementation Roadmap

**Last Updated**: 2025-11-30
**Version**: 2.1

## Executive Summary

Granville RPC has comprehensive security **documentation** (~3,500 lines) but **zero implementation**. This document provides a 15-phase incremental roadmap to achieve internet-ready security.

**Current Status**: Not safe for internet deployment. Suitable for local development only.

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

| Threat | Likelihood | Impact | Risk |
|--------|------------|--------|------|
| DDoS Attacks | High | High | **CRITICAL** |
| Deserialization Exploits | High | Critical | **CRITICAL** |
| Authentication Bypass | Medium | High | High |
| MITM Attacks | Medium | High | High |
| Game State Manipulation | High | Medium | High |
| Resource Exhaustion | High | Medium | Medium |
| Session Hijacking | Medium | Medium | Medium |

---

## Implementation Roadmap: 15 Phases

### Phase 1: HTTP Authentication & Session Grains
**Priority**: CRITICAL | **Status**: Not Started
**Reference**: `PSK-ARCHITECTURE-PLAN.md` Phase 1

Implement HTTP guest authentication that generates session keys stored in Orleans grains.

**Deliverables**:
- [ ] `IPlayerSessionGrain` interface in `Shooter.Shared`
- [ ] `PlayerSessionGrain` implementation in `Shooter.Silo`
- [ ] `PlayerAuthController` HTTP endpoint for `/api/world/players/register`
- [ ] Session key generation (32 random bytes)
- [ ] `PlayerSession` record with PlayerId, PlayerName, Role, SessionKey, Expiry
- [ ] Return session_key in `PlayerRegistrationResponse`
- [ ] Unit tests for session grain

**Design Notes**:
- Guest mode: any username accepted, no password required
- Session key is 256-bit random, stored in Orleans grain memory
- Sessions expire after 4 hours (configurable)
- Same user can have multiple sessions (different devices)

**Success Criteria**: Guest users get session_key via HTTP

---

### Phase 2: DTLS-PSK Transport Layer
**Priority**: CRITICAL | **Status**: Not Started
**Reference**: `PSK-ARCHITECTURE-PLAN.md` Phase 2-3

Implement encrypted UDP using DTLS 1.2 with Pre-Shared Keys via BouncyCastle.

**Deliverables**:
- [ ] Add `BouncyCastle.Cryptography` NuGet package
- [ ] `DtlsPskTransport` class wrapping `IRpcTransport`
- [ ] `DtlsPskSession` class with BouncyCastle integration
- [ ] PSK lookup via `IPlayerSessionGrain.ValidateSessionKey()`
- [ ] DTLS handshake state machine
- [ ] Client-side DTLS-PSK configuration
- [ ] Integration tests for encrypted communication
- [ ] Performance benchmarks (target: <150ms handshake, <0.3ms encrypt/decrypt)

**Design Notes**:
- DTLS authenticates the *connection*, not each packet (zero per-packet overhead)
- After handshake, all traffic is AES-GCM encrypted
- Same session_key works across zone transitions (validated via Orleans)

**Location**: `/src/Rpc/Orleans.Rpc.Security/Transport/`

**Success Criteria**: Encrypted UDP communication with session key validation

---

### Phase 3: Security Mode Configuration
**Priority**: CRITICAL | **Status**: Not Started
**Reference**: `PSK-ARCHITECTURE-PLAN.md` Phase 5

Provide pluggable security modes for different deployment scenarios.

**Deliverables**:
- [ ] `UseNoSecurity()` extension for development
- [ ] `UseDtlsPsk()` extension for production (recommended)
- [ ] `UsePskAes()` extension for custom encryption (alternative)
- [ ] Security configuration validation on startup
- [ ] Warning logs when running without security
- [ ] Documentation for each mode

**Configuration API**:
```csharp
// Development
rpc.UseNoSecurity();

// Production (recommended)
rpc.UseDtlsPsk(options => {
    options.PskLookup = async (playerId) => /* Orleans grain lookup */;
});

// Alternative (simpler, custom)
rpc.UsePskAes(options => {
    options.PskLookup = /* same */;
});
```

**Success Criteria**: Three security modes available and configurable

---

### Phase 4: RPC Call Context Integration
**Priority**: CRITICAL | **Status**: Not Started

Wire authenticated identity into RPC call context so grain methods know who's calling.

**Deliverables**:
- [ ] `RpcUserContext` class (PlayerId, PlayerName, Role)
- [ ] `IRpcCallContext.User` property
- [ ] Extract `RpcUserContext` from DTLS session after handshake
- [ ] Flow context through RPC pipeline to grain method invocations
- [ ] `[AllowAnonymous]` attribute for methods that don't require auth
- [ ] Authentication failure returns `RpcStatus.Unauthenticated`
- [ ] Integration tests for context flow

**Design Notes**:
- DTLS handshake establishes identity at connection level
- `RpcUserContext` is populated once per connection, not per call
- Grains can access via `IRpcCallContext.User`

**Success Criteria**: Grain methods can access caller identity

---

### Phase 5: Basic Authorization Attributes
**Priority**: CRITICAL | **Status**: Not Started

Implement attribute-based authorization to control method access.

**Deliverables**:
- [ ] `[Authorize]` attribute (requires any authenticated user)
- [ ] `[RequireRole("role")]` attribute for role-based access
- [ ] `[RequireAnyRole("role1", "role2")]` attribute
- [ ] `[RequireAllRoles("role1", "role2")]` attribute
- [ ] `AuthorizationMiddleware` in RPC pipeline
- [ ] Authorization failure returns `RpcStatus.PermissionDenied`
- [ ] Unit tests for each attribute type

**Example Usage**:
```csharp
public interface IGameGrain : IGrainWithStringKey
{
    [AllowAnonymous]
    Task<ServerInfo> GetServerInfo();

    [Authorize]  // Any authenticated user
    Task<PlayerState> GetPlayerState();

    [RequireRole("admin")]
    Task KickPlayer(string playerId);
}
```

---

### Phase 6: Role System and Hierarchy
**Priority**: HIGH | **Status**: Not Started

Implement a proper role system with hierarchy for games and server components.

**Deliverables**:
- [ ] `UserRole` enum: Guest, User, Server, Admin (from PSK plan)
- [ ] `RoleDefinition` class with name and permissions
- [ ] `IRoleProvider` interface for custom role logic
- [ ] Role hierarchy (Admin > Server > User > Guest)
- [ ] Role assignment during HTTP authentication
- [ ] Role stored in `PlayerSession` and `RpcUserContext`
- [ ] Unit tests for role hierarchy checks

**Built-in Role Hierarchy**:
```
Admin
  └── Server (ActionServer-to-Silo)
        └── User (authenticated player)
              └── Guest (unauthenticated or guest login)
```

---

### Phase 7: Grain Access Control
**Priority**: HIGH | **Status**: Not Started

Control which grains can be created/accessed by which callers.

**Deliverables**:
- [ ] `[ClientCreatable]` attribute for client-instantiable grains
- [ ] `[ServerOnly]` attribute for infrastructure grains
- [ ] `GrainAccessRegistry` for runtime access checks
- [ ] Enforcement in grain activation pipeline
- [ ] Blocked grain access returns `RpcStatus.PermissionDenied`
- [ ] Configuration to list allowed grain types per role
- [ ] Integration tests for grain access control

**Example**:
```csharp
[ClientCreatable]  // Players can access
public interface IPlayerGrain : IGrainWithStringKey { }

[ServerOnly]  // Only ActionServers can access
public interface IWorldSimulationGrain : IGrainWithStringKey { }
```

---

### Phase 8: Rate Limiting (Per-IP)
**Priority**: HIGH | **Status**: Not Started

Basic DoS protection based on source IP address.

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

### Phase 9: Rate Limiting (Per-User)
**Priority**: HIGH | **Status**: Not Started

User-aware rate limiting (more effective than IP-based alone).

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

Prevent arbitrary type instantiation during deserialization to block RCE attacks.

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
| Threat Model | `THREAT-MODEL.md` | Risk analysis |
| Security Concerns | `SECURITY-CONCERNS.md` | Vulnerability catalog |
| Auth Design | `AUTHENTICATION-DESIGN.md` | Auth architecture |
| Authz Design | `AUTHORIZATION-DESIGN.md` | RBAC design |
| Serialization Guide | `SECURITY-SERIALIZATION-GUIDE.md` | Safe deserialization |
| Implementation | `IMPLEMENTATION-GUIDE.md` | How-to examples |
| Tasks | `TASKS.md` | Detailed checklist |
| Shooter TODOs | `/granville/samples/Rpc/docs/SECURITY-TODO.md` | Demo app tasks |
| Status Report | `SECURITY-STATUS.md` | Detailed assessment |

---

## Progress Tracking

| Phase | Description | Status | Completion |
|-------|-------------|--------|------------|
| 1 | HTTP Auth & Session Grains | Not Started | 0% |
| 2 | DTLS-PSK Transport | Not Started | 0% |
| 3 | Security Mode Configuration | Not Started | 0% |
| 4 | RPC Call Context Integration | Not Started | 0% |
| 5 | Basic Authorization Attributes | Not Started | 0% |
| 6 | Role System and Hierarchy | Not Started | 0% |
| 7 | Grain Access Control | Not Started | 0% |
| 8 | Rate Limiting (Per-IP) | Not Started | 0% |
| 9 | Rate Limiting (Per-User) | Not Started | 0% |
| 10 | Type Whitelisting | Not Started | 0% |
| 11 | Input Validation Framework | Not Started | 0% |
| 12 | Security Event Logging | Not Started | 0% |
| 13 | Session Management | Not Started | 0% |
| 14 | Anti-Cheat Foundation | Not Started | 0% |
| 15 | Production Hardening | Not Started | 0% |

**Overall Progress**: 0%

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
