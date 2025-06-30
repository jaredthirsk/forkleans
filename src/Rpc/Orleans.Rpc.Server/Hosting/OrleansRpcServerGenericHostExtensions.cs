using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Granville.Rpc.Hosting;
using Orleans.Runtime;

namespace Microsoft.Extensions.Hosting
{
    /// <summary>
    /// Extension methods for <see cref="IHostBuilder"/> to configure Orleans RPC server.
    /// </summary>
    public static class OrleansRpcServerGenericHostExtensions
    {
        private static readonly Type MarkerType = typeof(OrleansRpcBuilderMarker);

        /// <summary>
        /// Configures the host app builder to host an Orleans RPC server.
        /// </summary>
        /// <param name="hostAppBuilder">The host app builder.</param>
        /// <returns>The host builder.</returns>
        public static IHostApplicationBuilder UseOrleansRpc(
            this IHostApplicationBuilder hostAppBuilder)
            => hostAppBuilder.UseOrleansRpc(_ => { });

        /// <summary>
        /// Configures the host app builder to host an Orleans RPC server.
        /// </summary>
        /// <param name="hostAppBuilder">The host app builder.</param>
        /// <param name="configureDelegate">The delegate used to configure the RPC server.</param>
        /// <returns>The host builder.</returns>
        public static IHostApplicationBuilder UseOrleansRpc(
            this IHostApplicationBuilder hostAppBuilder,
            Action<IRpcServerBuilder> configureDelegate)
        {
            ArgumentNullException.ThrowIfNull(hostAppBuilder);
            ArgumentNullException.ThrowIfNull(configureDelegate);

            configureDelegate(AddOrleansRpcCore(hostAppBuilder.Services, hostAppBuilder.Configuration));

            return hostAppBuilder;
        }

        /// <summary>
        /// Configures the host builder to host an Orleans RPC server.
        /// </summary>
        /// <param name="hostBuilder">The host builder.</param>
        /// <param name="configureDelegate">The delegate used to configure the RPC server.</param>
        /// <returns>The host builder.</returns>
        public static IHostBuilder UseOrleansRpc(
            this IHostBuilder hostBuilder,
            Action<IRpcServerBuilder> configureDelegate) => hostBuilder.UseOrleansRpc((_, rpcBuilder) => configureDelegate(rpcBuilder));

        /// <summary>
        /// Configures the host builder to host an Orleans RPC server.
        /// </summary>
        /// <param name="hostBuilder">The host builder.</param>
        /// <param name="configureDelegate">The delegate used to configure the RPC server.</param>
        /// <returns>The host builder.</returns>
        public static IHostBuilder UseOrleansRpc(
            this IHostBuilder hostBuilder,
            Action<HostBuilderContext, IRpcServerBuilder> configureDelegate)
        {
            ArgumentNullException.ThrowIfNull(hostBuilder);
            ArgumentNullException.ThrowIfNull(configureDelegate);

            if (hostBuilder.Properties.ContainsKey("HasOrleansClientBuilder") || hostBuilder.Properties.ContainsKey("HasOrleansSiloBuilder"))
            {
                throw new OrleansConfigurationException("Cannot use UseOrleansRpc with UseOrleans or UseOrleansClient. Orleans RPC is a separate, non-clustered implementation.");
            }

            hostBuilder.Properties["HasOrleansRpcServerBuilder"] = "true";

            return hostBuilder.ConfigureServices((context, services) => configureDelegate(context, AddOrleansRpcCore(services, context.Configuration)));
        }

        /// <summary>
        /// Configures the service collection to host an Orleans RPC server.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configureDelegate">The delegate used to configure the RPC server.</param>
        /// <returns>The service collection.</returns>
        public static IServiceCollection AddOrleansRpc(
            this IServiceCollection services,
            Action<IRpcServerBuilder> configureDelegate)
        {
            ArgumentNullException.ThrowIfNull(configureDelegate);

            var builder = AddOrleansRpcCore(services, null);

            configureDelegate(builder);

            return services;
        }

        private static IRpcServerBuilder AddOrleansRpcCore(IServiceCollection services, IConfiguration configuration)
        {
            IRpcServerBuilder builder = default;
            configuration ??= new ConfigurationBuilder().Build();
            
            foreach (var descriptor in services.Where(d => d.ServiceType.Equals(MarkerType)))
            {
                var marker = (OrleansRpcBuilderMarker)descriptor.ImplementationInstance;
                if (marker.BuilderInstance is IRpcServerBuilder existingBuilder)
                {
                    builder = existingBuilder;
                }
                else
                {
                    throw new OrleansConfigurationException("Cannot mix Orleans RPC with standard Orleans client/silo configuration.");
                }
            }

            if (builder is null)
            {
                builder = new RpcServerBuilder(services, configuration);
                services.AddSingleton(new OrleansRpcBuilderMarker(builder));
            }

            return builder;
        }
    }

    /// <summary>
    /// Marker type used for storing an RPC builder in a service collection.
    /// </summary>
    internal sealed class OrleansRpcBuilderMarker
    {
        public OrleansRpcBuilderMarker(object builderInstance) => BuilderInstance = builderInstance;
        public object BuilderInstance { get; }
    }
}