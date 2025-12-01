# Change: Add DDoS Protection and Rate Limiting

## Why

Granville RPC currently has **zero protection** against denial-of-service attacks. Any client can send unlimited requests, exhaust server resources, and crash the system. This is rated **CRITICAL** in the threat model. Internet deployment requires multi-layered protection at transport, packet, request, and resource levels.

## What Changes

- **Transport Layer Protection**:
  - Connection rate limiting (max 10 connections/sec per IP)
  - IP blocklist/allowlist support
  - MaxConnections enforcement (currently defined but unused)
  - Connection timeout enforcement

- **Packet Layer Protection**:
  - Message size validation (max 64KB default)
  - Per-connection message rate limiting (1000 msg/min)
  - Deserialization timeout (100ms)

- **Request Layer Protection**:
  - Per-user rate limiting by PlayerId
  - Per-method rate limiting for expensive operations
  - Request queue management with backpressure
  - Role-based rate limit tiers (Guest: 100/min, User: 500/min, Admin: unlimited)

- **Resource Protection**:
  - Grain creation limits per client
  - Memory pressure detection
  - CPU time watchdog for long-running requests

## Impact

- **Affected specs**: New `rpc-rate-limiting` capability
- **Affected code**:
  - `/src/Rpc/Orleans.Rpc.Transport/` - Transport-level protection
  - `/src/Rpc/Orleans.Rpc.Server/` - Request-level rate limiting
  - `/src/Rpc/Orleans.Rpc.Security/` - Rate limiter implementations
  - `LiteNetLibTransport.cs` - Connection filtering
  - `RpcServer.cs` - Message rate limiting
- **Dependencies**: Benefits from authorization (Phase 4-7) for per-user limits
- **Breaking changes**: None - protection is additive

## References

- Detailed implementation plan: `/src/Rpc/docs/security/roadmap/DDOS-RESOURCE-EXHAUSTION-PLAN.md`
- Security roadmap Phases 8-9: `/src/Rpc/docs/security/roadmap/SECURITY-RECAP.md`
