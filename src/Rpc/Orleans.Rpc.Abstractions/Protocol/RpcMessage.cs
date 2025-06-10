using System;
using Forkleans;
using Forkleans.Runtime;
using Forkleans.Serialization;

namespace Forkleans.Rpc.Protocol
{
    /// <summary>
    /// Base class for all RPC messages.
    /// </summary>
    [GenerateSerializer]
    public abstract class RpcMessage
    {
        /// <summary>
        /// Unique message ID for correlation.
        /// </summary>
        [Id(0)]
        public Guid MessageId { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Timestamp when the message was created.
        /// </summary>
        [Id(1)]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Request message for RPC calls.
    /// </summary>
    [GenerateSerializer]
    public class RpcRequest : RpcMessage
    {
        /// <summary>
        /// Target grain ID.
        /// </summary>
        [Id(2)]
        public GrainId GrainId { get; set; }

        /// <summary>
        /// Interface type.
        /// </summary>
        [Id(3)]
        public GrainInterfaceType InterfaceType { get; set; }

        /// <summary>
        /// Method ID to invoke.
        /// </summary>
        [Id(4)]
        public int MethodId { get; set; }

        /// <summary>
        /// Serialized arguments.
        /// </summary>
        [Id(5)]
        public byte[] Arguments { get; set; }

        /// <summary>
        /// Request timeout in milliseconds.
        /// </summary>
        [Id(6)]
        public int TimeoutMs { get; set; } = 30000;
    }

    /// <summary>
    /// Response message for RPC calls.
    /// </summary>
    [GenerateSerializer]
    public class RpcResponse : RpcMessage
    {
        /// <summary>
        /// ID of the request this is responding to.
        /// </summary>
        [Id(2)]
        public Guid RequestId { get; set; }

        /// <summary>
        /// Whether the call succeeded.
        /// </summary>
        [Id(3)]
        public bool Success { get; set; }

        /// <summary>
        /// Serialized result (if success) or exception (if failure).
        /// </summary>
        [Id(4)]
        public byte[] Payload { get; set; }

        /// <summary>
        /// Error message if the call failed.
        /// </summary>
        [Id(5)]
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Heartbeat message to keep connections alive.
    /// </summary>
    [GenerateSerializer]
    public class RpcHeartbeat : RpcMessage
    {
        /// <summary>
        /// Client or server ID.
        /// </summary>
        [Id(2)]
        public string SourceId { get; set; }
    }

    /// <summary>
    /// Connection handshake message.
    /// </summary>
    [GenerateSerializer]
    public class RpcHandshake : RpcMessage
    {
        /// <summary>
        /// Client ID.
        /// </summary>
        [Id(2)]
        public string ClientId { get; set; }

        /// <summary>
        /// Protocol version.
        /// </summary>
        [Id(3)]
        public int ProtocolVersion { get; set; } = 1;

        /// <summary>
        /// Supported features.
        /// </summary>
        [Id(4)]
        public string[] Features { get; set; } = Array.Empty<string>();
    }
}