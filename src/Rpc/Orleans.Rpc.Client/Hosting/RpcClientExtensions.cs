using System;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Forkleans.Rpc.Configuration;
using Forkleans.Rpc.Hosting;

namespace Forkleans.Rpc
{
    /// <summary>
    /// Extension methods for configuring RPC client.
    /// </summary>
    public static class RpcClientExtensions
    {
        /// <summary>
        /// Configures the RPC client to connect to a server endpoint.
        /// </summary>
        public static IRpcClientBuilder ConnectTo(this IRpcClientBuilder builder, string host, int port)
        {
            return builder.ConnectTo(new IPEndPoint(IPAddress.Parse(host), port));
        }

        /// <summary>
        /// Configures the RPC client to connect to a server endpoint.
        /// </summary>
        public static IRpcClientBuilder ConnectTo(this IRpcClientBuilder builder, IPEndPoint endpoint)
        {
            builder.Services.Configure<RpcClientOptions>(options =>
            {
                options.ServerEndpoints.Add(endpoint);
            });
            return builder;
        }
    }
}