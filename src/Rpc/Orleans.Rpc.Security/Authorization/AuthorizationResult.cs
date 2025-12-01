// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

namespace Granville.Rpc.Security.Authorization;

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
    /// <param name="rule">The rule that allowed the request.</param>
    /// <returns>A successful authorization result.</returns>
    public static AuthorizationResult Success(string? rule = null) =>
        new() { IsAuthorized = true, DecidingRule = rule };

    /// <summary>
    /// Creates a failed authorization result.
    /// </summary>
    /// <param name="reason">The reason for denial.</param>
    /// <param name="rule">The rule that denied the request.</param>
    /// <returns>A failed authorization result.</returns>
    public static AuthorizationResult Fail(string reason, string? rule = null) =>
        new() { IsAuthorized = false, FailureReason = reason, DecidingRule = rule };

    /// <summary>
    /// Authentication required but user is anonymous.
    /// </summary>
    /// <returns>A failed authorization result indicating authentication is required.</returns>
    public static AuthorizationResult Unauthenticated() =>
        Fail("Authentication required", "[Authorize]");

    /// <summary>
    /// User doesn't have required role.
    /// </summary>
    /// <param name="required">The required role.</param>
    /// <param name="actual">The user's actual role.</param>
    /// <returns>A failed authorization result indicating insufficient role.</returns>
    public static AuthorizationResult InsufficientRole(UserRole required, UserRole actual) =>
        Fail($"Role '{required}' required, user has '{actual}'", $"[RequireRole({required})]");

    /// <summary>
    /// Server-only method called by non-server.
    /// </summary>
    /// <returns>A failed authorization result indicating server-only restriction.</returns>
    public static AuthorizationResult ServerOnly() =>
        Fail("This method is restricted to server components", "[ServerOnly]");

    /// <summary>
    /// Grain not accessible to clients.
    /// </summary>
    /// <param name="grainType">The grain type that is not accessible.</param>
    /// <returns>A failed authorization result indicating grain not client accessible.</returns>
    public static AuthorizationResult GrainNotClientAccessible(string grainType) =>
        Fail($"Grain '{grainType}' is not accessible to clients", "[ClientAccessible] missing");
}
