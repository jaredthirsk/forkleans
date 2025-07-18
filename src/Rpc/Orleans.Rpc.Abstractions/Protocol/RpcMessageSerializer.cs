using System;
using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Orleans.Serialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Session;

namespace Granville.Rpc.Protocol
{
    /// <summary>
    /// Handles serialization and deserialization of RPC messages.
    /// </summary>
    public class RpcMessageSerializer
    {
        private readonly Serializer _serializer;
        private readonly ILogger<RpcMessageSerializer> _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        public RpcMessageSerializer(Serializer serializer, ILogger<RpcMessageSerializer> logger)
        {
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Configure JSON serialization options (kept for backward compatibility)
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Converters =
                {
                    new JsonStringEnumConverter()
                }
            };
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
                
                // Use Orleans binary serialization
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
                
                // Use Orleans binary deserialization
                return messageType switch
                {
                    1 => _serializer.Deserialize<RpcRequest>(messageData),
                    2 => _serializer.Deserialize<RpcResponse>(messageData),
                    3 => _serializer.Deserialize<RpcHeartbeat>(messageData),
                    4 => _serializer.Deserialize<RpcHandshake>(messageData),
                    5 => _serializer.Deserialize<RpcHandshakeAck>(messageData),
                    6 => _serializer.Deserialize<RpcAsyncEnumerableRequest>(messageData),
                    7 => _serializer.Deserialize<RpcAsyncEnumerableItem>(messageData),
                    8 => _serializer.Deserialize<RpcAsyncEnumerableCancel>(messageData),
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
                RpcHandshakeAck => 5,
                RpcAsyncEnumerableRequest => 6,
                RpcAsyncEnumerableItem => 7,
                RpcAsyncEnumerableCancel => 8,
                _ => throw new NotSupportedException($"Unknown message type: {message.GetType()}")
            };
        }
    }
}
