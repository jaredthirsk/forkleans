namespace Forkleans.Rpc.Configuration
{
    /// <summary>
    /// Options for configuring the RPC transport layer.
    /// </summary>
    public class RpcTransportOptions
    {
        /// <summary>
        /// Gets or sets the transport type to use.
        /// </summary>
        public string TransportType { get; set; } = "LiteNetLib";

        /// <summary>
        /// Gets or sets the maximum packet size in bytes.
        /// </summary>
        public int MaxPacketSize { get; set; } = 1024 * 64; // 64KB

        /// <summary>
        /// Gets or sets the send buffer size.
        /// </summary>
        public int SendBufferSize { get; set; } = 1024 * 1024; // 1MB

        /// <summary>
        /// Gets or sets the receive buffer size.
        /// </summary>
        public int ReceiveBufferSize { get; set; } = 1024 * 1024; // 1MB

        /// <summary>
        /// Gets or sets whether to enable reliable delivery.
        /// </summary>
        public bool EnableReliableDelivery { get; set; } = true;

        /// <summary>
        /// Gets or sets the retry count for reliable messages.
        /// </summary>
        public int ReliableRetryCount { get; set; } = 3;

        /// <summary>
        /// Gets or sets the retry timeout in milliseconds.
        /// </summary>
        public int ReliableRetryTimeoutMs { get; set; } = 1000;
    }
}