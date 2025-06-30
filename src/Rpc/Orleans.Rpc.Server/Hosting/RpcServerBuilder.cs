using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Granville.Rpc.Hosting
{
    /// <summary>
    /// Builder for configuring an Orleans RPC server.
    /// </summary>
    internal class RpcServerBuilder : IRpcServerBuilder
    {
        public RpcServerBuilder(IServiceCollection services, IConfiguration configuration)
        {
            Services = services;
            Configuration = configuration;
            DefaultRpcServerServices.AddDefaultServices(this);
        }

        public IServiceCollection Services { get; }
        public IConfiguration Configuration { get; }
    }
}