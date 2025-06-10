using System;
using Microsoft.Extensions.DependencyInjection;
using Forkleans.Runtime;
using Forkleans.Storage;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Forkleans.Configuration;
using Forkleans.Providers;
using Forkleans.Runtime.Hosting;

namespace Forkleans.Hosting
{
    /// <summary>
    /// <see cref="IServiceCollection"/> extensions.
    /// </summary>
    public static class DynamoDBGrainStorageServiceCollectionExtensions
    {
        /// <summary>
        /// Configure silo to use AWS DynamoDB storage as the default grain storage.
        /// </summary>
        public static IServiceCollection AddDynamoDBGrainStorageAsDefault(this IServiceCollection services, Action<DynamoDBStorageOptions> configureOptions)
        {
            return services.AddDynamoDBGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, ob => ob.Configure(configureOptions));
        }

        /// <summary>
        /// Configure silo to use AWS DynamoDB storage for grain storage.
        /// </summary>
        public static IServiceCollection AddDynamoDBGrainStorage(this IServiceCollection services, string name, Action<DynamoDBStorageOptions> configureOptions)
        {
            return services.AddDynamoDBGrainStorage(name, ob => ob.Configure(configureOptions));
        }

        /// <summary>
        /// Configure silo to use AWS DynamoDB storage as the default grain storage.
        /// </summary>
        public static IServiceCollection AddDynamoDBGrainStorageAsDefault(this IServiceCollection services, Action<OptionsBuilder<DynamoDBStorageOptions>> configureOptions = null)
        {
            return services.AddDynamoDBGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
        }

        /// <summary>
        /// Configure silo to use AWS DynamoDB storage for grain storage.
        /// </summary>
        public static IServiceCollection AddDynamoDBGrainStorage(this IServiceCollection services, string name,
            Action<OptionsBuilder<DynamoDBStorageOptions>> configureOptions = null)
        {
            configureOptions?.Invoke(services.AddOptions<DynamoDBStorageOptions>(name));
            services.AddTransient<IConfigurationValidator>(sp => new DynamoDBGrainStorageOptionsValidator(sp.GetRequiredService<IOptionsMonitor<DynamoDBStorageOptions>>().Get(name), name));
            services.ConfigureNamedOptionForLogging<DynamoDBStorageOptions>(name);
            services.AddTransient<IPostConfigureOptions<DynamoDBStorageOptions>, DefaultStorageProviderSerializerOptionsConfigurator<DynamoDBStorageOptions>>();
            return services.AddGrainStorage(name, DynamoDBGrainStorageFactory.Create);
        }
    }
}
