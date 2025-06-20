using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Forkleans.Configuration;
using Forkleans.Providers;
using Forkleans.Runtime;
using Forkleans.Transactions.Abstractions;
using Forkleans.Transactions.AzureStorage;

namespace Forkleans.Hosting
{
    /// <summary>
    /// <see cref="IServiceCollection"/> extensions.
    /// </summary>
    public static class AzureTableTransactionServicecollectionExtensions
    {
        internal static IServiceCollection AddAzureTableTransactionalStateStorage(this IServiceCollection services, string name,
            Action<OptionsBuilder<AzureTableTransactionalStateOptions>> configureOptions = null)
        {
            configureOptions?.Invoke(services.AddOptions<AzureTableTransactionalStateOptions>(name));
            services.AddTransient<IConfigurationValidator>(sp => new AzureTableTransactionalStateOptionsValidator(sp.GetRequiredService<IOptionsMonitor<AzureTableTransactionalStateOptions>>().Get(name), name));

            services.TryAddSingleton<ITransactionalStateStorageFactory>(sp => sp.GetKeyedService<ITransactionalStateStorageFactory>(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME));
            services.AddKeyedSingleton<ITransactionalStateStorageFactory>(name, (sp, key) => AzureTableTransactionalStateStorageFactory.Create(sp, key as string));
            services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>>(s => (ILifecycleParticipant<ISiloLifecycle>)s.GetRequiredKeyedService<ITransactionalStateStorageFactory>(name));

            return services;
        }
    }
}
