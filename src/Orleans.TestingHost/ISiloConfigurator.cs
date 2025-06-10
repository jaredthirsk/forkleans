using Forkleans.Hosting;

namespace Forkleans.TestingHost
{
    /// <summary>
    /// Allows implementations to configure the silo builder when starting up each silo in the test cluster.
    /// </summary>
    public interface ISiloConfigurator
    {
        /// <summary>
        /// Configures the silo builder.
        /// </summary>
        /// <param name="siloBuilder">The silo builder.</param>
        void Configure(ISiloBuilder siloBuilder);
    }
}