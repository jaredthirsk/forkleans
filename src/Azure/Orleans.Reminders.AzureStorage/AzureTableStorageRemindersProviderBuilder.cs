using System;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Forkleans;
using Forkleans.Configuration;
using Forkleans.Hosting;
using Forkleans.Providers;
using Forkleans.Reminders.AzureStorage;

[assembly: RegisterProvider("AzureTableStorage", "Reminders", "Silo", typeof(AzureTableStorageRemindersProviderBuilder))]

namespace Forkleans.Hosting;

internal sealed class AzureTableStorageRemindersProviderBuilder : IProviderBuilder<ISiloBuilder>
{
    public void Configure(ISiloBuilder builder, string name, IConfigurationSection configurationSection)
    {
        builder.UseAzureTableReminderService((OptionsBuilder<AzureTableReminderStorageOptions> optionsBuilder) =>
            optionsBuilder.Configure<IServiceProvider>((options, services) =>
            {
                var tableName = configurationSection["TableName"];
                if (!string.IsNullOrEmpty(tableName))
                {
                    options.TableName = tableName; 
                }

                var serviceKey = configurationSection["ServiceKey"];
                if (!string.IsNullOrEmpty(serviceKey))
                {
                    // Get a client by name.
                    options.TableServiceClient = services.GetRequiredKeyedService<TableServiceClient>(serviceKey);
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
                        if (Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
                        {
                            options.TableServiceClient = new(uri);
                        }
                        else
                        {
                            options.TableServiceClient = new(connectionString);
                        }
                    }
                }
            }));
    }
}
