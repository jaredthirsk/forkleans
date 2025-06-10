using System;
using System.Net;
using Microsoft.Extensions.Options;
using Forkleans.Rpc.Configuration;

namespace Forkleans.Rpc
{
    /// <summary>
    /// Implementation of ILocalRpcServerDetails.
    /// </summary>
    internal class LocalRpcServerDetails : ILocalRpcServerDetails
    {
        public LocalRpcServerDetails(IOptions<RpcServerOptions> options)
        {
            var opt = options.Value;
            ServerName = opt.ServerName ?? "RpcServer";
            ServerId = Guid.NewGuid().ToString("N");
            ServerEndpoint = opt.ListenEndpoint ?? new IPEndPoint(IPAddress.Any, opt.Port);
        }

        public string ServerName { get; }
        public IPEndPoint ServerEndpoint { get; }
        public string ServerId { get; }
    }
}