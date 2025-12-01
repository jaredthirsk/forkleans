// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

using Granville.Rpc.Security.Authorization;
using Granville.Rpc.Security.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Granville.Rpc.Security;

/// <summary>
/// Extension methods for configuring RPC authorization.
/// </summary>
public static class RpcAuthorizationExtensions
{
    /// <summary>
    /// Adds RPC authorization services with default options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRpcAuthorization(this IServiceCollection services)
    {
        return services.AddRpcAuthorization(_ => { });
    }

    /// <summary>
    /// Adds RPC authorization services with custom configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Action to configure security options.</param>
    /// <returns>The service collection for chaining.</returns>
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
    /// <typeparam name="TFilter">The filter type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRpcAuthorizationFilter<TFilter>(
        this IServiceCollection services)
        where TFilter : class, IRpcAuthorizationFilter
    {
        services.AddSingleton<IRpcAuthorizationFilter, TFilter>();
        return services;
    }

    /// <summary>
    /// Configures RPC security for production use.
    /// <list type="bullet">
    /// <item>Enables authorization</item>
    /// <item>Requires authentication by default</item>
    /// <item>Enables client-accessible grain restrictions</item>
    /// </list>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
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
    /// <list type="bullet">
    /// <item>Enables authorization</item>
    /// <item>Allows anonymous by default</item>
    /// <item>Logs all decisions</item>
    /// </list>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
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
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRpcAuthorizationDisabled(
        this IServiceCollection services)
    {
        return services.AddRpcAuthorization(options =>
        {
            options.EnableAuthorization = false;
        });
    }
}
