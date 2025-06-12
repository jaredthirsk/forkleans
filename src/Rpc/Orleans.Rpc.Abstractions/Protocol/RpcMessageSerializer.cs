using System;
using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        private readonly JsonSerializerOptions _jsonOptions;

        public RpcMessageSerializer(Serializer serializer, ILogger<RpcMessageSerializer> logger)
        {
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Configure JSON serialization options
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
                
                // Serialize to JSON
                var json = JsonSerializer.Serialize(message, message.GetType(), _jsonOptions);
                var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
                writer.Write(jsonBytes);
                
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
                
                // Deserialize from JSON
                var json = System.Text.Encoding.UTF8.GetString(messageData.Span);

                return messageType switch
                {
                    1 => JsonSerializer.Deserialize<RpcRequest>(json, _jsonOptions),
                    2 => JsonSerializer.Deserialize<RpcResponse>(json, _jsonOptions),
                    3 => JsonSerializer.Deserialize<RpcHeartbeat>(json, _jsonOptions),
                    4 => JsonSerializer.Deserialize<RpcHandshake>(json, _jsonOptions),
                    5 => JsonSerializer.Deserialize<RpcHandshakeAck>(json, _jsonOptions),
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
                _ => throw new NotSupportedException($"Unknown message type: {message.GetType()}")
            };
        }
    }
}