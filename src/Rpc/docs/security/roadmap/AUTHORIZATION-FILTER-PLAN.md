# RPC Authorization Filter Implementation Plan

**Document Version**: 1.0
**Created**: 2025-11-30
**Status**: Planning
**Priority**: HIGH (Addresses "Authentication Bypass" threat)

## Executive Summary

This plan implements **Orleans-style Grain Call Filters** for Granville RPC authorization. It builds on the existing PSK transport authentication (already implemented) to add fine-grained, attribute-based access control.

### What's Already Done (PSK Transport)

The `PskEncryptedTransport` in `/src/Rpc/Orleans.Rpc.Security/Transport/` already provides:
- ✅ Challenge-response handshake with PSK lookup via Orleans grain
- ✅ AES-256-GCM encryption after handshake
- ✅ Replay attack protection via sequence numbers
- ✅ **Connection rejected if PSK validation fails** (blocks unauthenticated clients)

### What This Plan Adds

| Feature | Description |
|---------|-------------|
| `[Authorize]` attribute | Require authenticated user |
| `[AllowAnonymous]` attribute | Exempt specific methods from auth |
| `[RequireRole(role)]` attribute | Role-based access control |
| `[ServerOnly]` attribute | Restrict to server-to-server calls |
| `RpcSecurityContext` | Flow identity through async call chain |
| `IRpcAuthorizationFilter` | Extensible authorization pipeline |
| Security logging | Audit trail for authorization decisions |

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    ALREADY IMPLEMENTED (PSK Transport)                   │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  UDP Packet → PskEncryptedTransport → Handshake → Encrypted Channel     │
│                     ↓                      ↓                             │
│              PSK Lookup via          Session established                 │
│              Orleans Grain           with Identity                       │
│                                                                          │
└──────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────┐
│                    THIS PLAN (Authorization Filter)                      │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  RpcConnection.ProcessRequestAsync()                                     │
│       ↓                                                                  │
│  [1] Extract identity from PskSession → RpcUserIdentity                 │
│       ↓                                                                  │
│  [2] Set RpcSecurityContext (AsyncLocal<T>)                             │
│       ↓                                                                  │
│  [3] IRpcAuthorizationFilter.AuthorizeAsync()                           │
│       │                                                                  │
│       ├─ Check [AllowAnonymous] → Allow                                 │
│       ├─ Check [Authorize] → Require user                               │
│       ├─ Check [RequireRole] → Check user.Role                          │
│       └─ Check [ServerOnly] → Check user.Role >= Server                 │
│       ↓                                                                  │
│  [4] If denied → Return RpcStatus.PermissionDenied                      │
│       ↓                                                                  │
│  [5] If allowed → InvokeGrainMethodAsync()                              │
│       ↓                                                                  │
│  Grain method can access RpcSecurityContext.CurrentUser                 │
│                                                                          │
└──────────────────────────────────────────────────────────────────────────┘
```

---

## Detailed Implementation Checklist

### Phase 1: Core Types and Interfaces

#### 1.1 Create RpcUserIdentity Record

**File**: `/src/Rpc/Orleans.Rpc.Abstractions/Security/RpcUserIdentity.cs`

```csharp
// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

using Orleans;

namespace Granville.Rpc.Security;

/// <summary>
/// Represents an authenticated user identity in Granville RPC.
/// Populated from the PSK session after successful handshake.
/// </summary>
[GenerateSerializer]
[Immutable]
public sealed record RpcUserIdentity
{
    /// <summary>
    /// The unique player/user identifier (from PSK identity).
    /// </summary>
    [Id(0)]
    public required string UserId { get; init; }

    /// <summary>
    /// The user's display name.
    /// </summary>
    [Id(1)]
    public required string UserName { get; init; }

    /// <summary>
    /// The user's role for authorization decisions.
    /// </summary>
    [Id(2)]
    public UserRole Role { get; init; } = UserRole.Guest;

    /// <summary>
    /// When the user was authenticated (handshake completion time).
    /// </summary>
    [Id(3)]
    public DateTime AuthenticatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// The connection ID associated with this identity.
    /// </summary>
    [Id(4)]
    public string? ConnectionId { get; init; }
}

/// <summary>
/// User roles for authorization in Granville RPC.
/// Higher numeric values have more permissions.
/// </summary>
[GenerateSerializer]
public enum UserRole : byte
{
    /// <summary>
    /// Unauthenticated or anonymous user (no PSK session).
    /// </summary>
    Anonymous = 0,

    /// <summary>
    /// Guest user with minimal permissions.
    /// </summary>
    Guest = 1,

    /// <summary>
    /// Authenticated regular user/player.
    /// </summary>
    User = 2,

    /// <summary>
    /// Server component (ActionServer, internal services).
    /// </summary>
    Server = 3,

    /// <summary>
    /// Administrator with full permissions.
    /// </summary>
    Admin = 4
}
```

**Checklist**:
- [ ] Create file `/src/Rpc/Orleans.Rpc.Abstractions/Security/RpcUserIdentity.cs`
- [ ] Add `[GenerateSerializer]` and `[Immutable]` attributes
- [ ] Add `[Id(n)]` to all properties for Orleans serialization
- [ ] Include `UserId`, `UserName`, `Role`, `AuthenticatedAt`, `ConnectionId`
- [ ] Create `UserRole` enum with `Anonymous`, `Guest`, `User`, `Server`, `Admin`
- [ ] Document each member with XML comments

---

#### 1.2 Create Authorization Attributes

**File**: `/src/Rpc/Orleans.Rpc.Abstractions/Security/AuthorizationAttributes.cs`

```csharp
// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

namespace Granville.Rpc.Security;

/// <summary>
/// Requires the caller to be authenticated (have a valid PSK session).
/// Can be applied to methods, interfaces, or classes.
/// </summary>
/// <remarks>
/// When applied to an interface or class, all methods require authentication
/// unless overridden with [AllowAnonymous].
/// </remarks>
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Interface | AttributeTargets.Class,
    Inherited = true,
    AllowMultiple = false)]
public sealed class AuthorizeAttribute : Attribute
{
}

/// <summary>
/// Allows anonymous (unauthenticated) access to a method.
/// Overrides [Authorize] on the containing interface or class.
/// </summary>
/// <remarks>
/// Use sparingly - only for methods that genuinely need public access
/// like server info endpoints.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Method,
    Inherited = true,
    AllowMultiple = false)]
public sealed class AllowAnonymousAttribute : Attribute
{
}

/// <summary>
/// Requires the caller to have at least the specified role.
/// Multiple [RequireRole] attributes on a method are OR'd together.
/// </summary>
/// <remarks>
/// Role comparison uses >= semantics, so [RequireRole(User)] allows
/// User, Server, and Admin roles.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Interface | AttributeTargets.Class,
    Inherited = true,
    AllowMultiple = true)]
public sealed class RequireRoleAttribute : Attribute
{
    /// <summary>
    /// The minimum required role.
    /// </summary>
    public UserRole Role { get; }

    /// <summary>
    /// Creates a new RequireRoleAttribute.
    /// </summary>
    /// <param name="role">The minimum required role.</param>
    public RequireRoleAttribute(UserRole role)
    {
        Role = role;
    }
}

