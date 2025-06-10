using System.Net;

namespace Forkleans.Rpc
{
    /// <summary>
    /// Details about the local RPC server instance.
    /// </summary>
    public interface ILocalRpcServerDetails
    {
        /// <summary>
        /// Gets the server name.
        /// </summary>
        string ServerName { get; }

        /// <summary>
        /// Gets the server endpoint.
        /// </summary>
        IPEndPoint ServerEndpoint { get; }

        /// <summary>
        /// Gets the server ID.
        /// </summary>
        string ServerId { get; }
    }
}