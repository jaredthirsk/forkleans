// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

using Granville.Rpc.Security.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Granville.Rpc.Security.Authorization;

/// <summary>
/// Default authorization filter implementing attribute-based authorization.
/// </summary>
public sealed class DefaultRpcAuthorizationFilter : IRpcAuthorizationFilter
{
    private readonly ILogger<DefaultRpcAuthorizationFilter> _logger;
    private readonly RpcSecurityOptions _options;

    /// <summary>
    /// Creates a new instance of <see cref="DefaultRpcAuthorizationFilter"/>.
    /// </summary>
    /// <param name="logger">Logger for authorization decisions.</param>
    /// <param name="options">Security configuration options.</param>
    public DefaultRpcAuthorizationFilter(
        ILogger<DefaultRpcAuthorizationFilter> logger,
        IOptions<RpcSecurityOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc/>
    public int Order => 0;

    /// <inheritdoc/>
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
