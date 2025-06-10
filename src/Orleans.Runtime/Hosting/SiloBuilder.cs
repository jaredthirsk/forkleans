using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Forkleans.Hosting
{
    /// <summary>
    /// Builder for configuring an Orleans server.
    /// </summary>
    internal class SiloBuilder : ISiloBuilder
    {
        public SiloBuilder(IServiceCollection services, IConfiguration configuration)
        {
            Services = services;
            Configuration = configuration;
            DefaultSiloServices.AddDefaultServices(this);
        }

        public IServiceCollection Services { get; }
        public IConfiguration Configuration { get; }
    }
}
