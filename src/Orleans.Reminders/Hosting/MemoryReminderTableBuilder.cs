using Microsoft.Extensions.Configuration;
using Forkleans;
using Forkleans.Hosting;
using Forkleans.Providers;
using Forkleans.Runtime.Hosting.ProviderConfiguration;

[assembly: RegisterProvider("Memory", "Reminders", "Silo", typeof(MemoryReminderTableBuilder))]

namespace Forkleans.Runtime.Hosting.ProviderConfiguration;

internal sealed class MemoryReminderTableBuilder : IProviderBuilder<ISiloBuilder>
{
    public void Configure(ISiloBuilder builder, string name, IConfigurationSection configurationSection)
    {
        builder.UseInMemoryReminderService();
    }
}
