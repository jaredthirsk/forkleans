using System;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization.Configuration;
using Orleans.Serialization;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Runtime;
using Orleans.Configuration;

namespace Granville.Rpc.Hosting
{
    /// <summary>
    /// Extension methods for configuring RPC server.
    /// </summary>
    public static class RpcServerBuilderExtensions
    {
        /// <summary>
        /// Adds an assembly containing grains to the RPC server.
        /// </summary>
        /// <param name="builder">The RPC server builder.</param>
        /// <param name="assembly">The assembly containing grains.</param>
        /// <returns>The builder.</returns>
        public static IRpcServerBuilder AddGrainAssembly(this IRpcServerBuilder builder, Assembly assembly)
        {
            ArgumentNullException.ThrowIfNull(assembly);

            // Configure TypeManifestOptions to include types from the assembly
            builder.Services.Configure<TypeManifestOptions>(options =>
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (!type.IsAbstract && !type.IsInterface)
                    {
                        // Check if it's a grain implementation
                        if (typeof(IGrain).IsAssignableFrom(type))
                        {
                            options.InterfaceImplementations.Add(type);
                        }
                    }
                    else if (type.IsInterface)
                    {
                        // Check if it's a grain interface
                        if (typeof(IAddressable).IsAssignableFrom(type))
                        {
                            options.Interfaces.Add(type);
                        }
                    }
                }
            });
            
            // Also configure GrainTypeOptions directly to ensure types are registered
            builder.Services.Configure<GrainTypeOptions>(options =>
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (!type.IsAbstract && !type.IsInterface)
                    {
                        // Check if it's a grain implementation
                        if (typeof(IGrain).IsAssignableFrom(type))
                        {
                            options.Classes.Add(type);
                        }
                    }
                    else if (type.IsInterface)
                    {
                        // Check if it's a grain interface
                        if (typeof(IAddressable).IsAssignableFrom(type))
                        {
                            options.Interfaces.Add(type);
                        }
                    }
                }
            });

            // Also add the assembly to serialization
            builder.Services.AddSerializer(serializer => serializer.AddAssembly(assembly));

            return builder;
        }

        /// <summary>
        /// Adds the calling assembly for grain discovery.
        /// </summary>
        /// <param name="builder">The RPC server builder.</param>
        /// <returns>The builder.</returns>
        public static IRpcServerBuilder AddCallingAssembly(this IRpcServerBuilder builder)
        {
            return builder.AddGrainAssembly(Assembly.GetCallingAssembly());
        }

        /// <summary>
        /// Adds the assembly containing a specific type.
        /// </summary>
        /// <typeparam name="T">The type whose assembly should be added.</typeparam>
        /// <param name="builder">The RPC server builder.</param>
        /// <returns>The builder.</returns>
        public static IRpcServerBuilder AddAssemblyContaining<T>(this IRpcServerBuilder builder)
        {
            return builder.AddGrainAssembly(typeof(T).Assembly);
        }
    }
}