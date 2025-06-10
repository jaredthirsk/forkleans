using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Forkleans.Hosting
{
    /// <summary>
    /// Builder for configuring an Orleans server.
    /// </summary>
    public interface ISiloBuilder
    {
        /// <summary>
        /// The services shared by the silo and host.
        /// </summary>
        IServiceCollection Services { get; }

        /// <summary>
        /// Gets the configuration.
        /// </summary>
        IConfiguration Configuration { get; }
    }
}