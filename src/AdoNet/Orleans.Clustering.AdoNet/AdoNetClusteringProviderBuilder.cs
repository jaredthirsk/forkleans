using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Forkleans;
using Forkleans.Configuration;
using Forkleans.Hosting;
using Forkleans.Providers;

[assembly: RegisterProvider("AdoNet", "Clustering", "Silo", typeof(AdoNetClusteringProviderBuilder))]
[assembly: RegisterProvider("AdoNet", "Clustering", "Client", typeof(AdoNetClusteringProviderBuilder))]

namespace Forkleans.Hosting;

internal sealed class AdoNetClusteringProviderBuilder : IProviderBuilder<ISiloBuilder>, IProviderBuilder<IClientBuilder>
{
    public void Configure(ISiloBuilder builder, string name, IConfigurationSection configurationSection)
    {
        builder.UseAdoNetClustering((OptionsBuilder<AdoNetClusteringSiloOptions> optionsBuilder) => optionsBuilder.Configure<IServiceProvider>((options, services) =>
            {
                var invariant = configurationSection[nameof(options.Invariant)];
                if (!string.IsNullOrEmpty(invariant))
                {
                    options.Invariant = invariant;
                }

                var connectionString = configurationSection[nameof(options.ConnectionString)];
                var connectionName = configurationSection["ConnectionName"];
                if (string.IsNullOrEmpty(connectionString) && !string.IsNullOrEmpty(connectionName))
                {
                    connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString(connectionName);
                }

                if (!string.IsNullOrEmpty(connectionString))
                {
                    options.ConnectionString = connectionString;
                }
            }));
    }

    public void Configure(IClientBuilder builder, string name, IConfigurationSection configurationSection)
    {
        builder.UseAdoNetClustering((OptionsBuilder<AdoNetClusteringClientOptions> optionsBuilder) => optionsBuilder.Configure<IServiceProvider>((options, services) =>
            {
                var invariant = configurationSection[nameof(options.Invariant)];
                if (!string.IsNullOrEmpty(invariant))
                {
                    options.Invariant = invariant;
                }

                var connectionString = configurationSection[nameof(options.ConnectionString)];
                var connectionName = configurationSection["ConnectionName"];
                if (string.IsNullOrEmpty(connectionString) && !string.IsNullOrEmpty(connectionName))
                {
                    connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString(connectionName);
                }

                if (!string.IsNullOrEmpty(connectionString))
                {
                    options.ConnectionString = connectionString;
                }
            }));
    }
}
