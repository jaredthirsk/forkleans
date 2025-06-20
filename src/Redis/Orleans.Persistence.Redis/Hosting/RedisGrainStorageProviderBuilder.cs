using Forkleans.Providers;
using Microsoft.Extensions.Configuration;
using Forkleans;
using Forkleans.Hosting;
using StackExchange.Redis;
using Microsoft.Extensions.Options;
using Forkleans.Persistence;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Forkleans.Storage;

[assembly: RegisterProvider("Redis", "GrainStorage", "Silo", typeof(RedisGrainStorageProviderBuilder))]
[assembly: RegisterProvider("AzureRedisCache", "GrainStorage", "Silo", typeof(RedisGrainStorageProviderBuilder))]

namespace Forkleans.Hosting;

internal sealed class RedisGrainStorageProviderBuilder : IProviderBuilder<ISiloBuilder>
{
    public void Configure(ISiloBuilder builder, string name, IConfigurationSection configurationSection)
    {
        builder.AddRedisGrainStorage(name, (OptionsBuilder<RedisStorageOptions> optionsBuilder) =>
        {
            optionsBuilder.Configure<IServiceProvider>((options, services) =>
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

                var serializerKey = configurationSection["SerializerKey"];
                if (!string.IsNullOrEmpty(serializerKey))
                {
                    options.GrainStorageSerializer = services.GetRequiredKeyedService<IGrainStorageSerializer>(serializerKey);
                }
            });
        });
    }
}
