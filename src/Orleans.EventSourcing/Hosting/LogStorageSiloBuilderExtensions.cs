
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Forkleans.EventSourcing;
using Forkleans.Providers;
using Forkleans.Runtime;
using Forkleans.EventSourcing.LogStorage;

namespace Forkleans.Hosting
{
    public static class LogStorageSiloBuilderExtensions
    {
        /// <summary>
        /// Adds a log storage log consistency provider as default consistency provider"/>
        /// </summary>
        public static ISiloBuilder AddLogStorageBasedLogConsistencyProviderAsDefault(this ISiloBuilder builder)
        {
            return builder.AddLogStorageBasedLogConsistencyProvider(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME);
        }

        /// <summary>
        /// Adds a log storage log consistency provider"/>
        /// </summary>
        public static ISiloBuilder AddLogStorageBasedLogConsistencyProvider(this ISiloBuilder builder, string name = "LogStorage")
        {
            return builder.ConfigureServices(services => services.AddLogStorageBasedLogConsistencyProvider(name));
        }

        internal static IServiceCollection AddLogStorageBasedLogConsistencyProvider(this IServiceCollection services, string name)
        {
            services.AddLogConsistencyProtocolServicesFactory();
            services.TryAddSingleton<ILogViewAdaptorFactory>(sp => sp.GetKeyedService<ILogViewAdaptorFactory>(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME));
            return services.AddKeyedSingleton<ILogViewAdaptorFactory, LogConsistencyProvider>(name);
        }
    }
}
