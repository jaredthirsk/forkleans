using System;

namespace Forkleans.Rpc
{
    /// <summary>
    /// Marks a grain interface method as an RPC method with specific delivery guarantees.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class RpcMethodAttribute : Attribute
    {
        /// <summary>
        /// Gets or sets the delivery mode for this RPC method.
        /// </summary>
        public RpcDeliveryMode DeliveryMode { get; set; } = RpcDeliveryMode.Reliable;

        /// <summary>
        /// Gets or sets whether this method supports IAsyncEnumerable responses.
        /// </summary>
        public bool SupportsAsyncEnumerable { get; set; }

        /// <summary>
        /// Gets or sets the maximum message size in bytes.
        /// </summary>
        public int MaxMessageSize { get; set; } = 65536; // 64KB default

        /// <summary>
        /// Gets or sets whether to compress the message payload.
        /// </summary>
        public bool EnableCompression { get; set; }
    }

    /// <summary>
    /// Specifies the delivery mode for RPC messages.
    /// </summary>
    public enum RpcDeliveryMode
    {
        /// <summary>
        /// Best-effort delivery. Messages may be lost.
        /// </summary>
        Unreliable,

        /// <summary>
        /// Reliable delivery with retries. Messages are guaranteed to be delivered.
        /// </summary>
        Reliable,

        /// <summary>
        /// Reliable and ordered delivery. Messages are delivered in order.
        /// </summary>
        ReliableOrdered
    }
}