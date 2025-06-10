using System.Net;

namespace Forkleans.Rpc.Configuration
{
    /// <summary>
    /// Options for configuring the RPC server.
    /// </summary>
    public class RpcServerOptions
    {
        /// <summary>
        /// Gets or sets the server name.
        /// </summary>
        public string ServerName { get; set; }

        /// <summary>
        /// Gets or sets the port to listen on.
        /// </summary>
        public int Port { get; set; } = 11111;

        /// <summary>
        /// Gets or sets the maximum number of concurrent connections.
        /// </summary>
        public int MaxConnections { get; set; } = 1000;

        /// <summary>
        /// Gets or sets the connection timeout in milliseconds.
        /// </summary>
        public int ConnectionTimeoutMs { get; set; } = 30000;

        /// <summary>
        /// Gets or sets the endpoint to listen on.
        /// </summary>
        public IPEndPoint ListenEndpoint { get; set; }
    }
}