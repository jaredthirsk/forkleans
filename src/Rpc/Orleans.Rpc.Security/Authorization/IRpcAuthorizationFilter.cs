// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

namespace Granville.Rpc.Security.Authorization;

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
    /// <returns>Authorization result indicating whether the request is allowed.</returns>
    Task<AuthorizationResult> AuthorizeAsync(
        RpcAuthorizationContext context,
        CancellationToken cancellationToken = default);
}
