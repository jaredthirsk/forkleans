using System;

namespace Forkleans.Connections.Security
{
    public interface ITlsApplicationProtocolFeature
    {
        ReadOnlyMemory<byte> ApplicationProtocol { get; }
    }
}