/// <summary>
/// Restricts access to server-to-server calls only.
/// Clients (User, Guest roles) cannot call methods with this attribute.
/// </summary>
/// <remarks>
/// Equivalent to [RequireRole(UserRole.Server)] but more explicit
/// about intent. Use for internal infrastructure grains.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Interface | AttributeTargets.Class,
    Inherited = true,
    AllowMultiple = false)]
public sealed class ServerOnlyAttribute : Attribute
{
}

/// <summary>
/// Marks a grain interface as creatable/accessible by clients.
/// Grains without this attribute can only be accessed by servers
/// when strict mode is enabled.
/// </summary>
/// <remarks>
/// This is a safety measure to prevent clients from accessing
/// internal infrastructure grains. Only effective when
/// RpcSecurityOptions.EnforceClientCreatableAttribute is true.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Interface,
    Inherited = false,
    AllowMultiple = false)]
public sealed class ClientAccessibleAttribute : Attribute
{
}
```

**Checklist**:
- [ ] Create file `/src/Rpc/Orleans.Rpc.Abstractions/Security/AuthorizationAttributes.cs`
- [ ] Implement `[Authorize]` attribute
  - [ ] Targets: Method, Interface, Class
  - [ ] Inherited = true
  - [ ] Document behavior with XML comments
- [ ] Implement `[AllowAnonymous]` attribute
  - [ ] Targets: Method only
  - [ ] Document that it overrides [Authorize]
- [ ] Implement `[RequireRole(UserRole)]` attribute
  - [ ] Targets: Method, Interface, Class
  - [ ] AllowMultiple = true (OR semantics)
  - [ ] Store Role property
- [ ] Implement `[ServerOnly]` attribute
  - [ ] Targets: Method, Interface, Class
  - [ ] Document equivalence to RequireRole(Server)
- [ ] Implement `[ClientAccessible]` attribute
  - [ ] Targets: Interface only
  - [ ] Document strict mode behavior

---

#### 1.3 Create RpcSecurityContext (AsyncLocal)

**File**: `/src/Rpc/Orleans.Rpc.Server/Security/RpcSecurityContext.cs`

```csharp
// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

using System.Net;
using Granville.Rpc.Security;

namespace Granville.Rpc.Server.Security;

/// <summary>
/// Provides access to security context for the current RPC request.
/// Uses AsyncLocal&lt;T&gt; to flow through async call chains, similar to
/// Orleans' RequestContext.
/// </summary>
/// <remarks>
/// This context is set by RpcConnection before processing each request
/// and is available to grain methods and authorization filters.
/// </remarks>
public static class RpcSecurityContext
{
    private static readonly AsyncLocal<SecurityContextData?> _current = new();

    /// <summary>
    /// Gets the authenticated user for the current request.
    /// Returns null if no user is authenticated (anonymous request or
    /// PSK transport not enabled).
    /// </summary>
    public static RpcUserIdentity? CurrentUser => _current.Value?.User;

    /// <summary>
    /// Gets the connection ID for the current request.
    /// </summary>
    public static string? ConnectionId => _current.Value?.ConnectionId;

    /// <summary>
    /// Gets the remote endpoint for the current request.
    /// </summary>
    public static IPEndPoint? RemoteEndpoint => _current.Value?.RemoteEndpoint;

    /// <summary>
    /// Gets the request ID for correlation/logging.
    /// </summary>
    public static Guid RequestId => _current.Value?.RequestId ?? Guid.Empty;

    /// <summary>
    /// Returns true if the current request has an authenticated user.
    /// </summary>
    public static bool IsAuthenticated => _current.Value?.User != null;

    /// <summary>
    /// Sets the security context for the duration of request processing.
    /// Returns an IDisposable that restores the previous context when disposed.
    /// </summary>
    /// <param name="user">The authenticated user, or null for anonymous.</param>
    /// <param name="connectionId">The connection ID.</param>
    /// <param name="remoteEndpoint">The remote endpoint.</param>
    /// <param name="requestId">Optional request ID for correlation.</param>
    /// <returns>A disposable scope that restores the previous context.</returns>
    public static IDisposable SetContext(
        RpcUserIdentity? user,
        string connectionId,
        IPEndPoint remoteEndpoint,
        Guid? requestId = null)
    {
        var previous = _current.Value;
        _current.Value = new SecurityContextData
        {
            User = user,
            ConnectionId = connectionId,
            RemoteEndpoint = remoteEndpoint,
            RequestId = requestId ?? Guid.NewGuid()
        };
        return new ContextScope(previous);
    }

    /// <summary>
    /// Clears the current security context.
    /// </summary>
    internal static void Clear()
    {
        _current.Value = null;
    }

    private sealed class SecurityContextData
    {
        public RpcUserIdentity? User { get; init; }
        public string? ConnectionId { get; init; }
        public IPEndPoint? RemoteEndpoint { get; init; }
        public Guid RequestId { get; init; }
    }

    private sealed class ContextScope : IDisposable
    {
        private readonly SecurityContextData? _previous;
        private bool _disposed;

        public ContextScope(SecurityContextData? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _current.Value = _previous;
        }
    }
}
```

**Checklist**:
- [ ] Create file `/src/Rpc/Orleans.Rpc.Server/Security/RpcSecurityContext.cs`
- [ ] Use `AsyncLocal<T>` for thread-safe context storage
- [ ] Implement `CurrentUser` property (returns `RpcUserIdentity?`)
- [ ] Implement `ConnectionId` property
- [ ] Implement `RemoteEndpoint` property
- [ ] Implement `RequestId` property for correlation
- [ ] Implement `IsAuthenticated` convenience property
- [ ] Implement `SetContext()` method returning `IDisposable`
- [ ] Implement `Clear()` internal method
- [ ] Create private `SecurityContextData` class
- [ ] Create private `ContextScope` class for disposal pattern
- [ ] Add XML documentation explaining similarity to Orleans RequestContext

---

### Phase 2: Authorization Filter Infrastructure

#### 2.1 Create Authorization Result Types

**File**: `/src/Rpc/Orleans.Rpc.Server/Security/AuthorizationResult.cs`

```csharp
// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

namespace Granville.Rpc.Server.Security;

/// <summary>
/// Result of an authorization check.
/// </summary>
public sealed record AuthorizationResult
{
    /// <summary>
    /// Whether the request is authorized.
    /// </summary>
    public bool IsAuthorized { get; init; }

    /// <summary>
    /// Reason for denial (null if authorized).
    /// </summary>
    public string? FailureReason { get; init; }

    /// <summary>
    /// The authorization rule that caused the decision.
    /// </summary>
    public string? DecidingRule { get; init; }

    /// <summary>
    /// Creates a successful authorization result.
    /// </summary>
    public static AuthorizationResult Success(string? rule = null) =>
        new() { IsAuthorized = true, DecidingRule = rule };

    /// <summary>
    /// Creates a failed authorization result.
    /// </summary>
    public static AuthorizationResult Fail(string reason, string? rule = null) =>
        new() { IsAuthorized = false, FailureReason = reason, DecidingRule = rule };

