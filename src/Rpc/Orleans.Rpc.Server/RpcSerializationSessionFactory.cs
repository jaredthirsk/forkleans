using Orleans.Serialization;
using Orleans.Serialization.Session;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.TypeSystem;
using Microsoft.Extensions.Logging;
using System;

namespace Granville.Rpc
{
    /// <summary>
    /// Factory for creating isolated serialization sessions optimized for RPC operations.
    /// These sessions prioritize value-based serialization over reference-based serialization
    /// to ensure compatibility across independent Orleans runtimes.
    /// </summary>
    public class RpcSerializationSessionFactory
    {
        private readonly TypeCodec _typeCodec;
        private readonly WellKnownTypeCollection _wellKnownTypes;
        private readonly CodecProvider _codecProvider;
        private readonly ILogger<RpcSerializationSessionFactory> _logger;

        public RpcSerializationSessionFactory(
            TypeCodec typeCodec,
            WellKnownTypeCollection wellKnownTypes,
            CodecProvider codecProvider,
            ILogger<RpcSerializationSessionFactory> logger)
        {
            _typeCodec = typeCodec;
            _wellKnownTypes = wellKnownTypes;
            _codecProvider = codecProvider;
            _logger = logger;
        }

        /// <summary>
        /// Creates a fresh serialization session for RPC client operations.
        /// This session is optimized for value-based serialization and should be
        /// disposed after a single RPC serialization operation.
        /// </summary>
        public SerializerSession CreateClientSession()
        {
            _logger.LogDebug("[RPC_SESSION_FACTORY] Creating isolated client session for value-based serialization");
            
            // Create a fresh session with no pre-existing references
            var session = new SerializerSession(_typeCodec, _wellKnownTypes, _codecProvider);
            
            // Note: SerializerSession doesn't expose configuration options to disable
            // reference tracking, but since we create fresh sessions for each operation,
            // we minimize reference accumulation and cross-session contamination
            
            return session;
        }

        /// <summary>
        /// Creates a fresh serialization session for RPC server operations.
        /// This session is optimized for value-based deserialization and should be
        /// disposed after a single RPC deserialization operation.
        /// </summary>
        public SerializerSession CreateServerSession()
        {
            _logger.LogDebug("[RPC_SESSION_FACTORY] Creating isolated server session for value-based deserialization");
            
            // Create a fresh session with no pre-existing references
            var session = new SerializerSession(_typeCodec, _wellKnownTypes, _codecProvider);
            
            // Fresh sessions ensure that any references in the serialized data
            // will fail to resolve, forcing Orleans to fall back to value-based
            // deserialization for cross-runtime scenarios
            
            return session;
        }

        /// <summary>
        /// Serializes arguments using an isolated session to ensure value-based serialization.
        /// </summary>
        public byte[] SerializeArgumentsWithIsolatedSession(Serializer serializer, object[] args)
        {
            using var session = CreateClientSession();
            
            _logger.LogDebug("[RPC_SESSION_FACTORY] Serializing {Count} arguments with isolated session", args.Length);
            
            var writer = new System.Buffers.ArrayBufferWriter<byte>();
            serializer.Serialize(args, writer, session);
            var result = writer.WrittenMemory.ToArray();
            
            _logger.LogDebug("[RPC_SESSION_FACTORY] Serialized to {Length} bytes with isolated session", result.Length);
            
            return result;
        }

        /// <summary>
        /// Deserializes arguments using an isolated session to handle value-based deserialization.
        /// Supports both JSON and Orleans binary formats based on marker byte.
        /// </summary>
        public T DeserializeWithIsolatedSession<T>(Serializer serializer, ReadOnlyMemory<byte> data)
        {
            if (data.Length == 0)
            {
                _logger.LogDebug("[RPC_SESSION_FACTORY] Empty data, returning default");
                return default(T);
            }
            
            _logger.LogDebug("[RPC_SESSION_FACTORY] Deserializing {Length} bytes with isolated session", data.Length);
            
            var dataSpan = data.Span;
            var marker = dataSpan[0];
            var actualData = data.Slice(1);
            
            if (marker == 0xFF) // JSON marker
            {
                _logger.LogDebug("[RPC_SESSION_FACTORY] Detected JSON serialization, using System.Text.Json deserializer");
                
                try
                {
                    var result = System.Text.Json.JsonSerializer.Deserialize<T>(actualData);
                    _logger.LogDebug("[RPC_SESSION_FACTORY] JSON deserialized successfully");
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[RPC_SESSION_FACTORY] JSON deserialization failed");
                    throw;
                }
            }
            else if (marker == 0x00) // Orleans binary marker
            {
                _logger.LogDebug("[RPC_SESSION_FACTORY] Detected Orleans binary serialization, using isolated session");
                
                using var session = CreateServerSession();
                var result = serializer.Deserialize<T>(actualData, session);
                
                _logger.LogDebug("[RPC_SESSION_FACTORY] Orleans binary deserialized with isolated session");
                return result;
            }
            else
            {
                // Backward compatibility: no marker byte, assume Orleans binary
                _logger.LogDebug("[RPC_SESSION_FACTORY] No marker detected, assuming Orleans binary format for backward compatibility");
                
                using var session = CreateServerSession();
                var result = serializer.Deserialize<T>(data, session);
                
                _logger.LogDebug("[RPC_SESSION_FACTORY] Legacy Orleans binary deserialized with isolated session");
                return result;
            }
        }
    }
}