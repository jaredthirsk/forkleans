## 1. Core Types (Orleans.Rpc.Abstractions)

- [x] 1.1 Create `Security/RpcUserIdentity.cs` with UserId, UserName, Role, AuthenticatedAt, ConnectionId
- [x] 1.2 Create `Security/UserRole.cs` enum (Anonymous, Guest, User, Server, Admin)
- [x] 1.3 Create `Security/Attributes/AuthorizeAttribute.cs`
- [x] 1.4 Create `Security/Attributes/AllowAnonymousAttribute.cs`
- [x] 1.5 Create `Security/Attributes/RequireRoleAttribute.cs`
- [x] 1.6 Create `Security/Attributes/ServerOnlyAttribute.cs`
- [x] 1.7 Create `Security/Attributes/ClientAccessibleAttribute.cs`
- [x] 1.8 Add Orleans `[GenerateSerializer]` attributes to RpcUserIdentity and UserRole

## 2. Security Context (Orleans.Rpc.Security)

- [x] 2.1 Create `Authorization/RpcSecurityContext.cs` with AsyncLocal storage
- [x] 2.2 Implement `SetContext()` returning IDisposable scope
- [x] 2.3 Implement `CurrentUser`, `ConnectionId`, `RemoteEndpoint`, `RequestId` properties
- [x] 2.4 Implement `IsAuthenticated` convenience property

## 3. Authorization Filter Interface (Orleans.Rpc.Security)

- [x] 3.1 Create `Authorization/IRpcAuthorizationFilter.cs` interface
- [x] 3.2 Create `Authorization/RpcAuthorizationContext.cs` with method metadata, user, grain type
- [x] 3.3 Create `Authorization/AuthorizationResult.cs` (Allowed, Denied with reason)

## 4. Default Authorization Filter (Orleans.Rpc.Security)

- [x] 4.1 Create `Authorization/DefaultRpcAuthorizationFilter.cs`
- [x] 4.2 Implement attribute scanning via RpcAuthorizationContext helper methods
- [x] 4.3 Implement `[AllowAnonymous]` check (highest priority, allows)
- [x] 4.4 Implement `[Authorize]` check (requires IsAuthenticated)
- [x] 4.5 Implement `[RequireRole]` check with hierarchy comparison
- [x] 4.6 Implement `[ServerOnly]` check (RequireRole(Server) equivalent)
- [x] 4.7 Implement `[ClientAccessible]` grain-level check for strict mode

## 5. Integration with RpcConnection

- [x] 5.1 Modify `RpcConnection` to accept authorization filter and connection user accessor
- [x] 5.2 Create `IConnectionUserAccessor` interface for retrieving user from connection
- [x] 5.3 Call `RpcSecurityContext.SetContext()` before processing each request
- [x] 5.4 Invoke `IRpcAuthorizationFilter.AuthorizeAsync()` before method dispatch
- [x] 5.5 Return error response with "Authorization denied" if authorization fails
- [x] 5.6 Dispose security context scope after request completes
- [x] 5.7 Update `PskEncryptedTransport` to implement `IConnectionUserAccessor`
- [x] 5.8 Add `PskLookupWithIdentity` callback to `DtlsPskOptions`
- [x] 5.9 Update `PskSession` to store `AuthenticatedUser`

## 6. DI Registration (Orleans.Rpc.Security)

- [x] 6.1 Create `Configuration/RpcSecurityOptions.cs` with EnforceClientAccessible, DefaultPolicy settings
- [x] 6.2 Create `AddRpcAuthorization()` extension method for IServiceCollection
- [x] 6.3 Create `AddRpcAuthorizationDevelopment()` with permissive defaults
- [x] 6.4 Create `AddRpcAuthorizationProduction()` with strict defaults
- [x] 6.5 Register `DefaultRpcAuthorizationFilter` as default `IRpcAuthorizationFilter`
- [x] 6.6 Create `AddRpcAuthorizationDisabled()` for disabling authorization

## 7. Shooter Sample Integration

- [x] 7.1 Add `[Authorize]` to game grain interfaces (IPlayerGrain, IWorldManagerGrain, IStatsCollectorGrain)
- [x] 7.2 Add `[ClientAccessible]` to client-facing grain methods
- [x] 7.3 Add `[ServerOnly]` to internal infrastructure grains (IPlayerSessionGrain)
- [x] 7.4 Add `[ServerOnly]` to server-only methods (TakeDamage, UpdateHealth, etc.)
- [x] 7.5 Wire up `AddRpcAuthorization()` in ActionServer startup
- [ ] 7.6 Test authorization flow with guest and server roles (pending - requires test run)

## 8. Testing

- [x] 8.1 Create test project `Orleans.Rpc.Security.Tests`
- [x] 8.2 Unit tests for `DefaultRpcAuthorizationFilter` (each attribute type)
- [x] 8.3 Unit tests for role hierarchy (User >= Guest, Server >= User, etc.)
- [x] 8.4 Unit test: authenticated user can call [Authorize] method
- [x] 8.5 Unit test: anonymous user rejected from [Authorize] method
- [x] 8.6 Unit test: Guest user rejected from [RequireRole(User)] method
- [x] 8.7 Unit test: [AllowAnonymous] overrides [Authorize] on interface
- [x] 8.8 Unit test: [ClientAccessible] enforcement with strict mode
- [ ] 8.9 Integration test with RpcConnection (pending - requires build)

## 9. Documentation

- [x] 9.1 Add XML doc comments to all public types
- [x] 9.2 Update tasks.md to mark completed items
- [x] 9.3 Update PSK-SECURITY-GUIDE.md with authorization examples
- [x] 9.4 Update SECURITY-RECAP.md to mark Phases 4-7 complete
