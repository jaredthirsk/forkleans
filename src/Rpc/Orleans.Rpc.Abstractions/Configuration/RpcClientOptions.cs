using System;
using System.Collections.Generic;
using System.Net;

namespace Forkleans.Rpc.Configuration
{
    /// <summary>
    /// Options for configuring the RPC client.
    /// </summary>
    public class RpcClientOptions
    {
        /// <summary>
        /// Gets or sets the client ID.
        /// </summary>
        public string ClientId { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Gets or sets the connection timeout in milliseconds.
        /// </summary>
        public int ConnectionTimeoutMs { get; set; } = 30000;

        /// <summary>
        /// Gets or sets the request timeout in milliseconds.
        /// </summary>
        public int RequestTimeoutMs { get; set; } = 30000;

        /// <summary>
        /// Gets or sets the maximum retry attempts.
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Gets or sets the retry delay in milliseconds.
        /// </summary>
        public int RetryDelayMs { get; set; } = 1000;

        /// <summary>
        /// Gets or sets the server endpoints to connect to.
        /// </summary>
        public List<IPEndPoint> ServerEndpoints { get; set; } = new List<IPEndPoint>();
    }
}