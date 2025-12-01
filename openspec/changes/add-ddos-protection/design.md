## Context

Granville RPC exposes UDP endpoints to the internet for real-time game communication. Without rate limiting, attackers can overwhelm servers with connection floods, packet storms, or resource exhaustion attacks. This design adds defense-in-depth protection at multiple layers.

**Stakeholders**: Operations teams, game developers, security auditors

**Constraints**:
- Must not add latency to legitimate traffic (fast path optimization)
- Must handle burst traffic gracefully (games have natural traffic spikes)
- Must provide clear metrics for monitoring and alerting
- Must be configurable per deployment scenario

## Goals / Non-Goals

**Goals**:
- Block malicious traffic before it consumes significant resources
- Gracefully degrade under load rather than crash
- Provide visibility into attack patterns via metrics
- Support different rate limit tiers by user role
- Allow configuration without code changes

**Non-Goals**:
- External DDoS mitigation (CDN/WAF) - out of scope, complementary
- Geographic IP filtering - external concern
- Bot detection - future enhancement

## Decisions

### Decision 1: Four-Layer Defense Architecture

**What**: Protect at Transport → Packet → Request → Resource layers.

**Why**: Each layer catches different attack types earlier in the pipeline, minimizing resource consumption. Connection flooding is blocked before handshake; packet flooding before deserialization; request flooding before grain invocation.

### Decision 2: Sliding Window Rate Limiter

**What**: Use sliding window algorithm for rate limiting rather than fixed windows.

**Why**: Fixed windows have boundary problems (2x burst at window edges). Sliding window provides smooth limiting. Implementation uses token bucket approximation for O(1) space.

**Alternatives considered**:
- Fixed window → Boundary burst problem
- Leaky bucket → More complex, similar results
- Token bucket → Equivalent to sliding window for our use case

### Decision 3: Role-Based Rate Tiers

**What**: Different rate limits by UserRole (Guest < User < Server < Admin).

**Why**: Legitimate power users need higher limits. Servers need unlimited for internal communication. Guests (potentially unauthenticated) get strictest limits.

### Decision 4: Temporary vs Permanent Blocklists

**What**: Support both temporary blocks (15 min for rate limit violations) and permanent blocks (known bad IPs).

**Why**: Temporary blocks handle transient attackers without manual intervention. Permanent blocks for known threats loaded from configuration.

## Risks / Trade-offs

| Risk | Mitigation |
|------|------------|
| False positives block legitimate users | Generous burst allowance, clear error messages, easy unblock |
| Attackers bypass IP limits with botnets | Per-user limits after authentication layer |
| Rate limiter state grows unbounded | LRU eviction, periodic cleanup of expired entries |
| Clock skew affects distributed rate limiting | Single-server rate limiting acceptable; distributed uses Orleans grain |

## Migration Plan

1. **Phase 1**: Add rate limiter infrastructure (no enforcement)
2. **Phase 2**: Enable transport-layer protection with permissive defaults
3. **Phase 3**: Enable packet-layer protection
4. **Phase 4**: Enable request-layer protection with role-based tiers
5. **Rollback**: Configuration flag to disable each layer independently

## Open Questions

- Should rate limit state be persisted across restarts? → No, fresh start is acceptable
- Should we expose rate limit headers in responses? → Yes, X-RateLimit-Remaining header
