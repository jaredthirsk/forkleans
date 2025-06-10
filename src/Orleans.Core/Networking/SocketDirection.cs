namespace Forkleans.Messaging
{
    internal enum ConnectionDirection : byte
    {
        SiloToSilo,
        ClientToGateway,
        GatewayToClient
    }
}