    /// <summary>
    /// Authentication required but user is anonymous.
    /// </summary>
    public static AuthorizationResult Unauthenticated() =>
        Fail("Authentication required", "[Authorize]");

    /// <summary>
    /// User doesn't have required role.
    /// </summary>
    public static AuthorizationResult InsufficientRole(UserRole required, UserRole actual) =>
        Fail($"Role '{required}' required, user has '{actual}'", $"[RequireRole({required})]");

    /// <summary>
    /// Server-only method called by non-server.
    /// </summary>
    public static AuthorizationResult ServerOnly() =>
        Fail("This method is restricted to server components", "[ServerOnly]");

    /// <summary>
    /// Grain not accessible to clients.
    /// </summary>
    public static AuthorizationResult GrainNotClientAccessible(string grainType) =>
        Fail($"Grain '{grainType}' is not accessible to clients", "[ClientAccessible] missing");
}
```

**Checklist**:
- [ ] Create file `/src/Rpc/Orleans.Rpc.Server/Security/AuthorizationResult.cs`
- [ ] Implement as sealed record
- [ ] Add `IsAuthorized`, `FailureReason`, `DecidingRule` properties
- [ ] Implement static factory methods:
  - [ ] `Success(rule)`
  - [ ] `Fail(reason, rule)`
  - [ ] `Unauthenticated()`
  - [ ] `InsufficientRole(required, actual)`
  - [ ] `ServerOnly()`
  - [ ] `GrainNotClientAccessible(grainType)`

---

#### 2.2 Create Authorization Context

**File**: `/src/Rpc/Orleans.Rpc.Server/Security/RpcAuthorizationContext.cs`

```csharp
// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

using System.Net;
using System.Reflection;
using Granville.Rpc.Security;
using Orleans.Runtime;

namespace Granville.Rpc.Server.Security;

/// <summary>
/// Context passed to authorization filters containing all information
/// needed to make an authorization decision.
/// </summary>
public sealed class RpcAuthorizationContext
{
    /// <summary>
    /// The grain interface being called.
    /// </summary>
    public required Type GrainInterface { get; init; }

    /// <summary>
    /// The interface method being called.
    /// </summary>
    public required MethodInfo Method { get; init; }

    /// <summary>
    /// The target grain ID.
    /// </summary>
    public required GrainId GrainId { get; init; }

    /// <summary>
    /// The authenticated user, or null if anonymous.
    /// </summary>
    public RpcUserIdentity? User { get; init; }

    /// <summary>
    /// The remote endpoint of the caller.
    /// </summary>
    public IPEndPoint? RemoteEndpoint { get; init; }

    /// <summary>
    /// The connection ID.
    /// </summary>
    public string? ConnectionId { get; init; }

    /// <summary>
    /// The request ID for correlation.
    /// </summary>
    public Guid RequestId { get; init; }

    /// <summary>
    /// The RPC method ID (for logging).
    /// </summary>
    public int MethodId { get; init; }

    /// <summary>
    /// Checks if the method has the specified attribute.
    /// </summary>
    public bool HasMethodAttribute<T>() where T : Attribute =>
        Method.GetCustomAttribute<T>() != null;

    /// <summary>
    /// Checks if the interface has the specified attribute.
    /// </summary>
    public bool HasInterfaceAttribute<T>() where T : Attribute =>
        GrainInterface.GetCustomAttribute<T>() != null;

    /// <summary>
    /// Gets all attributes of the specified type from method and interface.
    /// </summary>
    public IEnumerable<T> GetAttributes<T>() where T : Attribute =>
        Method.GetCustomAttributes<T>()
            .Concat(GrainInterface.GetCustomAttributes<T>());
}
```

**Checklist**:
- [ ] Create file `/src/Rpc/Orleans.Rpc.Server/Security/RpcAuthorizationContext.cs`
- [ ] Add all required properties with `required` modifier where appropriate
- [ ] Implement helper methods:
  - [ ] `HasMethodAttribute<T>()`
  - [ ] `HasInterfaceAttribute<T>()`
  - [ ] `GetAttributes<T>()` (combines method + interface attributes)

---

#### 2.3 Create IRpcAuthorizationFilter Interface

**File**: `/src/Rpc/Orleans.Rpc.Server/Security/IRpcAuthorizationFilter.cs`

```csharp
// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

namespace Granville.Rpc.Server.Security;

/// <summary>
/// Interface for RPC authorization filters.
/// Implement this to add custom authorization logic.
/// </summary>
/// <remarks>
/// Filters are executed in registration order. The first filter
/// to return a non-authorized result stops the chain.
/// </remarks>
public interface IRpcAuthorizationFilter
{
    /// <summary>
    /// Order in which this filter runs. Lower values run first.
    /// Default filters use order 0.
    /// </summary>
    int Order => 0;

    /// <summary>
    /// Checks if the request is authorized.
    /// </summary>
    /// <param name="context">The authorization context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Authorization result.</returns>
    Task<AuthorizationResult> AuthorizeAsync(
        RpcAuthorizationContext context,
        CancellationToken cancellationToken = default);
}
```

**Checklist**:
- [ ] Create file `/src/Rpc/Orleans.Rpc.Server/Security/IRpcAuthorizationFilter.cs`
- [ ] Define `Order` property with default implementation (0)
- [ ] Define `AuthorizeAsync()` method
- [ ] Document filter chain behavior

---

#### 2.4 Implement Default Authorization Filter

**File**: `/src/Rpc/Orleans.Rpc.Server/Security/DefaultRpcAuthorizationFilter.cs`

```csharp
// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

using Granville.Rpc.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Granville.Rpc.Server.Security;

/// <summary>
/// Default authorization filter implementing attribute-based authorization.
/// </summary>
public sealed class DefaultRpcAuthorizationFilter : IRpcAuthorizationFilter
{
    private readonly ILogger<DefaultRpcAuthorizationFilter> _logger;
    private readonly RpcSecurityOptions _options;

