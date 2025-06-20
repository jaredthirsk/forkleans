using System.Net;
using Microsoft.Extensions.Options;
using Forkleans.Configuration;
using Forkleans.Runtime;
using Forkleans.Runtime.Configuration;

namespace Forkleans
{
    internal class LocalClientDetails
    {
        public LocalClientDetails(IOptions<ClientMessagingOptions> clientMessagingOptions)
        {
            var options = clientMessagingOptions.Value;
            var ipAddress = options.LocalAddress ?? ConfigUtilities.GetLocalIPAddress(options.PreferredFamily, options.NetworkInterfaceName);

            // Client generations are negative
            var generation = -SiloAddress.AllocateNewGeneration();
            ClientAddress = SiloAddress.New(ipAddress, 0, generation);
            ClientId = ClientGrainId.Create();
        }

        public ClientGrainId ClientId { get; }
        public IPAddress IPAddress => ClientAddress.Endpoint.Address;
        public SiloAddress ClientAddress { get; }
    }
}