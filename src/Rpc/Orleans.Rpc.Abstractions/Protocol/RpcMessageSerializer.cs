using System;
using System.Buffers;
using Microsoft.Extensions.Logging;
using Forkleans.Serialization;

namespace Forkleans.Rpc.Protocol
{
    /// <summary>
    /// Handles serialization and deserialization of RPC messages.
    /// </summary>
    public class RpcMessageSerializer
    {
        private readonly Serializer _serializer;
        private readonly ILogger<RpcMessageSerializer> _logger;

        public RpcMessageSerializer(Serializer serializer, ILogger<RpcMessageSerializer> logger)
        {
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Serializes an RPC message to bytes.
        /// </summary>
        public byte[] SerializeMessage(RpcMessage message)
        {
            try
            {
                var writer = new ArrayBufferWriter<byte>();
                
                // Write message type byte
                writer.Write(new[] { GetMessageTypeByte(message) });
                
                // Serialize the message
                _serializer.Serialize(message, writer);
                
                return writer.WrittenMemory.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to serialize RPC message of type {Type}", message.GetType().Name);
                throw;
            }
        }

        /// <summary>
        /// Deserializes an RPC message from bytes.
        /// </summary>
        public RpcMessage DeserializeMessage(ReadOnlyMemory<byte> data)
        {
            try
            {
                if (data.Length < 1)
                {
                    throw new ArgumentException("Message data is too short");
                }

                var messageType = data.Span[0];
                var messageData = data.Slice(1);

                return messageType switch
                {
                    1 => _serializer.Deserialize<RpcRequest>(messageData),
                    2 => _serializer.Deserialize<RpcResponse>(messageData),
                    3 => _serializer.Deserialize<RpcHeartbeat>(messageData),
                    4 => _serializer.Deserialize<RpcHandshake>(messageData),
                    _ => throw new NotSupportedException($"Unknown message type: {messageType}")
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize RPC message");
                throw;
            }
        }

        private byte GetMessageTypeByte(RpcMessage message)
        {
            return message switch
            {
                RpcRequest => 1,
                RpcResponse => 2,
                RpcHeartbeat => 3,
                RpcHandshake => 4,
                _ => throw new NotSupportedException($"Unknown message type: {message.GetType()}")
            };
        }
    }
}