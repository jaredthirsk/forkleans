using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Granville.Rpc.Hosting
{
    /// <summary>
    /// Builder for configuring an Orleans RPC client.
    /// </summary>
    internal class RpcClientBuilder : IRpcClientBuilder
    {
        public RpcClientBuilder(IServiceCollection services, IConfiguration configuration)
        {
            Services = services;
            Configuration = configuration;
            DefaultRpcClientServices.AddDefaultServices(this);
        }

        public IServiceCollection Services { get; }
        public IConfiguration Configuration { get; }
    }
}