    public DefaultRpcAuthorizationFilter(
        ILogger<DefaultRpcAuthorizationFilter> logger,
        IOptions<RpcSecurityOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public int Order => 0;

    public Task<AuthorizationResult> AuthorizeAsync(
        RpcAuthorizationContext context,
        CancellationToken cancellationToken = default)
    {
        // If authorization is disabled, allow everything
        if (!_options.EnableAuthorization)
        {
            LogDecision(context, AuthorizationResult.Success("Authorization disabled"), LogLevel.Debug);
            return Task.FromResult(AuthorizationResult.Success("Authorization disabled"));
        }

        // Check 1: [AllowAnonymous] on method (highest priority)
        if (context.HasMethodAttribute<AllowAnonymousAttribute>())
        {
            var result = AuthorizationResult.Success("[AllowAnonymous]");
            LogDecision(context, result, LogLevel.Debug);
            return Task.FromResult(result);
        }

        // Check 2: [ClientAccessible] for strict mode
        if (_options.EnforceClientAccessibleAttribute &&
            context.User?.Role < UserRole.Server &&
            !context.HasInterfaceAttribute<ClientAccessibleAttribute>())
        {
            var result = AuthorizationResult.GrainNotClientAccessible(context.GrainInterface.Name);
            LogDecision(context, result, LogLevel.Warning);
            return Task.FromResult(result);
        }

        // Gather authorization requirements
        var requiresAuth = context.HasMethodAttribute<AuthorizeAttribute>() ||
                          context.HasInterfaceAttribute<AuthorizeAttribute>();

        var serverOnly = context.HasMethodAttribute<ServerOnlyAttribute>() ||
                        context.HasInterfaceAttribute<ServerOnlyAttribute>();

        var roleAttributes = context.GetAttributes<RequireRoleAttribute>().ToList();

        // Apply default policy if no explicit attributes
        if (!requiresAuth && !serverOnly && roleAttributes.Count == 0)
        {
            if (_options.DefaultPolicy == DefaultAuthorizationPolicy.RequireAuthentication)
            {
                requiresAuth = true;
            }
            else
            {
                // AllowAnonymous by default
                var result = AuthorizationResult.Success("Default: AllowAnonymous");
                LogDecision(context, result, LogLevel.Debug);
                return Task.FromResult(result);
            }
        }

        // Check 3: Authentication required
        if ((requiresAuth || serverOnly || roleAttributes.Count > 0) && context.User == null)
        {
            var result = AuthorizationResult.Unauthenticated();
            LogDecision(context, result, LogLevel.Warning);
            return Task.FromResult(result);
        }

        // Check 4: [ServerOnly]
        if (serverOnly && context.User!.Role < UserRole.Server)
        {
            var result = AuthorizationResult.ServerOnly();
            LogDecision(context, result, LogLevel.Warning);
            return Task.FromResult(result);
        }

        // Check 5: [RequireRole] - any matching role allows access (OR semantics)
        if (roleAttributes.Count > 0)
        {
            var highestRequired = roleAttributes.Max(r => r.Role);
            if (context.User!.Role < highestRequired)
            {
                var result = AuthorizationResult.InsufficientRole(highestRequired, context.User.Role);
                LogDecision(context, result, LogLevel.Warning);
                return Task.FromResult(result);
            }
        }

        // All checks passed
        var successResult = AuthorizationResult.Success("All checks passed");
        LogDecision(context, successResult, LogLevel.Debug);
        return Task.FromResult(successResult);
    }

    private void LogDecision(
        RpcAuthorizationContext context,
        AuthorizationResult result,
        LogLevel level)
    {
        if (!_options.LogAuthorizationDecisions && level == LogLevel.Debug)
            return;

        if (result.IsAuthorized)
        {
            _logger.Log(level,
                "[AUTHZ] Allowed {Interface}.{Method} for user {UserId} ({Role}). Rule: {Rule}",
                context.GrainInterface.Name,
                context.Method.Name,
                context.User?.UserId ?? "anonymous",
                context.User?.Role.ToString() ?? "none",
                result.DecidingRule);
        }
        else
        {
            _logger.Log(level,
                "[AUTHZ] Denied {Interface}.{Method} for user {UserId} ({Role}). " +
                "Reason: {Reason}. Rule: {Rule}. Endpoint: {Endpoint}",
                context.GrainInterface.Name,
                context.Method.Name,
                context.User?.UserId ?? "anonymous",
                context.User?.Role.ToString() ?? "none",
                result.FailureReason,
                result.DecidingRule,
                context.RemoteEndpoint);
        }
    }
}
```

**Checklist**:
- [ ] Create file `/src/Rpc/Orleans.Rpc.Server/Security/DefaultRpcAuthorizationFilter.cs`
- [ ] Inject `ILogger` and `IOptions<RpcSecurityOptions>`
- [ ] Implement authorization logic in order:
  - [ ] Check if authorization is disabled
  - [ ] Check `[AllowAnonymous]` on method (allows access)
  - [ ] Check `[ClientAccessible]` requirement if strict mode enabled
  - [ ] Determine if auth required (explicit `[Authorize]` or default policy)
  - [ ] Check authentication if required
  - [ ] Check `[ServerOnly]` attribute
  - [ ] Check `[RequireRole]` attributes
- [ ] Implement `LogDecision()` private method
- [ ] Handle all edge cases

---

### Phase 3: Configuration and DI

#### 3.1 Create RpcSecurityOptions

**File**: `/src/Rpc/Orleans.Rpc.Security/Configuration/RpcSecurityOptions.cs`

```csharp
// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

namespace Granville.Rpc.Security.Configuration;

/// <summary>
/// Configuration options for RPC security.
/// </summary>
public sealed class RpcSecurityOptions
{
    /// <summary>
    /// Enable or disable authorization checks.
    /// When false, all requests are allowed regardless of attributes.
    /// Default: true
    /// </summary>
    public bool EnableAuthorization { get; set; } = true;

    /// <summary>
    /// Default authorization policy when no attributes are present on
    /// a method or its containing interface.
    /// Default: AllowAnonymous (for backward compatibility)
    /// </summary>
    public DefaultAuthorizationPolicy DefaultPolicy { get; set; } =
        DefaultAuthorizationPolicy.AllowAnonymous;

    /// <summary>
    /// When true, client connections can only access grain interfaces
    /// marked with [ClientAccessible]. Server connections bypass this check.
    /// Default: false (for backward compatibility)
    /// </summary>
    public bool EnforceClientAccessibleAttribute { get; set; } = false;

    /// <summary>
    /// Log all authorization decisions (for debugging).
    /// When false, only denials are logged.
    /// Default: false
    /// </summary>
    public bool LogAuthorizationDecisions { get; set; } = false;

    /// <summary>
    /// Log full stack trace on authorization failures (for debugging).
    /// Default: false
    /// </summary>
    public bool LogStackTraceOnFailure { get; set; } = false;
}

/// <summary>
/// Default authorization policy when no explicit attributes are present.
/// </summary>
public enum DefaultAuthorizationPolicy
{
    /// <summary>
    /// Allow anonymous access unless [Authorize] or [RequireRole] is present.
    /// Use for development or backward compatibility.
    /// </summary>
    AllowAnonymous,

    /// <summary>
    /// Require authentication unless [AllowAnonymous] is present.
    /// Recommended for production.
    /// </summary>
    RequireAuthentication
}
```

**Checklist**:
- [ ] Create/update file `/src/Rpc/Orleans.Rpc.Security/Configuration/RpcSecurityOptions.cs`
- [ ] Add `EnableAuthorization` property (default: true)
- [ ] Add `DefaultPolicy` property (default: AllowAnonymous)
- [ ] Add `EnforceClientAccessibleAttribute` property (default: false)
- [ ] Add `LogAuthorizationDecisions` property (default: false)
- [ ] Add `LogStackTraceOnFailure` property (default: false)
- [ ] Create `DefaultAuthorizationPolicy` enum

---

#### 3.2 Create Extension Methods for DI Registration

**File**: `/src/Rpc/Orleans.Rpc.Security/Extensions/RpcAuthorizationExtensions.cs`

```csharp
// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

using Granville.Rpc.Security.Configuration;
using Granville.Rpc.Server.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Granville.Rpc.Security.Extensions;

/// <summary>
/// Extension methods for configuring RPC authorization.
/// </summary>
public static class RpcAuthorizationExtensions
{
    /// <summary>
    /// Adds RPC authorization services with default options.
    /// </summary>
    public static IServiceCollection AddRpcAuthorization(this IServiceCollection services)
    {
        return services.AddRpcAuthorization(_ => { });
    }

