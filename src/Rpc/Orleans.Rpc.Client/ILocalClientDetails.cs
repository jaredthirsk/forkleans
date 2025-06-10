using Forkleans.Runtime;

namespace Forkleans.Rpc
{
    /// <summary>
    /// Local client details interface for RPC clients.
    /// </summary>
    internal interface ILocalClientDetails
    {
        string ClientId { get; }
        SiloAddress ClientAddress { get; }
    }
}