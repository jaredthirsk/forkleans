using System.Collections.Generic;
using System.Net;
using Microsoft.Extensions.Configuration;
using Forkleans;
using Forkleans.Hosting;
using Forkleans.Providers;

[assembly: RegisterProvider("Development", "Clustering", "Client", typeof(StaticGatewayListProviderBuilder))]
[assembly: RegisterProvider("Static", "Clustering", "Client", typeof(StaticGatewayListProviderBuilder))]

namespace Forkleans.Providers;

internal sealed class StaticGatewayListProviderBuilder : IProviderBuilder<IClientBuilder>
{
    public void Configure(IClientBuilder builder, string name, IConfigurationSection configurationSection)
    {
        var endpoints = new List<IPEndPoint>();
        var gatewaysSection = configurationSection.GetSection("Gateways");
        foreach (var child in gatewaysSection.GetChildren())
        {
            if (IPEndPoint.TryParse(child.Value, out var ep))
            {
                endpoints.Add(ep);
            }
        }

        builder.UseStaticClustering([.. endpoints]);
    }
}