    /// <summary>
    /// Adds RPC authorization services with custom configuration.
    /// </summary>
    public static IServiceCollection AddRpcAuthorization(
        this IServiceCollection services,
        Action<RpcSecurityOptions> configure)
    {
        services.Configure(configure);
        services.TryAddSingleton<IRpcAuthorizationFilter, DefaultRpcAuthorizationFilter>();
        return services;
    }

    /// <summary>
    /// Adds a custom authorization filter to the pipeline.
    /// </summary>
    public static IServiceCollection AddRpcAuthorizationFilter<TFilter>(
        this IServiceCollection services)
        where TFilter : class, IRpcAuthorizationFilter
    {
        services.AddSingleton<IRpcAuthorizationFilter, TFilter>();
        return services;
    }

    /// <summary>
    /// Configures RPC security for production use.
    /// - Enables authorization
    /// - Requires authentication by default
    /// - Enables client-accessible grain restrictions
    /// </summary>
    public static IServiceCollection AddRpcAuthorizationProduction(
        this IServiceCollection services)
    {
        return services.AddRpcAuthorization(options =>
        {
            options.EnableAuthorization = true;
            options.DefaultPolicy = DefaultAuthorizationPolicy.RequireAuthentication;
            options.EnforceClientAccessibleAttribute = true;
            options.LogAuthorizationDecisions = false;
        });
    }

    /// <summary>
    /// Configures RPC security for development use.
    /// - Enables authorization
    /// - Allows anonymous by default
    /// - Logs all decisions
    /// </summary>
    public static IServiceCollection AddRpcAuthorizationDevelopment(
        this IServiceCollection services)
    {
        return services.AddRpcAuthorization(options =>
        {
            options.EnableAuthorization = true;
            options.DefaultPolicy = DefaultAuthorizationPolicy.AllowAnonymous;
            options.EnforceClientAccessibleAttribute = false;
            options.LogAuthorizationDecisions = true;
        });
    }

    /// <summary>
    /// Disables all RPC authorization checks.
    /// WARNING: Only use for local development!
    /// </summary>
    public static IServiceCollection AddRpcAuthorizationDisabled(
        this IServiceCollection services)
    {
        return services.AddRpcAuthorization(options =>
        {
            options.EnableAuthorization = false;
        });
    }
}
```

**Checklist**:
- [ ] Create file `/src/Rpc/Orleans.Rpc.Security/Extensions/RpcAuthorizationExtensions.cs`
- [ ] Implement `AddRpcAuthorization()` (default options)
- [ ] Implement `AddRpcAuthorization(Action<RpcSecurityOptions>)` (custom config)
- [ ] Implement `AddRpcAuthorizationFilter<T>()` for custom filters
- [ ] Implement `AddRpcAuthorizationProduction()` preset
- [ ] Implement `AddRpcAuthorizationDevelopment()` preset
- [ ] Implement `AddRpcAuthorizationDisabled()` for testing

---

### Phase 4: Integration with RpcConnection

#### 4.1 Add Identity Storage to PskSession

**File**: `/src/Rpc/Orleans.Rpc.Security/Transport/PskSession.cs` (MODIFY)

Add after line 46 (after `IsEstablished` property):

```csharp
/// <summary>
/// The authenticated user identity after successful handshake.
/// Populated by the transport when PSK lookup returns user info.
/// </summary>
public RpcUserIdentity? AuthenticatedUser { get; set; }
```

**Checklist**:
- [ ] Add `AuthenticatedUser` property to `PskSession` class
- [ ] Property should be settable (set during handshake)

---

#### 4.2 Populate Identity During PSK Handshake

**File**: `/src/Rpc/Orleans.Rpc.Security/Transport/PskEncryptedTransport.cs` (MODIFY)

The PSK lookup callback needs to return user info, not just the key. Update `DtlsPskOptions`:

**File**: `/src/Rpc/Orleans.Rpc.Security/Configuration/DtlsPskOptions.cs` (MODIFY)

```csharp
// Add new callback type that returns identity info
/// <summary>
/// Callback to look up PSK and user info by identity.
/// Returns null if identity is not valid.
/// </summary>
public Func<string, CancellationToken, Task<PskLookupResult?>>? PskLookupWithIdentity { get; set; }

/// <summary>
/// Result of a PSK lookup including user identity info.
/// </summary>
public sealed record PskLookupResult
{
    /// <summary>
    /// The pre-shared key bytes.
    /// </summary>
    public required byte[] Psk { get; init; }

    /// <summary>
    /// The authenticated user identity.
    /// </summary>
    public required RpcUserIdentity User { get; init; }
}
```

Then in `PskEncryptedTransport.HandleHelloAsync()`, after line 230 (after PSK lookup):

```csharp
// If using the new lookup with identity
if (_options.PskLookupWithIdentity != null)
{
    var lookupResult = await _options.PskLookupWithIdentity(identity, CancellationToken.None);
    if (lookupResult == null)
    {
        _logger.LogWarning("[PSK] No PSK found for identity '{Identity}'", identity);
        await SendHandshakeErrorAsync(e.RemoteEndPoint, "Unknown identity");
        return;
    }

    // Create session with challenge
    var session = new PskSession(identity, lookupResult.Psk, _logger);
    session.GenerateChallenge();
    session.AuthenticatedUser = lookupResult.User;  // NEW: Store identity
    _sessions[connectionId] = session;
    _endpointToConnection[e.RemoteEndPoint] = connectionId;

    // ... rest of handshake
}
```

**Checklist**:
- [ ] Add `PskLookupWithIdentity` callback to `DtlsPskOptions`
- [ ] Create `PskLookupResult` record with `Psk` and `User` properties
- [ ] Modify `HandleHelloAsync()` to use new callback if available
- [ ] Store `AuthenticatedUser` on `PskSession` after lookup
- [ ] Maintain backward compatibility with existing `PskLookup` callback

---

#### 4.3 Create Connection User Accessor Interface

**File**: `/src/Rpc/Orleans.Rpc.Server/Security/IConnectionUserAccessor.cs`

```csharp
// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

using Granville.Rpc.Security;

namespace Granville.Rpc.Server.Security;

/// <summary>
/// Interface for accessing the authenticated user associated with a connection.
/// Implemented by the security transport to provide identity info.
/// </summary>
public interface IConnectionUserAccessor
{
    /// <summary>
    /// Gets the authenticated user for the given connection ID.
    /// Returns null if the connection is anonymous or not found.
    /// </summary>
    RpcUserIdentity? GetUserForConnection(string connectionId);
}
```

**File**: `/src/Rpc/Orleans.Rpc.Security/Transport/PskEncryptedTransport.cs` (MODIFY)

Implement the interface:

```csharp
public class PskEncryptedTransport : IRpcTransport, IConnectionUserAccessor
{
    // ... existing code ...

