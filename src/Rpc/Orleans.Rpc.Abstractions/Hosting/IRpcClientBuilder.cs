using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Granville.Rpc.Hosting
{
    /// <summary>
    /// Builder interface for configuring an Orleans RPC client.
    /// </summary>
    public interface IRpcClientBuilder
    {
        /// <summary>
        /// Gets the service collection.
        /// </summary>
        IServiceCollection Services { get; }

        /// <summary>
        /// Gets the configuration.
        /// </summary>
        IConfiguration Configuration { get; }
    }
}