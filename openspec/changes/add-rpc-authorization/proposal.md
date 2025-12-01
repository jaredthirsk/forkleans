# Change: Add RPC Authorization Filter

## Why

The PSK transport layer (already implemented) blocks unauthenticated connections at the network level. However, there is no method-level authorization - any authenticated user can call any RPC method. This is a **CRITICAL** security gap for internet deployment where different users need different permissions (e.g., guests vs admins, clients vs server-to-server calls).

## What Changes

- **RpcSecurityContext**: AsyncLocal-based context that flows authenticated user identity through async call chains, similar to Orleans' `RequestContext`
- **RpcUserIdentity**: Immutable record representing the authenticated user (UserId, UserName, Role, AuthenticatedAt)
- **UserRole enum**: Role hierarchy (Anonymous < Guest < User < Server < Admin)
- **Authorization attributes**:
  - `[Authorize]` - Require authenticated user
  - `[AllowAnonymous]` - Exempt specific methods from auth
  - `[RequireRole(role)]` - Role-based access control with hierarchy
  - `[ServerOnly]` - Restrict to server-to-server calls
  - `[ClientAccessible]` - Mark grains as client-creatable (strict mode)
- **IRpcAuthorizationFilter**: Extensible pipeline for custom authorization logic
- **DefaultRpcAuthorizationFilter**: Attribute-based authorization implementation
- **DI integration**: `AddRpcAuthorization()` extension method

## Impact

- **Affected specs**: New `rpc-authorization` capability
- **Affected code**:
  - `/src/Rpc/Orleans.Rpc.Abstractions/Security/` - New types
  - `/src/Rpc/Orleans.Rpc.Security/Filters/` - Filter implementation
  - `/src/Rpc/Orleans.Rpc.Server/RpcConnection.cs` - Integration point
  - `/src/Rpc/Orleans.Rpc.Server/RpcServer.cs` - Context setup
  - `/granville/samples/Rpc/` - Shooter sample integration
- **Dependencies**: Requires PSK transport (already complete)
- **Breaking changes**: None - opt-in via attributes

## References

- Detailed implementation plan: `/src/Rpc/docs/security/roadmap/AUTHORIZATION-FILTER-PLAN.md`
- Security roadmap Phases 4-7: `/src/Rpc/docs/security/roadmap/SECURITY-RECAP.md`
