using Forkleans.Providers;
using Microsoft.Extensions.Configuration;
using Forkleans;
using Forkleans.Hosting;
using StackExchange.Redis;
using Forkleans.Clustering.Redis.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

[assembly: RegisterProvider("Redis", "Clustering", "Silo", typeof(RedisClusteringProviderBuilder))]
[assembly: RegisterProvider("AzureRedisCache", "Clustering", "Silo", typeof(RedisClusteringProviderBuilder))]

[assembly: RegisterProvider("Redis", "Clustering", "Client", typeof(RedisClusteringProviderBuilder))]
[assembly: RegisterProvider("AzureRedisCache", "Clustering", "Client", typeof(RedisClusteringProviderBuilder))]

namespace Forkleans.Clustering.Redis.Hosting;

internal sealed class RedisClusteringProviderBuilder : IProviderBuilder<ISiloBuilder>, IProviderBuilder<IClientBuilder>
{
    public void Configure(ISiloBuilder builder, string name, IConfigurationSection configurationSection)
    {
        builder.UseRedisClustering(_ => { });
        builder.Services.AddOptions<RedisClusteringOptions>()
            .Configure<IServiceProvider>((options, services) =>
            {
                var serviceKey = configurationSection["ServiceKey"];
                if (!string.IsNullOrEmpty(serviceKey))
                {
                    // Get a connection multiplexer instance by name.
                    var multiplexer = services.GetRequiredKeyedService<IConnectionMultiplexer>(serviceKey);
                    options.CreateMultiplexer = _ => Task.FromResult(multiplexer);
                    options.ConfigurationOptions = new ConfigurationOptions();
                }
                else
                {
                    // Construct a connection multiplexer from a connection string.
                    var connectionName = configurationSection["ConnectionName"];
                    var connectionString = configurationSection["ConnectionString"];
                    if (!string.IsNullOrEmpty(connectionName) && string.IsNullOrEmpty(connectionString))
                    {
                        var rootConfiguration = services.GetRequiredService<IConfiguration>();
                        connectionString = rootConfiguration.GetConnectionString(connectionName);
                    }

                    if (!string.IsNullOrEmpty(connectionString))
                    {
                        options.ConfigurationOptions = ConfigurationOptions.Parse(connectionString);
                    }
                }
            });
    }

    public void Configure(IClientBuilder builder, string name, IConfigurationSection configurationSection)
    {
        builder.UseRedisClustering(_ => { });
        builder.Services.AddOptions<RedisClusteringOptions>()
            .Configure<IServiceProvider>((options, services) =>
            {
                var serviceKey = configurationSection["ServiceKey"];
                if (!string.IsNullOrEmpty(serviceKey))
                {
                    // Get a connection multiplexer instance by name.
                    var multiplexer = services.GetRequiredKeyedService<IConnectionMultiplexer>(serviceKey);
                    options.CreateMultiplexer = _ => Task.FromResult(multiplexer);
                    options.ConfigurationOptions = new ConfigurationOptions();
                }
                else
                {
                    // Construct a connection multiplexer from a connection string.
                    var connectionName = configurationSection["ConnectionName"];
                    var connectionString = configurationSection["ConnectionString"];
                    if (!string.IsNullOrEmpty(connectionName) && string.IsNullOrEmpty(connectionString))
                    {
                        var rootConfiguration = services.GetRequiredService<IConfiguration>();
                        connectionString = rootConfiguration.GetConnectionString(connectionName);
                    }

                    if (!string.IsNullOrEmpty(connectionString))
                    {
                        options.ConfigurationOptions = ConfigurationOptions.Parse(connectionString);
                    }
                }
            });
    }
}
