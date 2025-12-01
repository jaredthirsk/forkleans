## 1. Core Types (Orleans.Rpc.Abstractions)

- [ ] 1.1 Create `Security/RpcUserIdentity.cs` with UserId, UserName, Role, AuthenticatedAt, ConnectionId
- [ ] 1.2 Create `Security/UserRole.cs` enum (Anonymous, Guest, User, Server, Admin)
- [ ] 1.3 Create `Security/Attributes/AuthorizeAttribute.cs`
- [ ] 1.4 Create `Security/Attributes/AllowAnonymousAttribute.cs`
- [ ] 1.5 Create `Security/Attributes/RequireRoleAttribute.cs`
- [ ] 1.6 Create `Security/Attributes/ServerOnlyAttribute.cs`
- [ ] 1.7 Create `Security/Attributes/ClientAccessibleAttribute.cs`
- [ ] 1.8 Add Orleans `[GenerateSerializer]` attributes to RpcUserIdentity and UserRole

## 2. Security Context (Orleans.Rpc.Server)

- [ ] 2.1 Create `Security/RpcSecurityContext.cs` with AsyncLocal storage
- [ ] 2.2 Implement `SetContext()` returning IDisposable scope
- [ ] 2.3 Implement `CurrentUser`, `ConnectionId`, `RemoteEndpoint`, `RequestId` properties
- [ ] 2.4 Implement `IsAuthenticated` convenience property

## 3. Authorization Filter Interface (Orleans.Rpc.Security)

- [ ] 3.1 Create `Filters/IRpcAuthorizationFilter.cs` interface
- [ ] 3.2 Create `Filters/RpcAuthorizationContext.cs` with method metadata, user, grain type
- [ ] 3.3 Create `Filters/AuthorizationResult.cs` (Allowed, Denied with reason)

## 4. Default Authorization Filter (Orleans.Rpc.Security)

- [ ] 4.1 Create `Filters/DefaultRpcAuthorizationFilter.cs`
- [ ] 4.2 Implement attribute scanning with caching (MethodInfo → attributes)
- [ ] 4.3 Implement `[AllowAnonymous]` check (highest priority, allows)
- [ ] 4.4 Implement `[Authorize]` check (requires IsAuthenticated)
- [ ] 4.5 Implement `[RequireRole]` check with hierarchy comparison
- [ ] 4.6 Implement `[ServerOnly]` check (RequireRole(Server) equivalent)
- [ ] 4.7 Implement `[ClientAccessible]` grain-level check for strict mode

## 5. Integration with RpcConnection

- [ ] 5.1 Modify `RpcConnection` to extract identity from `PskSession` after handshake
- [ ] 5.2 Create `RpcUserIdentity` from session data (PlayerId, PlayerName, Role)
- [ ] 5.3 Call `RpcSecurityContext.SetContext()` before processing each request
- [ ] 5.4 Invoke `IRpcAuthorizationFilter.AuthorizeAsync()` before method dispatch
- [ ] 5.5 Return `RpcStatus.PermissionDenied` if authorization fails
- [ ] 5.6 Dispose security context scope after request completes

## 6. DI Registration (Orleans.Rpc.Security)

- [ ] 6.1 Create `RpcSecurityOptions.cs` with EnforceClientAccessible, DefaultPolicy settings
- [ ] 6.2 Create `AddRpcAuthorization()` extension method for IServiceCollection
- [ ] 6.3 Create `AddRpcAuthorizationDevelopment()` with permissive defaults
- [ ] 6.4 Create `AddRpcAuthorizationProduction()` with strict defaults
- [ ] 6.5 Register `DefaultRpcAuthorizationFilter` as default `IRpcAuthorizationFilter`

## 7. Shooter Sample Integration

- [ ] 7.1 Add `[Authorize]` to game grain interfaces (IPlayerGrain, IWorldGrain, etc.)
- [ ] 7.2 Add `[ClientAccessible]` to client-facing grains
- [ ] 7.3 Add `[ServerOnly]` to internal infrastructure grains
- [ ] 7.4 Add `[AllowAnonymous]` to public info endpoints if any
- [ ] 7.5 Wire up `AddRpcAuthorization()` in ActionServer startup
- [ ] 7.6 Test authorization flow with guest and server roles

## 8. Testing

- [ ] 8.1 Unit tests for `RpcSecurityContext` (set/get/clear, async flow)
- [ ] 8.2 Unit tests for `DefaultRpcAuthorizationFilter` (each attribute type)
- [ ] 8.3 Unit tests for role hierarchy (User >= Guest, Server >= User, etc.)
- [ ] 8.4 Integration test: authenticated user can call [Authorize] method
- [ ] 8.5 Integration test: anonymous user rejected from [Authorize] method
- [ ] 8.6 Integration test: Guest user rejected from [RequireRole(Admin)] method
- [ ] 8.7 Integration test: [AllowAnonymous] overrides [Authorize] on interface

## 9. Documentation

- [ ] 9.1 Update PSK-SECURITY-HOWTO.md with authorization examples
- [ ] 9.2 Add XML doc comments to all public types
- [ ] 9.3 Update SECURITY-RECAP.md to mark Phases 4-7 complete
