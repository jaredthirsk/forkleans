using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Storage;

namespace Orleans.Hosting
{
    /// <summary>
    /// <see cref="IServiceCollection"/> extensions.
    /// </summary>
    public static class MartenGrainStorageServiceCollectionExtensions
    {
        /// <summary>
        /// Configure silo to use  AdoNet grain storage as the default grain storage. Instructions on configuring your database are available at <see href="http://aka.ms/orleans-sql-scripts"/>.
        /// </summary>
        /// <remarks>
        /// Instructions on configuring your database are available at <see href="http://aka.ms/orleans-sql-scripts"/>.
        /// </remarks>
        public static IServiceCollection AddAdoNetGrainStorage(this IServiceCollection services, Action<MartenGrainStorageOptions> configureOptions)
        {
            return services.AddAdoNetGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, ob => ob.Configure(configureOptions));
        }

        /// <summary>
        /// Configure silo to use AdoNet grain storage for grain storage. Instructions on configuring your database are available at <see href="http://aka.ms/orleans-sql-scripts"/>.
        /// </summary>
        /// <remarks>
        /// Instructions on configuring your database are available at <see href="http://aka.ms/orleans-sql-scripts"/>.
        /// </remarks>
        public static IServiceCollection AddAdoNetGrainStorage(this IServiceCollection services, string name, Action<MartenGrainStorageOptions> configureOptions)
        {
            return services.AddAdoNetGrainStorage(name, ob => ob.Configure(configureOptions));
        }

        /// <summary>
        /// Configure silo to use AdoNet grain storage as the default grain storage. Instructions on configuring your database are available at <see href="http://aka.ms/orleans-sql-scripts"/>.
        /// </summary>
        /// <remarks>
        /// Instructions on configuring your database are available at <see href="http://aka.ms/orleans-sql-scripts"/>.
        /// </remarks>
        public static IServiceCollection AddAdoNetGrainStorageAsDefault(this IServiceCollection services, Action<OptionsBuilder<MartenGrainStorageOptions>> configureOptions = null)
        {
            return services.AddAdoNetGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
        }

        /// <summary>
        /// Configure silo to use AdoNet grain storage for grain storage. Instructions on configuring your database are available at <see href="http://aka.ms/orleans-sql-scripts"/>.
        /// </summary>
        /// <remarks>
        /// Instructions on configuring your database are available at <see href="http://aka.ms/orleans-sql-scripts"/>.
        /// </remarks>
        public static IServiceCollection AddAdoNetGrainStorage(this IServiceCollection services, string name,
            Action<OptionsBuilder<MartenGrainStorageOptions>> configureOptions = null)
        {
            configureOptions?.Invoke(services.AddOptions<MartenGrainStorageOptions>(name));
            services.ConfigureNamedOptionForLogging<MartenGrainStorageOptions>(name);
            if (string.Equals(name, ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, StringComparison.Ordinal))
            {
                services.TryAddSingleton(sp => sp.GetServiceByName<IGrainStorage>(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME));
            }
            services.AddTransient<IPostConfigureOptions<MartenGrainStorageOptions>, DefaultStorageProviderSerializerOptionsConfigurator<MartenGrainStorageOptions>>();
            services.AddTransient<IConfigurationValidator>(sp => new AdoNetGrainStorageOptionsValidator(sp.GetRequiredService<IOptionsMonitor<MartenGrainStorageOptions>>().Get(name), name));
            return services.AddSingletonNamedService<IGrainStorage>(name, MartenGrainStorageFactory.Create)
                           .AddSingletonNamedService<ILifecycleParticipant<ISiloLifecycle>>(name, (s, n) => (ILifecycleParticipant<ISiloLifecycle>)s.GetRequiredServiceByName<IGrainStorage>(n));
        }
    } 
}
