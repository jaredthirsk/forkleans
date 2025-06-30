using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Granville.Rpc.Hosting;
using Orleans.Runtime;

namespace Microsoft.Extensions.Hosting
{
    /// <summary>
    /// Extension methods for <see cref="IHostBuilder"/> to configure Orleans RPC client.
    /// </summary>
    public static class OrleansRpcClientGenericHostExtensions
    {
        private static readonly Type MarkerType = typeof(OrleansRpcClientBuilderMarker);

        /// <summary>
        /// Configures the host app builder to host an Orleans RPC client.
        /// </summary>
        /// <param name="hostAppBuilder">The host app builder.</param>
        /// <returns>The host builder.</returns>
        public static IHostApplicationBuilder UseOrleansRpcClient(
            this IHostApplicationBuilder hostAppBuilder)
            => hostAppBuilder.UseOrleansRpcClient(_ => { });

        /// <summary>
        /// Configures the host app builder to host an Orleans RPC client.
        /// </summary>
        /// <param name="hostAppBuilder">The host app builder.</param>
        /// <param name="configureDelegate">The delegate used to configure the RPC client.</param>
        /// <returns>The host builder.</returns>
        public static IHostApplicationBuilder UseOrleansRpcClient(
            this IHostApplicationBuilder hostAppBuilder,
            Action<IRpcClientBuilder> configureDelegate)
        {
            ArgumentNullException.ThrowIfNull(hostAppBuilder);
            ArgumentNullException.ThrowIfNull(configureDelegate);

            hostAppBuilder.Services.AddOrleansRpcClient(hostAppBuilder.Configuration, configureDelegate);

            return hostAppBuilder;
        }

        /// <summary>
        /// Configures the host builder to host an Orleans RPC client.
        /// </summary>
        /// <param name="hostBuilder">The host builder.</param>
        /// <param name="configureDelegate">The delegate used to configure the RPC client.</param>
        /// <returns>The host builder.</returns>
        public static IHostBuilder UseOrleansRpcClient(
            this IHostBuilder hostBuilder,
            Action<IRpcClientBuilder> configureDelegate) => hostBuilder.UseOrleansRpcClient((_, clientBuilder) => configureDelegate(clientBuilder));

        /// <summary>
        /// Configures the host builder to host an Orleans RPC client.
        /// </summary>
        /// <param name="hostBuilder">The host builder.</param>
        /// <param name="configureDelegate">The delegate used to configure the RPC client.</param>
        /// <returns>The host builder.</returns>
        public static IHostBuilder UseOrleansRpcClient(
            this IHostBuilder hostBuilder,
            Action<HostBuilderContext, IRpcClientBuilder> configureDelegate)
        {
            ArgumentNullException.ThrowIfNull(hostBuilder);
            ArgumentNullException.ThrowIfNull(configureDelegate);

            if (hostBuilder.Properties.ContainsKey("HasOrleansSiloBuilder") || 
                hostBuilder.Properties.ContainsKey("HasOrleansClientBuilder") ||
                hostBuilder.Properties.ContainsKey("HasOrleansRpcServerBuilder"))
            {
                throw new OrleansConfigurationException("Cannot use UseOrleansRpcClient with other Orleans configurations. Orleans RPC client is a separate implementation.");
            }

            hostBuilder.Properties["HasOrleansRpcClientBuilder"] = "true";

            return hostBuilder.ConfigureServices((ctx, services) => configureDelegate(ctx, AddOrleansRpcClient(services, ctx.Configuration)));
        }

        /// <summary>
        /// Configures the service collection to host an Orleans RPC client.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configureDelegate">The delegate used to configure the RPC client.</param>
        /// <returns>The service collection.</returns>
        public static IServiceCollection AddOrleansRpcClient(
            this IServiceCollection services,
            Action<IRpcClientBuilder> configureDelegate)
        {
            ArgumentNullException.ThrowIfNull(configureDelegate);

            var clientBuilder = AddOrleansRpcClient(services, configuration: null);
            configureDelegate(clientBuilder);
            return services;
        }

        /// <summary>
        /// Configures the service collection to host an Orleans RPC client.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">The configuration.</param>
        /// <param name="configureDelegate">The delegate used to configure the RPC client.</param>
        /// <returns>The service collection.</returns>
        public static IServiceCollection AddOrleansRpcClient(
            this IServiceCollection services,
            IConfiguration configuration,
            Action<IRpcClientBuilder> configureDelegate)
        {
            ArgumentNullException.ThrowIfNull(configureDelegate);

            var clientBuilder = AddOrleansRpcClient(services, configuration);
            configureDelegate(clientBuilder);
            return services;
        }

        private static IRpcClientBuilder AddOrleansRpcClient(IServiceCollection services, IConfiguration configuration)
        {
            configuration ??= new ConfigurationBuilder().Build();
            IRpcClientBuilder clientBuilder = default;
            
            foreach (var descriptor in services.Where(d => d.ServiceType.Equals(MarkerType)))
            {
                var instance = (OrleansRpcClientBuilderMarker)descriptor.ImplementationInstance;
                clientBuilder = instance.BuilderInstance as IRpcClientBuilder;
                if (clientBuilder is null)
                {
                    throw new OrleansConfigurationException("Cannot mix Orleans RPC client with other Orleans configurations.");
                }
            }

            if (clientBuilder is null)
            {
                clientBuilder = new RpcClientBuilder(services, configuration);
                services.AddSingleton(new OrleansRpcClientBuilderMarker(clientBuilder));
            }

            return clientBuilder;
        }
    }

    /// <summary>
    /// Marker type used for storing an RPC client builder in a service collection.
    /// </summary>
    internal sealed class OrleansRpcClientBuilderMarker
    {
        public OrleansRpcClientBuilderMarker(object builderInstance) => BuilderInstance = builderInstance;
        public object BuilderInstance { get; }
    }
}