    public RpcUserIdentity? GetUserForConnection(string connectionId)
    {
        if (_sessions.TryGetValue(connectionId, out var session) && session.IsEstablished)
        {
            return session.AuthenticatedUser;
        }
        return null;
    }
}
```

**Checklist**:
- [ ] Create file `/src/Rpc/Orleans.Rpc.Server/Security/IConnectionUserAccessor.cs`
- [ ] Define `GetUserForConnection(connectionId)` method
- [ ] Implement interface on `PskEncryptedTransport`
- [ ] Return `AuthenticatedUser` from session if established

---

#### 4.4 Integrate Authorization into RpcConnection

**File**: `/src/Rpc/Orleans.Rpc.Server/RpcConnection.cs` (MODIFY)

Add fields after existing fields (around line 47):

```csharp
private readonly IRpcAuthorizationFilter? _authorizationFilter;
private readonly IConnectionUserAccessor? _connectionUserAccessor;
```

Update constructor to accept new dependencies:

```csharp
public RpcConnection(
    string connectionId,
    IPEndPoint remoteEndPoint,
    IRpcTransport transport,
    RpcCatalog catalog,
    MessageFactory messageFactory,
    MessagingOptions messagingOptions,
    InterfaceToImplementationMappingCache interfaceToImplementationMapping,
    Serializer serializer,
    RpcSerializationSessionFactory sessionFactory,
    ILogger<RpcConnection> logger,
    IRpcAuthorizationFilter? authorizationFilter = null,  // NEW
    IConnectionUserAccessor? connectionUserAccessor = null)  // NEW
{
    // ... existing assignments ...
    _authorizationFilter = authorizationFilter;
    _connectionUserAccessor = connectionUserAccessor;
}
```

Modify `ProcessRequestAsync()` to add authorization:

```csharp
public async Task ProcessRequestAsync(Protocol.RpcRequest request)
{
    if (_disposed != 0)
    {
        _logger.LogWarning("Ignoring request on disposed connection {ConnectionId}", _connectionId);
        return;
    }

    // NEW: Get authenticated user from connection
    var user = _connectionUserAccessor?.GetUserForConnection(_connectionId);

    // NEW: Set security context for this request
    using var securityScope = RpcSecurityContext.SetContext(
        user,
        _connectionId,
        _remoteEndPoint,
        request.MessageId);

    try
    {
        // NEW: Authorization check
        if (_authorizationFilter != null)
        {
            var authResult = await AuthorizeRequestAsync(request, user);
            if (!authResult.IsAuthorized)
            {
                _logger.LogWarning(
                    "[RPC] Authorization denied for {ConnectionId}: {Reason}",
                    _connectionId, authResult.FailureReason);

                var errorResponse = new Protocol.RpcResponse
                {
                    RequestId = request.MessageId,
                    Success = false,
                    ErrorMessage = $"Authorization denied: {authResult.FailureReason}"
                };
                await SendResponseAsync(errorResponse);
                return;
            }
        }

        // Existing logic...
        var result = await InvokeGrainMethodAsync(request);
        // ...
    }
    catch (Exception ex)
    {
        // ... existing error handling ...
    }
}

private async Task<AuthorizationResult> AuthorizeRequestAsync(
    Protocol.RpcRequest request,
    RpcUserIdentity? user)
{
    // Resolve interface type and method
    var interfaceType = ResolveInterfaceType(request);
    var method = ResolveMethod(interfaceType, request.MethodId);

    var context = new RpcAuthorizationContext
    {
        GrainInterface = interfaceType,
        Method = method,
        GrainId = request.GrainId,
        User = user,
        RemoteEndpoint = _remoteEndPoint,
        ConnectionId = _connectionId,
        RequestId = request.MessageId,
        MethodId = request.MethodId
    };

    return await _authorizationFilter!.AuthorizeAsync(context);
}

private Type ResolveInterfaceType(Protocol.RpcRequest request)
{
    // Use existing logic from InvokeGrainMethodAsync to resolve interface type
    // This may require refactoring to avoid duplication
    // For now, a simplified approach:
    var grainContext = _catalog.TryGetActivation(request.GrainId);
    if (grainContext == null)
    {
        throw new InvalidOperationException($"Grain not found: {request.GrainId}");
    }

    var grainType = grainContext.GrainInstance?.GetType();
    if (grainType == null)
    {
        throw new InvalidOperationException($"Grain instance not found: {request.GrainId}");
    }

    // Find grain interface (same logic as InvokeGrainMethodAsync)
    foreach (var iface in grainType.GetInterfaces())
    {
        if (!iface.IsClass &&
            typeof(IGrain).IsAssignableFrom(iface) &&
            iface != typeof(IGrainObserver) &&
            iface != typeof(IAddressable) &&
            iface != typeof(IGrainExtension) &&
            iface != typeof(IGrain) &&
            iface != typeof(IGrainWithGuidKey) &&
            iface != typeof(IGrainWithIntegerKey) &&
            iface != typeof(IGrainWithGuidCompoundKey) &&
            iface != typeof(IGrainWithIntegerCompoundKey) &&
            iface != typeof(ISystemTarget))
        {
            return iface;
        }
    }

    throw new InvalidOperationException($"No grain interface found for {grainType.Name}");
}

private MethodInfo ResolveMethod(Type interfaceType, int methodId)
{
    var methods = interfaceType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Where(m => !m.IsSpecialName)
        .OrderBy(m => m.Name, StringComparer.Ordinal)
        .ToArray();

    if (methodId >= methods.Length)
    {
        throw new InvalidOperationException(
            $"Method ID {methodId} not found on interface {interfaceType.Name}");
    }

    return methods[methodId];
}
```

**Checklist**:
- [ ] Add `_authorizationFilter` field
- [ ] Add `_connectionUserAccessor` field
- [ ] Update constructor to accept new optional dependencies
- [ ] Add `using Granville.Rpc.Server.Security;` import
- [ ] In `ProcessRequestAsync()`:
  - [ ] Get user from `_connectionUserAccessor`
  - [ ] Create security context with `RpcSecurityContext.SetContext()`
  - [ ] Call `AuthorizeRequestAsync()` if filter is present
  - [ ] Return error response if not authorized
- [ ] Implement `AuthorizeRequestAsync()` method
- [ ] Implement `ResolveInterfaceType()` helper method
- [ ] Implement `ResolveMethod()` helper method

---

#### 4.5 Update RpcConnection Factory/Registration

Ensure the new dependencies are wired up in DI. This depends on how RpcConnection is created.

**File**: `/src/Rpc/Orleans.Rpc.Server/Hosting/DefaultRpcServerServices.cs` (MODIFY if exists)

Or wherever RpcConnection is instantiated, ensure it gets:
- `IRpcAuthorizationFilter` from DI (optional)
- `IConnectionUserAccessor` from DI (transport implements this)

**Checklist**:
- [ ] Register `IRpcAuthorizationFilter` in DI
- [ ] Register transport as `IConnectionUserAccessor`
- [ ] Update RpcConnection creation to inject dependencies

---

### Phase 5: Apply Attributes to Shooter Grains

#### 5.1 Update IGameGranule

**File**: `/granville/samples/Rpc/Shooter.Shared/GrainInterfaces/IGameGranule.cs` (MODIFY)

```csharp
using Granville.Rpc.Security;

[ClientAccessible]  // Clients can access this grain
[Authorize]  // All methods require authentication by default
public interface IGameGranule : IGrainWithStringKey
{
    [AllowAnonymous]  // Anyone can check server info
    Task<GameServerInfo> GetServerInfo();

