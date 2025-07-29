using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Granville.Rpc.Multiplexing;
using Granville.Rpc.Multiplexing.Strategies;

namespace Granville.Rpc.Hosting
{
    /// <summary>
    /// Extension methods for configuring RPC client multiplexer services.
    /// </summary>
    public static class RpcClientMultiplexerServiceExtensions
    {
        /// <summary>
        /// Adds the RPC client multiplexer to the service collection.
        /// </summary>
        public static IServiceCollection AddRpcClientMultiplexer(
            this IServiceCollection services,
            Action<RpcClientMultiplexerOptions>? configureOptions = null)
        {
            // Add options
            if (configureOptions != null)
            {
                services.Configure(configureOptions);
            }
            
            // Register default routing strategies
            services.TryAddSingleton<ZoneBasedRoutingStrategy>();
            services.TryAddSingleton<ServiceBasedRoutingStrategy>();
            services.TryAddSingleton<CompositeRoutingStrategy>();
            
            // Register the multiplexer
            services.TryAddSingleton<IRpcClientMultiplexer>(provider =>
            {
                var options = provider.GetService<IOptions<RpcClientMultiplexerOptions>>()?.Value 
                    ?? new RpcClientMultiplexerOptions();
                var logger = provider.GetRequiredService<ILogger<RpcClientMultiplexer>>();
                
                return new RpcClientMultiplexer(options, provider, logger);
            });
            
            return services;
        }
        
        /// <summary>
        /// Adds the RPC client multiplexer with a custom routing strategy.
        /// </summary>
        public static IServiceCollection AddRpcClientMultiplexer(
            this IServiceCollection services,
            Func<IServiceProvider, IGrainRoutingStrategy> routingStrategyFactory,
            Action<RpcClientMultiplexerOptions>? configureOptions = null)
        {
            // Add base multiplexer services
            services.AddRpcClientMultiplexer(configureOptions);
            
            // Override with custom multiplexer that uses the provided routing strategy
            services.AddSingleton<IRpcClientMultiplexer>(provider =>
            {
                var options = provider.GetService<IOptions<RpcClientMultiplexerOptions>>()?.Value 
                    ?? new RpcClientMultiplexerOptions();
                var logger = provider.GetRequiredService<ILogger<RpcClientMultiplexer>>();
                var routingStrategy = routingStrategyFactory(provider);
                
                var multiplexer = new RpcClientMultiplexer(options, provider, logger);
                multiplexer.SetRoutingStrategy(routingStrategy);
                
                return multiplexer;
            });
            
            return services;
        }
        
        /// <summary>
        /// Creates a builder for configuring the RPC client multiplexer.
        /// </summary>
        public static RpcClientMultiplexerBuilder AddRpcClientMultiplexerWithBuilder(
            this IServiceCollection services,
            Action<RpcClientMultiplexerOptions>? configureOptions = null)
        {
            // Add base services
            services.AddRpcClientMultiplexer(configureOptions);
            
            return new RpcClientMultiplexerBuilder(services);
        }
    }
    
    /// <summary>
    /// Builder for configuring RPC client multiplexer.
    /// </summary>
    public class RpcClientMultiplexerBuilder
    {
        private readonly IServiceCollection _services;
        
        public RpcClientMultiplexerBuilder(IServiceCollection services)
        {
            _services = services;
        }
        
        /// <summary>
        /// Adds a server descriptor to the multiplexer configuration.
        /// </summary>
        public RpcClientMultiplexerBuilder AddServer(
            string serverId,
            string hostName,
            int port,
            bool isPrimary = false,
            Action<ServerDescriptor>? configure = null)
        {
            _services.AddSingleton<IServerRegistration>(provider =>
            {
                var descriptor = new ServerDescriptor
                {
                    ServerId = serverId,
                    HostName = hostName,
                    Port = port,
                    IsPrimary = isPrimary
                };
                
                configure?.Invoke(descriptor);
                
                return new ServerRegistration { Descriptor = descriptor };
            });
            
            return this;
        }
        
        /// <summary>
        /// Uses zone-based routing strategy.
        /// </summary>
        public RpcClientMultiplexerBuilder UseZoneBasedRouting()
        {
            _services.AddSingleton<IRpcClientMultiplexer>(provider =>
            {
                var options = provider.GetService<IOptions<RpcClientMultiplexerOptions>>()?.Value 
                    ?? new RpcClientMultiplexerOptions();
                var logger = provider.GetRequiredService<ILogger<RpcClientMultiplexer>>();
                var zoneStrategy = provider.GetRequiredService<ZoneBasedRoutingStrategy>();
                
                var multiplexer = new RpcClientMultiplexer(options, provider, logger);
                multiplexer.SetRoutingStrategy(zoneStrategy);
                
                // Register servers from DI
                foreach (var registration in provider.GetServices<IServerRegistration>())
                {
                    multiplexer.RegisterServer(registration.Descriptor);
                }
                
                return multiplexer;
            });
            
            return this;
        }
        
        /// <summary>
        /// Uses service-based routing strategy.
        /// </summary>
        public RpcClientMultiplexerBuilder UseServiceBasedRouting(
            Action<ServiceBasedRoutingStrategy>? configure = null)
        {
            _services.AddSingleton<IRpcClientMultiplexer>(provider =>
            {
                var options = provider.GetService<IOptions<RpcClientMultiplexerOptions>>()?.Value 
                    ?? new RpcClientMultiplexerOptions();
                var logger = provider.GetRequiredService<ILogger<RpcClientMultiplexer>>();
                var serviceStrategy = provider.GetRequiredService<ServiceBasedRoutingStrategy>();
                
                configure?.Invoke(serviceStrategy);
                
                var multiplexer = new RpcClientMultiplexer(options, provider, logger);
                multiplexer.SetRoutingStrategy(serviceStrategy);
                
                // Register servers from DI
                foreach (var registration in provider.GetServices<IServerRegistration>())
                {
                    multiplexer.RegisterServer(registration.Descriptor);
                }
                
                return multiplexer;
            });
            
            return this;
        }
        
        /// <summary>
        /// Uses composite routing strategy.
        /// </summary>
        public RpcClientMultiplexerBuilder UseCompositeRouting(
            Action<CompositeRoutingStrategy>? configure = null)
        {
            _services.AddSingleton<IRpcClientMultiplexer>(provider =>
            {
                var options = provider.GetService<IOptions<RpcClientMultiplexerOptions>>()?.Value 
                    ?? new RpcClientMultiplexerOptions();
                var logger = provider.GetRequiredService<ILogger<RpcClientMultiplexer>>();
                var compositeStrategy = provider.GetRequiredService<CompositeRoutingStrategy>();
                
                configure?.Invoke(compositeStrategy);
                
                var multiplexer = new RpcClientMultiplexer(options, provider, logger);
                multiplexer.SetRoutingStrategy(compositeStrategy);
                
                // Register servers from DI
                foreach (var registration in provider.GetServices<IServerRegistration>())
                {
                    multiplexer.RegisterServer(registration.Descriptor);
                }
                
                return multiplexer;
            });
            
            return this;
        }
    }
    
    // Internal interfaces for DI registration
    internal interface IServerRegistration
    {
        IServerDescriptor Descriptor { get; }
    }
    
    internal class ServerRegistration : IServerRegistration
    {
        public IServerDescriptor Descriptor { get; set; } = null!;
    }
}