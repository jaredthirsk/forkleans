using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Forkleans.Configuration;
using Forkleans.Runtime;
using Forkleans.Runtime.Placement;
using Forkleans.TestingHost;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;

namespace TestVersionGrains
{
    public class VersionGrainsSiloBuilderConfigurator : IHostConfigurator
    {
        public void Configure(IHostBuilder hostBuilder)
        {
            var cfg = hostBuilder.GetConfiguration();
            var siloCount = int.Parse(cfg["SiloCount"]);
            hostBuilder.UseOrleans((ctx, siloBuilder) =>
            {
                siloBuilder.Configure<SiloMessagingOptions>(options => options.AssumeHomogenousSilosForTesting = false);
                siloBuilder.Configure<GrainVersioningOptions>(options =>
                {
                    options.DefaultCompatibilityStrategy = cfg["CompatibilityStrategy"];
                    options.DefaultVersionSelectorStrategy = cfg["VersionSelectorStrategy"];
                });

                siloBuilder.ConfigureServices(ConfigureServices)
                    .AddMemoryGrainStorageAsDefault();
            });
        }

        private void ConfigureServices(IServiceCollection services)
        {
            services.AddPlacementDirector<VersionAwarePlacementStrategy, VersionAwarePlacementDirector>();
        }
    }
}
