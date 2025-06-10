using System;

namespace Forkleans.Rpc
{
    /// <summary>
    /// Specifies RPC connection settings for a grain interface.
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface, AllowMultiple = false)]
    public class RpcConnectionAttribute : Attribute
    {
        /// <summary>
        /// Gets or sets the connection timeout in milliseconds.
        /// </summary>
        public int ConnectionTimeoutMs { get; set; } = 30000;

        /// <summary>
        /// Gets or sets whether to maintain a persistent connection.
        /// </summary>
        public bool PersistentConnection { get; set; } = true;

        /// <summary>
        /// Gets or sets the heartbeat interval in milliseconds for persistent connections.
        /// </summary>
        public int HeartbeatIntervalMs { get; set; } = 10000;

        /// <summary>
        /// Gets or sets the maximum number of concurrent requests on this connection.
        /// </summary>
        public int MaxConcurrentRequests { get; set; } = 100;
    }
}