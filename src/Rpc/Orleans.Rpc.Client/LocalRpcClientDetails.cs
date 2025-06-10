using System;
using System.Net;
using Microsoft.Extensions.Options;
using Forkleans.Configuration;
using Forkleans.Rpc.Configuration;
using Forkleans.Runtime;

namespace Forkleans.Rpc
{
    /// <summary>
    /// Local RPC client details implementation.
    /// </summary>
    internal sealed class LocalRpcClientDetails : ILocalClientDetails
    {
        private readonly string _clientId;
        private readonly SiloAddress _clientAddress;

        public LocalRpcClientDetails(IOptions<RpcClientOptions> rpcOptions)
        {
            var options = rpcOptions.Value;
            _clientId = options.ClientId;
            
            // Create a dummy client address for RPC mode
            var ipAddress = IPAddress.Loopback;
            var generation = -SiloAddress.AllocateNewGeneration();
            _clientAddress = SiloAddress.New(ipAddress, 0, generation);
        }

        public string ClientId => _clientId;
        
        public SiloAddress ClientAddress => _clientAddress;
    }
}