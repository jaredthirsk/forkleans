## ADDED Requirements

### Requirement: Security Context Flow

The system SHALL provide an `RpcSecurityContext` that flows authenticated user identity through async call chains using `AsyncLocal<T>`.

#### Scenario: Identity available in grain method
- **WHEN** an RPC request is processed after PSK authentication
- **THEN** `RpcSecurityContext.CurrentUser` returns the authenticated `RpcUserIdentity`
- **AND** `RpcSecurityContext.IsAuthenticated` returns true

#### Scenario: Context scoped to request
- **WHEN** multiple concurrent RPC requests are processed
- **THEN** each request has its own isolated security context
- **AND** context is cleared after request completes

### Requirement: User Identity Model

The system SHALL represent authenticated users with an `RpcUserIdentity` record containing UserId, UserName, Role, AuthenticatedAt, and ConnectionId.

#### Scenario: Identity populated from PSK session
- **WHEN** PSK handshake completes successfully
- **THEN** `RpcUserIdentity` is created with PlayerId as UserId
- **AND** PlayerName from session
- **AND** Role from session (Guest, User, Server, or Admin)

### Requirement: Role Hierarchy

The system SHALL enforce a role hierarchy where higher roles inherit permissions of lower roles: Anonymous < Guest < User < Server < Admin.

#### Scenario: Higher role satisfies lower requirement
- **WHEN** a method requires `[RequireRole(User)]`
- **AND** the caller has role Server or Admin
- **THEN** authorization succeeds

#### Scenario: Lower role fails higher requirement
- **WHEN** a method requires `[RequireRole(Admin)]`
- **AND** the caller has role User or Guest
- **THEN** authorization fails with PermissionDenied

### Requirement: Authorize Attribute

The system SHALL provide an `[Authorize]` attribute that requires the caller to be authenticated.

#### Scenario: Authenticated user allowed
- **WHEN** a method has `[Authorize]` attribute
- **AND** the caller has a valid PSK session
- **THEN** the method executes normally

#### Scenario: Anonymous user denied
- **WHEN** a method has `[Authorize]` attribute
- **AND** the caller has no PSK session (anonymous)
- **THEN** the request returns `RpcStatus.PermissionDenied`

#### Scenario: Interface-level authorization
- **WHEN** an interface has `[Authorize]` attribute
- **THEN** all methods on that interface require authentication

### Requirement: AllowAnonymous Attribute

The system SHALL provide an `[AllowAnonymous]` attribute that exempts a method from authentication requirements.

#### Scenario: Override interface authorization
- **WHEN** an interface has `[Authorize]` attribute
- **AND** a method has `[AllowAnonymous]` attribute
- **THEN** anonymous callers can invoke that specific method

### Requirement: RequireRole Attribute

The system SHALL provide a `[RequireRole(UserRole)]` attribute that requires the caller to have at least the specified role.

#### Scenario: Role requirement enforced
- **WHEN** a method has `[RequireRole(Admin)]`
- **AND** the caller has role User
- **THEN** the request returns `RpcStatus.PermissionDenied`

#### Scenario: Multiple roles OR semantics
- **WHEN** a method has multiple `[RequireRole]` attributes
- **THEN** the caller must satisfy at least one of them (OR logic)

### Requirement: ServerOnly Attribute

The system SHALL provide a `[ServerOnly]` attribute that restricts access to server-to-server calls (role >= Server).

#### Scenario: Server allowed
- **WHEN** a method has `[ServerOnly]` attribute
- **AND** the caller has role Server or Admin
- **THEN** the method executes normally

#### Scenario: Client denied
- **WHEN** a method has `[ServerOnly]` attribute
- **AND** the caller has role User or Guest
- **THEN** the request returns `RpcStatus.PermissionDenied`

### Requirement: ClientAccessible Attribute

The system SHALL provide a `[ClientAccessible]` attribute for grain interfaces that marks them as safe for client access when strict mode is enabled.

#### Scenario: Strict mode enforces attribute
- **WHEN** `RpcSecurityOptions.EnforceClientAccessible` is true
- **AND** a client calls a grain without `[ClientAccessible]`
- **THEN** the request returns `RpcStatus.PermissionDenied`

#### Scenario: Permissive mode ignores attribute
- **WHEN** `RpcSecurityOptions.EnforceClientAccessible` is false
- **THEN** clients can call any grain regardless of `[ClientAccessible]`

### Requirement: Authorization Filter Pipeline

The system SHALL provide an `IRpcAuthorizationFilter` interface for extensible authorization logic.

#### Scenario: Custom filter integration
- **WHEN** a custom `IRpcAuthorizationFilter` is registered
- **THEN** it is invoked before each RPC method execution
- **AND** can allow or deny the request

#### Scenario: Default filter handles attributes
- **WHEN** no custom filter is registered
- **THEN** `DefaultRpcAuthorizationFilter` processes authorization attributes

### Requirement: Authorization Logging

The system SHALL log authorization decisions for security auditing.

#### Scenario: Denied request logged
- **WHEN** authorization fails
- **THEN** a warning is logged with method name, user ID, required role, and denial reason

#### Scenario: Successful request optionally logged
- **WHEN** authorization succeeds
- **AND** verbose logging is enabled
- **THEN** a debug log records the allowed access