    // These inherit [Authorize] from interface
    Task<PlayerState> GetPlayerState();
    Task RegisterPlayer(string playerId, string playerName);
    Task Move(MoveCommand command);
    Task Fire(FireCommand command);
    Task<ImmutableList<Bullet>> GetBullets();

    [ServerOnly]  // Only ActionServers (not clients) can call this
    Task<ImmutableList<PlayerState>> GetAllPlayers();

    [RequireRole(UserRole.Admin)]  // Admin only
    Task KickPlayer(string playerId);

    [RequireRole(UserRole.Admin)]
    Task BanPlayer(string playerId, TimeSpan duration);
}
```

**Checklist**:
- [ ] Add `using Granville.Rpc.Security;`
- [ ] Add `[ClientAccessible]` to interface
- [ ] Add `[Authorize]` to interface (default for all methods)
- [ ] Add `[AllowAnonymous]` to `GetServerInfo()`
- [ ] Add `[ServerOnly]` to internal methods like `GetAllPlayers()`
- [ ] Add `[RequireRole(UserRole.Admin)]` to admin methods

---

#### 5.2 Update IPlayerGrain

**File**: `/granville/samples/Rpc/Shooter.Shared/GrainInterfaces/IPlayerGrain.cs` (MODIFY)

```csharp
using Granville.Rpc.Security;

[ClientAccessible]
[Authorize]
public interface IPlayerGrain : IGrainWithStringKey
{
    [AllowAnonymous]  // Check if player exists
    Task<bool> Exists();

    Task<PlayerInfo> GetInfo();
    Task UpdatePosition(Vector2 position);
    Task TakeDamage(float damage, string sourcePlayerId);

    [ServerOnly]  // Only servers can force state changes
    Task ForceSetState(PlayerState state);
}
```

**Checklist**:
- [ ] Add authorization attributes to `IPlayerGrain`
- [ ] Mark internal state manipulation as `[ServerOnly]`

---

#### 5.3 Update IWorldManagerGrain

**File**: `/granville/samples/Rpc/Shooter.Shared/GrainInterfaces/IWorldManagerGrain.cs` (MODIFY)

```csharp
using Granville.Rpc.Security;

[ServerOnly]  // Only servers (Silo, ActionServers) access world manager
public interface IWorldManagerGrain : IGrainWithIntegerKey
{
    Task<ActionServerInfo> RegisterActionServer(...);
    Task UnregisterActionServer(string serverId);
    Task<PlayerInfo> RegisterPlayer(string playerId, string name);
    Task<ActionServerInfo?> GetActionServerForPosition(Vector2 position);
    Task<ImmutableList<ActionServerInfo>> GetAllActionServers();
    // ...
}
```

**Checklist**:
- [ ] Add `[ServerOnly]` to infrastructure grains
- [ ] Ensure internal grains are not `[ClientAccessible]`

---

### Phase 6: Update PSK Lookup to Return Identity

#### 6.1 Update Shooter ActionServer PSK Configuration

The ActionServer needs to configure PSK lookup to return full user identity.

**File**: `/granville/samples/Rpc/Shooter.ActionServer/Program.cs` (MODIFY)

Find where PSK is configured and update to use `PskLookupWithIdentity`:

```csharp
// In the service configuration
services.Configure<DtlsPskOptions>(options =>
{
    options.IsServer = true;
    options.PskLookupWithIdentity = async (identity, ct) =>
    {
        // Get the grain factory from DI
        var grainFactory = services.BuildServiceProvider()
            .GetRequiredService<Orleans.IGrainFactory>();

        // Look up session from Orleans grain
        var sessionGrain = grainFactory.GetGrain<IPlayerSessionGrain>(identity);
        var session = await sessionGrain.GetSessionAsync();

        if (session == null || !session.IsValid)
        {
            return null;
        }

        return new PskLookupResult
        {
            Psk = session.GetSessionKeyBytes(),
            User = new RpcUserIdentity
            {
                UserId = session.PlayerId,
                UserName = session.PlayerName,
                Role = MapToRpcRole(session.Role),
                ConnectionId = null  // Will be set by transport
            }
        };
    };
});

// Helper to map Shooter.Shared.UserRole to Granville.Rpc.Security.UserRole
static Granville.Rpc.Security.UserRole MapToRpcRole(Shooter.Shared.GrainInterfaces.UserRole role)
{
    return role switch
    {
        Shooter.Shared.GrainInterfaces.UserRole.Guest => Granville.Rpc.Security.UserRole.Guest,
        Shooter.Shared.GrainInterfaces.UserRole.User => Granville.Rpc.Security.UserRole.User,
        Shooter.Shared.GrainInterfaces.UserRole.Server => Granville.Rpc.Security.UserRole.Server,
        Shooter.Shared.GrainInterfaces.UserRole.Admin => Granville.Rpc.Security.UserRole.Admin,
        _ => Granville.Rpc.Security.UserRole.Guest
    };
}
```

**Checklist**:
- [ ] Update PSK configuration to use `PskLookupWithIdentity`
- [ ] Return `PskLookupResult` with both PSK and user identity
- [ ] Map Shooter's `UserRole` to RPC's `UserRole`
- [ ] Register authorization services: `services.AddRpcAuthorizationDevelopment()` or `AddRpcAuthorizationProduction()`

---

### Phase 7: Testing

#### 7.1 Unit Tests for DefaultRpcAuthorizationFilter

**File**: `/src/Rpc/test/Orleans.Rpc.Security.Tests/DefaultRpcAuthorizationFilterTests.cs`

```csharp
public class DefaultRpcAuthorizationFilterTests
{
    [Fact]
    public async Task AllowAnonymous_Method_AllowsUnauthenticated()
    {
        // Arrange
        var filter = CreateFilter(enableAuth: true);
        var context = CreateContext(
            interfaceType: typeof(ITestGrain),
            methodName: nameof(ITestGrain.PublicMethod),
            user: null);

        // Act
        var result = await filter.AuthorizeAsync(context);

        // Assert
        Assert.True(result.IsAuthorized);
    }

    [Fact]
    public async Task Authorize_Interface_RequiresAuthentication()
    {
        // Arrange
        var filter = CreateFilter(enableAuth: true);
        var context = CreateContext(
            interfaceType: typeof(IAuthorizedGrain),
            methodName: nameof(IAuthorizedGrain.SecureMethod),
            user: null);

        // Act
        var result = await filter.AuthorizeAsync(context);

        // Assert
        Assert.False(result.IsAuthorized);
        Assert.Equal("Authentication required", result.FailureReason);
    }

