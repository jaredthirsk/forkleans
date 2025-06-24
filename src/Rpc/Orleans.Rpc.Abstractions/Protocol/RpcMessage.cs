using System;
using System.Collections.Generic;
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
        public GrainId GrainId { get; set; } = default!

        /// <summary>
        /// Interface type.
        /// </summary>
        [Id(3)]
        public GrainInterfaceType InterfaceType { get; set; } = default!

        /// <summary>
        /// Method ID to invoke.
        /// </summary>
        [Id(4)]
        public int MethodId { get; set; }

        /// <summary>
        /// Serialized arguments.
        /// </summary>
        [Id(5)]
        public byte[] Arguments { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Request timeout in milliseconds.
        /// </summary>
        [Id(6)]
        public int TimeoutMs { get; set; } = 30000;

        /// <summary>
        /// Return type name for deserialization.
        /// </summary>
        [Id(7)]
        public string ReturnTypeName { get; set; } = string.Empty;

        /// <summary>
        /// Optional target zone ID for zone-aware routing.
        /// </summary>
        [Id(8)]
        public int? TargetZoneId { get; set; }
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
        public byte[] Payload { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Error message if the call failed.
        /// </summary>
        [Id(5)]
        public string ErrorMessage { get; set; } = string.Empty;
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
        public string SourceId { get; set; } = string.Empty;
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
        [Id(2)] // TODO FIXME: Fork cleanup - The Id should start at 0, even though there is a base class with serialized properties.
        public string ClientId { get; set; } = string.Empty;

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

    /// <summary>
    /// Handshake acknowledgment from server.
    /// </summary>
    [GenerateSerializer]
    public class RpcHandshakeAck : RpcMessage
    {
        /// <summary>
        /// Server ID.
        /// </summary>
        [Id(2)]
        public string ServerId { get; set; } = string.Empty;

        /// <summary>
        /// Protocol version accepted.
        /// </summary>
        [Id(3)]
        public int ProtocolVersion { get; set; } = 1;

        /// <summary>
        /// Grain manifest from the server.
        /// </summary>
        [Id(4)]
        public RpcGrainManifest GrainManifest { get; set; } = new();

        /// <summary>
        /// Zone ID that this server handles (for zone-aware servers).
        /// </summary>
        [Id(5)]
        public int? ZoneId { get; set; }

        /// <summary>
        /// Mapping of zone IDs to server IDs for zone routing.
        /// </summary>
        [Id(6)]
        public Dictionary<int, string> ZoneToServerMapping { get; set; } = new();
    }

    /// <summary>
    /// Simplified grain manifest for RPC.
    /// </summary>
    [GenerateSerializer]
    public class RpcGrainManifest
    {
        /// <summary>
        /// Mapping of interface type to grain type.
        /// </summary>
        [Id(0)]
        public Dictionary<string, string> InterfaceToGrainMappings { get; set; } = new();

        /// <summary>
        /// Grain type properties.
        /// </summary>
        [Id(1)]
        public Dictionary<string, Dictionary<string, string>> GrainProperties { get; set; } = new();

        /// <summary>
        /// Interface properties.
        /// </summary>
        [Id(2)]
        public Dictionary<string, Dictionary<string, string>> InterfaceProperties { get; set; } = new();
    }

    /// <summary>
    /// Request to start an IAsyncEnumerable method result.
    /// </summary>
    [GenerateSerializer]
    public class RpcAsyncEnumerableRequest : RpcMessage
    {
        /// <summary>
        /// Target grain ID.
        /// </summary>
        [Id(2)]
        public GrainId GrainId { get; set; } = default!

        /// <summary>
        /// Interface type.
        /// </summary>
        [Id(3)]
        public GrainInterfaceType InterfaceType { get; set; } = default!

        /// <summary>
        /// Method ID to invoke.
        /// </summary>
        [Id(4)]
        public int MethodId { get; set; }

        /// <summary>
        /// Serialized arguments.
        /// </summary>
        [Id(5)]
        public byte[] Arguments { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Stream ID for correlation.
        /// </summary>
        [Id(6)]
        public Guid StreamId { get; set; }
    }

    /// <summary>
    /// Response containing a single item from an IAsyncEnumerable stream.
    /// </summary>
    [GenerateSerializer]
    public class RpcAsyncEnumerableItem : RpcMessage
    {
        /// <summary>
        /// Stream ID for correlation.
        /// </summary>
        [Id(2)]
        public Guid StreamId { get; set; }

        /// <summary>
        /// Sequence number of this item in the stream.
        /// </summary>
        [Id(3)]
        public long SequenceNumber { get; set; }

        /// <summary>
        /// Serialized item data.
        /// </summary>
        [Id(4)]
        public byte[] ItemData { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Whether this is the last item in the stream.
        /// </summary>
        [Id(5)]
        public bool IsComplete { get; set; }

        /// <summary>
        /// Error message if the stream failed.
        /// </summary>
        [Id(6)]
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Request to cancel an IAsyncEnumerable operation.
    /// </summary>
    [GenerateSerializer]
    public class RpcAsyncEnumerableCancel : RpcMessage
    {
        /// <summary>
        /// Stream ID to cancel.
        /// </summary>
        [Id(2)]
        public Guid StreamId { get; set; }
    }
}
