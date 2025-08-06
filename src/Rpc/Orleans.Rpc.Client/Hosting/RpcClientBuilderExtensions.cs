using System;
using Microsoft.Extensions.DependencyInjection;
using Granville.Rpc.Zones;
using Orleans.Hosting;

namespace Granville.Rpc.Hosting
{
    /// <summary>
    /// Extension methods for configuring zone detection strategies on RPC clients.
    /// </summary>
    public static class RpcClientBuilderExtensions
    {
        /// <summary>
        /// Configures the RPC client to use the specified zone detection strategy.
        /// </summary>
        /// <typeparam name="TStrategy">The type of zone detection strategy to use.</typeparam>
        /// <param name="builder">The RPC client builder.</param>
        /// <returns>The builder for chaining.</returns>
        public static IClientBuilder UseZoneDetectionStrategy<TStrategy>(this IClientBuilder builder)
            where TStrategy : class, IZoneDetectionStrategy
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IZoneDetectionStrategy, TStrategy>();
            });
            return builder;
        }
        
        /// <summary>
        /// Configures the RPC client to use the specified zone detection strategy instance.
        /// </summary>
        /// <param name="builder">The RPC client builder.</param>
        /// <param name="strategy">The zone detection strategy instance to use.</param>
        /// <returns>The builder for chaining.</returns>
        public static IClientBuilder UseZoneDetectionStrategy(this IClientBuilder builder, IZoneDetectionStrategy strategy)
        {
            if (strategy == null)
            {
                throw new ArgumentNullException(nameof(strategy));
            }
            
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IZoneDetectionStrategy>(strategy);
            });
            return builder;
        }
        
        /// <summary>
        /// Configures the RPC client to use a zone detection strategy with custom configuration.
        /// </summary>
        /// <typeparam name="TStrategy">The type of zone detection strategy to use.</typeparam>
        /// <param name="builder">The RPC client builder.</param>
        /// <param name="configureStrategy">Action to configure the strategy after creation.</param>
        /// <returns>The builder for chaining.</returns>
        public static IClientBuilder UseZoneDetectionStrategy<TStrategy>(this IClientBuilder builder, Action<TStrategy> configureStrategy)
            where TStrategy : class, IZoneDetectionStrategy, new()
        {
            if (configureStrategy == null)
            {
                throw new ArgumentNullException(nameof(configureStrategy));
            }
            
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IZoneDetectionStrategy>(sp =>
                {
                    var strategy = ActivatorUtilities.CreateInstance<TStrategy>(sp);
                    configureStrategy(strategy);
                    return strategy;
                });
            });
            return builder;
        }
    }
}