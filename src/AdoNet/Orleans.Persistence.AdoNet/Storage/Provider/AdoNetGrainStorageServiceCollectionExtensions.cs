using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Forkleans.Configuration;
using Forkleans.Hosting;
using Forkleans.Providers;
using Forkleans.Runtime;
using Forkleans.Runtime.Hosting;
using Forkleans.Storage;

namespace Forkleans.Hosting
{
    /// <summary>
    /// <see cref="IServiceCollection"/> extensions.
    /// </summary>
    public static class AdoNetGrainStorageServiceCollectionExtensions
    {
        /// <summary>
        /// Configure silo to use  AdoNet grain storage as the default grain storage. Instructions on configuring your database are available at <see href="http://aka.ms/orleans-sql-scripts"/>.
        /// </summary>
        /// <remarks>
        /// Instructions on configuring your database are available at <see href="http://aka.ms/orleans-sql-scripts"/>.
        /// </remarks>
        public static IServiceCollection AddAdoNetGrainStorage(this IServiceCollection services, Action<AdoNetGrainStorageOptions> configureOptions)
        {
            return services.AddAdoNetGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, ob => ob.Configure(configureOptions));
        }

        /// <summary>
        /// Configure silo to use AdoNet grain storage for grain storage. Instructions on configuring your database are available at <see href="http://aka.ms/orleans-sql-scripts"/>.
        /// </summary>
        /// <remarks>
        /// Instructions on configuring your database are available at <see href="http://aka.ms/orleans-sql-scripts"/>.
        /// </remarks>
        public static IServiceCollection AddAdoNetGrainStorage(this IServiceCollection services, string name, Action<AdoNetGrainStorageOptions> configureOptions)
        {
            return services.AddAdoNetGrainStorage(name, ob => ob.Configure(configureOptions));
        }

        /// <summary>
        /// Configure silo to use AdoNet grain storage as the default grain storage. Instructions on configuring your database are available at <see href="http://aka.ms/orleans-sql-scripts"/>.
        /// </summary>
        /// <remarks>
        /// Instructions on configuring your database are available at <see href="http://aka.ms/orleans-sql-scripts"/>.
        /// </remarks>
        public static IServiceCollection AddAdoNetGrainStorageAsDefault(this IServiceCollection services, Action<OptionsBuilder<AdoNetGrainStorageOptions>> configureOptions = null)
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
            Action<OptionsBuilder<AdoNetGrainStorageOptions>> configureOptions = null)
        {
            configureOptions?.Invoke(services.AddOptions<AdoNetGrainStorageOptions>(name));
            services.ConfigureNamedOptionForLogging<AdoNetGrainStorageOptions>(name);
            services.AddTransient<IPostConfigureOptions<AdoNetGrainStorageOptions>, DefaultStorageProviderSerializerOptionsConfigurator<AdoNetGrainStorageOptions>>();
            services.AddTransient<IPostConfigureOptions<AdoNetGrainStorageOptions>, DefaultAdoNetGrainStorageOptionsHashPickerConfigurator>();
            services.AddTransient<IConfigurationValidator>(sp => new AdoNetGrainStorageOptionsValidator(sp.GetRequiredService<IOptionsMonitor<AdoNetGrainStorageOptions>>().Get(name), name));
            return services.AddGrainStorage(name, AdoNetGrainStorageFactory.Create);
        }
    } 
}
