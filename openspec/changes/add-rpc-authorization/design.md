## Context

Granville RPC needs method-level authorization to complement the existing PSK transport authentication. The PSK layer ensures only authenticated users can connect, but doesn't control which methods they can call. This design adds Orleans-style grain call filters for fine-grained access control.

**Stakeholders**: Game developers using Granville RPC, security auditors, operations teams

**Constraints**:
- Must integrate with existing PSK transport without breaking changes
- Must follow Orleans patterns (familiar to Orleans developers)
- Must have minimal performance overhead for the common (authorized) path
- Must support both development (permissive) and production (strict) modes

## Goals / Non-Goals

**Goals**:
- Flow authenticated identity through async call chains via AsyncLocal
- Provide declarative attribute-based authorization
- Support role hierarchy (Anonymous < Guest < User < Server < Admin)
- Enable custom authorization logic via filter interface
- Log authorization decisions for security auditing

**Non-Goals**:
- Claims-based authorization (future enhancement)
- OAuth/OIDC integration (out of scope - PSK is the auth mechanism)
- Per-grain instance authorization (per-grain-type only)

## Decisions

### Decision 1: Use AsyncLocal for Security Context

**What**: Use `AsyncLocal<T>` to flow `RpcSecurityContext` through async call chains.

**Why**: This mirrors Orleans' `RequestContext` pattern, ensuring identity is available in grain methods without explicit parameter passing. It's thread-safe and works correctly with async/await.

**Alternatives considered**:
- Pass identity as method parameter → Breaks existing interfaces, verbose
- Store in `HttpContext` → Not available in UDP path
- Orleans `RequestContext` directly → Tight coupling, may conflict with Orleans usage

### Decision 2: Role Hierarchy with Numeric Comparison

**What**: `UserRole` is an enum where higher values have more permissions. Authorization uses `>=` comparison.

**Why**: Simple, fast comparison. `[RequireRole(User)]` automatically allows User, Server, and Admin. Matches common game server patterns.

**Alternatives considered**:
- Claim sets → More flexible but complex, overkill for game scenarios
- Separate permission flags → Harder to reason about, more configuration

### Decision 3: Attribute Inheritance with Override

**What**: `[Authorize]` on interface applies to all methods; `[AllowAnonymous]` on method overrides.

**Why**: Reduces boilerplate. Most grains need auth on all methods. Explicit opt-out for exceptions.

### Decision 4: Filter Pipeline (IRpcAuthorizationFilter)

**What**: Authorization runs through `IRpcAuthorizationFilter` pipeline before method invocation.

**Why**: Extensible. Default filter handles attributes; custom filters can add logging, custom logic, or external authorization systems.

## Risks / Trade-offs

| Risk | Mitigation |
|------|------------|
| Performance overhead from reflection on every call | Cache attribute metadata per method at startup |
| Forgetting to add attributes exposes methods | Strict mode requires explicit `[ClientAccessible]` on client-facing grains |
| AsyncLocal doesn't flow to Orleans grains | Document: use Orleans `RequestContext` for cross-silo calls |

## Migration Plan

1. **Phase 1**: Add types to Abstractions package (no behavior change)
2. **Phase 2**: Add filter infrastructure to Security package
3. **Phase 3**: Integrate into RpcConnection/RpcServer (opt-in via DI)
4. **Phase 4**: Update Shooter sample with authorization attributes
5. **Rollback**: Remove DI registration to disable; no code changes needed

## Open Questions

- Should we support `[RequireRole]` with multiple roles (OR semantics)? → Yes, AllowMultiple=true
- Should authorization failures return 403 or disconnect? → Return `RpcStatus.PermissionDenied`, keep connection
