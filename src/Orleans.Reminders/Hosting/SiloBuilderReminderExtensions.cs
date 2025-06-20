using Microsoft.Extensions.DependencyInjection;
using Forkleans.Runtime;
using Forkleans.Configuration.Internal;
using System.Linq;
using Forkleans.Runtime.ReminderService;
using Forkleans.Timers;

namespace Forkleans.Hosting;

public static class SiloBuilderReminderExtensions
{
    /// <summary>
    /// Adds support for reminders to this silo.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <returns>The silo builder.</returns>
    public static ISiloBuilder AddReminders(this ISiloBuilder builder) => builder.ConfigureServices(services => AddReminders(services));

    /// <summary>
    /// Add support for reminders to this client.
    /// </summary>
    /// <param name="services">The services.</param>
    public static void AddReminders(this IServiceCollection services)
    {
        if (services.Any(service => service.ServiceType.Equals(typeof(LocalReminderService))))
        {
            return;
        }

        services.AddSingleton<LocalReminderService>();
        services.AddFromExisting<IReminderService, LocalReminderService>();
        services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, LocalReminderService>();
        services.AddSingleton<IReminderRegistry, ReminderRegistry>();
    }
}
