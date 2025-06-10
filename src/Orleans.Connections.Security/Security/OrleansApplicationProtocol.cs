using System.Net.Security;

namespace Forkleans.Connections.Security
{
    internal static class OrleansApplicationProtocol
    {
        public static readonly SslApplicationProtocol Orleans1 = new SslApplicationProtocol("Orleans1");
    }
}
