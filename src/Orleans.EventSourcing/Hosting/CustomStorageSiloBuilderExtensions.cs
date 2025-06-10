
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Forkleans.EventSourcing;
using Forkleans.Providers;
using Forkleans.Runtime;
using Forkleans.EventSourcing.CustomStorage;
using Forkleans.Configuration;

namespace Forkleans.Hosting
{
    public static class CustomStorageSiloBuilderExtensions
    {
        /// <summary>
        /// Adds a custom storage log consistency provider as default consistency provider"/>
        /// </summary>
        public static ISiloBuilder AddCustomStorageBasedLogConsistencyProviderAsDefault(this ISiloBuilder builder, string primaryCluster = null)
        {
            return builder.AddCustomStorageBasedLogConsistencyProvider(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, primaryCluster);
        }

        /// <summary>
        /// Adds a custom storage log consistency provider"/>
        /// </summary>
        public static ISiloBuilder AddCustomStorageBasedLogConsistencyProvider(this ISiloBuilder builder, string name = "LogStorage", string primaryCluster = null)
        {
            return builder.ConfigureServices(services => services.AddCustomStorageBasedLogConsistencyProvider(name, primaryCluster));
        }

        internal static void AddCustomStorageBasedLogConsistencyProvider(this IServiceCollection services, string name, string primaryCluster)
        {
            services.AddLogConsistencyProtocolServicesFactory();
            services.AddOptions<CustomStorageLogConsistencyOptions>(name)
                    .Configure(options => options.PrimaryCluster = primaryCluster);
            services.ConfigureNamedOptionForLogging<CustomStorageLogConsistencyOptions>(name)
                .AddKeyedSingleton<ILogViewAdaptorFactory>(name, (sp, key) => LogConsistencyProviderFactory.Create(sp, key as string))
                .TryAddSingleton<ILogViewAdaptorFactory>(sp => sp.GetKeyedService<ILogViewAdaptorFactory>(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME));
        }
    }
}
