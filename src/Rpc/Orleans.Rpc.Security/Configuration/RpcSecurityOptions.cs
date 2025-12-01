// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

namespace Granville.Rpc.Security.Configuration;

/// <summary>
/// Configuration options for RPC security and authorization.
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
    /// marked with <see cref="ClientAccessibleAttribute"/>.
    /// Server connections bypass this check.
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
