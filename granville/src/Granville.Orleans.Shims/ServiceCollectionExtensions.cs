using System;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace Granville.Orleans.Shims
{
    /// <summary>
    /// Extension methods for configuring Granville Orleans shim compatibility.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds Granville Orleans shim compatibility to ensure proper serialization metadata discovery.
        /// This is a workaround for the issue where Orleans serialization cannot discover metadata
        /// through TypeForwardedTo attributes in shim assemblies.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddOrleansShims(this IServiceCollection services)
        {
            // Ensure Granville assemblies are loaded early
            EnsureGranvilleAssembliesLoaded();

            // Configure serializer to include Granville assemblies
            services.AddSerializer(serializerBuilder =>
            {
                AddGranvilleAssemblies(serializerBuilder);
            });

            return services;
        }

        /// <summary>
        /// Adds Granville Orleans shim compatibility with custom serializer configuration.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configureSerializer">Additional serializer configuration.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddOrleansShims(
            this IServiceCollection services,
            Action<ISerializerBuilder> configureSerializer)
        {
            // Ensure Granville assemblies are loaded early
            EnsureGranvilleAssembliesLoaded();

            // Configure serializer to include Granville assemblies
            services.AddSerializer(serializerBuilder =>
            {
                AddGranvilleAssemblies(serializerBuilder);
                configureSerializer?.Invoke(serializerBuilder);
            });

            return services;
        }

        /// <summary>
        /// Adds Granville assemblies to an existing serializer builder.
        /// Use this if you're already calling AddSerializer elsewhere.
        /// </summary>
        /// <param name="serializerBuilder">The serializer builder.</param>
        /// <returns>The serializer builder for chaining.</returns>
        public static ISerializerBuilder AddGranvilleAssemblies(this ISerializerBuilder serializerBuilder)
        {
            // Force load Granville assemblies by referencing their types
            // This ensures they're loaded before we try to find them
            var granvilleSerializationType = typeof(Orleans.Serialization.Serializer);
            var granvilleCoreAbstractionsType = typeof(Orleans.Metadata.GrainManifest);
            var granvilleCoreType = typeof(Orleans.MembershipTableData);
            var granvilleRuntimeType = typeof(Orleans.Runtime.SiloAddress);

            // Add the actual Granville assemblies (not the shims)
            serializerBuilder.AddAssembly(granvilleSerializationType.Assembly); // Granville.Orleans.Serialization
            serializerBuilder.AddAssembly(granvilleCoreAbstractionsType.Assembly); // Granville.Orleans.Core.Abstractions
            serializerBuilder.AddAssembly(granvilleCoreType.Assembly); // Granville.Orleans.Core
            serializerBuilder.AddAssembly(granvilleRuntimeType.Assembly); // Granville.Orleans.Runtime

            // Also add Orleans.Persistence.Memory if it's loaded (for storage-related types)
            try
            {
                var memoryStorageType = Type.GetType("Orleans.Storage.MemoryGrainStorage, Orleans.Persistence.Memory");
                if (memoryStorageType != null)
                {
                    serializerBuilder.AddAssembly(memoryStorageType.Assembly);
                }
            }
            catch
            {
                // Ignore if Orleans.Persistence.Memory is not available
            }

            return serializerBuilder;
        }

        private static void EnsureGranvilleAssembliesLoaded()
        {
            try
            {
                // Force load core Granville assemblies
                _ = typeof(Orleans.Serialization.Serializer).Assembly;
                _ = typeof(Orleans.Metadata.GrainManifest).Assembly;
                _ = typeof(Orleans.MembershipTableData).Assembly;
                _ = typeof(Orleans.Runtime.SiloAddress).Assembly;
            }
            catch
            {
                // Best effort - don't fail if assemblies aren't available
            }
        }
    }
}