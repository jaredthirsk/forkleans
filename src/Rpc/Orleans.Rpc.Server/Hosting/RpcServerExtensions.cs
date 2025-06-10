using System;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Forkleans.Rpc.Configuration;
using Forkleans.Rpc.Hosting;

namespace Forkleans.Rpc
{
    /// <summary>
    /// Extension methods for configuring RPC server.
    /// </summary>
    public static class RpcServerExtensions
    {
        /// <summary>
        /// Configures the RPC server endpoint.
        /// </summary>
        public static IRpcServerBuilder ConfigureEndpoint(this IRpcServerBuilder builder, int port)
        {
            return builder.ConfigureEndpoint(new IPEndPoint(IPAddress.Any, port));
        }

        /// <summary>
        /// Configures the RPC server endpoint.
        /// </summary>
        public static IRpcServerBuilder ConfigureEndpoint(this IRpcServerBuilder builder, IPEndPoint endpoint)
        {
            builder.Services.Configure<RpcServerOptions>(options =>
            {
                options.ListenEndpoint = endpoint;
            });
            return builder;
        }
    }
}