    [Fact]
    public async Task RequireRole_Admin_DeniesUserRole()
    {
        // Arrange
        var filter = CreateFilter(enableAuth: true);
        var context = CreateContext(
            interfaceType: typeof(IAdminGrain),
            methodName: nameof(IAdminGrain.AdminOnlyMethod),
            user: new RpcUserIdentity
            {
                UserId = "user1",
                UserName = "Test User",
                Role = UserRole.User
            });

        // Act
        var result = await filter.AuthorizeAsync(context);

        // Assert
        Assert.False(result.IsAuthorized);
        Assert.Contains("Admin", result.FailureReason);
    }

    [Fact]
    public async Task ServerOnly_DeniesClientRole()
    {
        // Arrange
        var filter = CreateFilter(enableAuth: true);
        var context = CreateContext(
            interfaceType: typeof(IServerOnlyGrain),
            methodName: nameof(IServerOnlyGrain.InternalMethod),
            user: new RpcUserIdentity
            {
                UserId = "user1",
                UserName = "Test User",
                Role = UserRole.User
            });

        // Act
        var result = await filter.AuthorizeAsync(context);

        // Assert
        Assert.False(result.IsAuthorized);
        Assert.Contains("server", result.FailureReason.ToLower());
    }

    [Fact]
    public async Task DisabledAuthorization_AllowsEverything()
    {
        // Arrange
        var filter = CreateFilter(enableAuth: false);
        var context = CreateContext(
            interfaceType: typeof(IAdminGrain),
            methodName: nameof(IAdminGrain.AdminOnlyMethod),
            user: null);

        // Act
        var result = await filter.AuthorizeAsync(context);

        // Assert
        Assert.True(result.IsAuthorized);
    }

    // Test interfaces
    public interface ITestGrain : IGrain
    {
        [AllowAnonymous]
        Task PublicMethod();
    }

    [Authorize]
    public interface IAuthorizedGrain : IGrain
    {
        Task SecureMethod();
    }

    public interface IAdminGrain : IGrain
    {
        [RequireRole(UserRole.Admin)]
        Task AdminOnlyMethod();
    }

    [ServerOnly]
    public interface IServerOnlyGrain : IGrain
    {
        Task InternalMethod();
    }
}
```

**Checklist**:
- [ ] Create test file
- [ ] Test `[AllowAnonymous]` allows unauthenticated
- [ ] Test `[Authorize]` requires authentication
- [ ] Test `[RequireRole]` checks role hierarchy
- [ ] Test `[ServerOnly]` denies client roles
- [ ] Test disabled authorization allows everything
- [ ] Test default policy (AllowAnonymous vs RequireAuthentication)
- [ ] Test `[ClientAccessible]` enforcement when strict mode enabled

---

#### 7.2 Integration Tests

**File**: `/granville/samples/Rpc/test/Shooter.Tests/AuthorizationIntegrationTests.cs`

```csharp
public class AuthorizationIntegrationTests
{
    [Fact]
    public async Task AuthenticatedClient_CanCallAuthorizedMethod()
    {
        // Setup client with valid PSK session
        // Call authorized method
        // Expect success
    }

    [Fact]
    public async Task UnauthenticatedClient_CannotCallAuthorizedMethod()
    {
        // Setup client without PSK
        // Attempt to call authorized method
        // Expect error response
    }

    [Fact]
    public async Task Client_CanCallAllowAnonymousMethod()
    {
        // Setup client without PSK
        // Call [AllowAnonymous] method
        // Expect success
    }

    [Fact]
    public async Task Client_CannotCallServerOnlyMethod()
    {
        // Setup client with valid user session (not server role)
        // Attempt to call [ServerOnly] method
        // Expect authorization failure
    }
}
```

**Checklist**:
- [ ] Create integration test file
- [ ] Test authenticated client can access authorized methods
- [ ] Test unauthenticated client is rejected from authorized methods
- [ ] Test anyone can access `[AllowAnonymous]` methods
- [ ] Test `[ServerOnly]` blocks client connections
- [ ] Test `[RequireRole]` with different role levels

---

## File Summary

### New Files to Create

| File | Purpose |
|------|---------|
| `/src/Rpc/Orleans.Rpc.Abstractions/Security/RpcUserIdentity.cs` | User identity record + UserRole enum |
| `/src/Rpc/Orleans.Rpc.Abstractions/Security/AuthorizationAttributes.cs` | Authorization attributes |
| `/src/Rpc/Orleans.Rpc.Server/Security/RpcSecurityContext.cs` | AsyncLocal context for identity flow |
| `/src/Rpc/Orleans.Rpc.Server/Security/AuthorizationResult.cs` | Result type for auth decisions |
| `/src/Rpc/Orleans.Rpc.Server/Security/RpcAuthorizationContext.cs` | Context passed to auth filter |
| `/src/Rpc/Orleans.Rpc.Server/Security/IRpcAuthorizationFilter.cs` | Filter interface |
| `/src/Rpc/Orleans.Rpc.Server/Security/DefaultRpcAuthorizationFilter.cs` | Default implementation |
| `/src/Rpc/Orleans.Rpc.Server/Security/IConnectionUserAccessor.cs` | Interface to get user from connection |
| `/src/Rpc/Orleans.Rpc.Security/Extensions/RpcAuthorizationExtensions.cs` | DI extension methods |

### Files to Modify

| File | Changes |
|------|---------|
| `/src/Rpc/Orleans.Rpc.Security/Configuration/DtlsPskOptions.cs` | Add `PskLookupWithIdentity` callback |
| `/src/Rpc/Orleans.Rpc.Security/Configuration/RpcSecurityOptions.cs` | Add authorization options |
| `/src/Rpc/Orleans.Rpc.Security/Transport/PskSession.cs` | Add `AuthenticatedUser` property |
| `/src/Rpc/Orleans.Rpc.Security/Transport/PskEncryptedTransport.cs` | Implement `IConnectionUserAccessor`, populate identity |
| `/src/Rpc/Orleans.Rpc.Server/RpcConnection.cs` | Add authorization check in ProcessRequestAsync |
| `/granville/samples/Rpc/Shooter.Shared/GrainInterfaces/*.cs` | Add authorization attributes |
| `/granville/samples/Rpc/Shooter.ActionServer/Program.cs` | Configure PSK with identity lookup |

---

## Success Criteria

- [ ] Unauthenticated connections cannot call `[Authorize]` methods
- [ ] `[AllowAnonymous]` methods work without authentication
- [ ] `[RequireRole]` correctly enforces role hierarchy
- [ ] `[ServerOnly]` prevents client access to internal grains
- [ ] `RpcSecurityContext.CurrentUser` is available in grain methods
- [ ] Authorization decisions are logged
- [ ] Existing functionality continues to work (backward compatible)
- [ ] Performance impact is minimal (<1ms overhead per request)

---

## Related Documents

- `SECURITY-RECAP.md` - Overall security roadmap
- `PSK-ARCHITECTURE-PLAN.md` - Transport-layer security (already implemented)
- `DESERIALIZATION-SAFETY-PLAN.md` - Type whitelisting (separate concern)
