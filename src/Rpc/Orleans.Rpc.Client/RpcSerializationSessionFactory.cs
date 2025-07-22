using Orleans.Serialization;
using Orleans.Serialization.Session;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.TypeSystem;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;

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
        /// Uses hybrid approach: System.Text.Json for simple types to force value semantics,
        /// Orleans binary for complex types.
        /// </summary>
        public byte[] SerializeArgumentsWithIsolatedSession(Serializer serializer, object[] args)
        {
            _logger.LogDebug("[RPC_SESSION_FACTORY] Serializing {Count} arguments with isolated session", args.Length);
            
            // Check if all arguments are simple types that can benefit from JSON serialization
            bool allSimple = args.All(IsSimpleType);
            
            if (allSimple && args.Length > 0)
            {
                _logger.LogDebug("[RPC_SESSION_FACTORY] All {Count} arguments are simple types, using JSON serialization to force value semantics", args.Length);
                
                try
                {
                    var jsonBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(args);
                    _logger.LogDebug("[RPC_SESSION_FACTORY] JSON serialized to {Length} bytes with guaranteed value semantics", jsonBytes.Length);
                    
                    // Wrap with a marker byte to indicate JSON serialization
                    var jsonResult = new byte[jsonBytes.Length + 1];
                    jsonResult[0] = 0xFF; // JSON marker
                    Array.Copy(jsonBytes, 0, jsonResult, 1, jsonBytes.Length);
                    
                    return jsonResult;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[RPC_SESSION_FACTORY] JSON serialization failed, falling back to Orleans binary");
                    // Fall through to Orleans binary serialization
                }
            }
            
            // Use Orleans binary serialization with isolated session
            using var session = CreateClientSession();
            
            var writer = new System.Buffers.ArrayBufferWriter<byte>();
            serializer.Serialize(args, writer, session);
            var result = writer.WrittenMemory.ToArray();
            
            _logger.LogDebug("[RPC_SESSION_FACTORY] Orleans binary serialized to {Length} bytes with isolated session", result.Length);
            
            // Wrap with Orleans binary marker
            var finalResult = new byte[result.Length + 1];
            finalResult[0] = 0x00; // Orleans binary marker
            Array.Copy(result, 0, finalResult, 1, result.Length);
            
            return finalResult;
        }
        
        /// <summary>
        /// Determines if a type can be safely serialized with JSON to avoid Orleans reference issues.
        /// </summary>
        private static bool IsSimpleType(object obj)
        {
            if (obj == null) return true;
            
            var type = obj.GetType();
            
            // Primitive types and common value types
            if (type.IsPrimitive || type.IsEnum) return true;
            
            // Specific types known to work well with JSON
            if (type == typeof(string) || 
                type == typeof(Guid) || 
                type == typeof(DateTime) || 
                type == typeof(DateTimeOffset) || 
                type == typeof(TimeSpan) ||
                type == typeof(decimal)) return true;
            
            return false;
        }

        /// <summary>
        /// Deserializes arguments using an isolated session to handle value-based deserialization.
        /// </summary>
        public T DeserializeWithIsolatedSession<T>(Serializer serializer, ReadOnlyMemory<byte> data)
        {
            using var session = CreateServerSession();
            
            _logger.LogDebug("[RPC_SESSION_FACTORY] Deserializing {Length} bytes with isolated session", data.Length);
            
            var result = serializer.Deserialize<T>(data, session);
            
            _logger.LogDebug("[RPC_SESSION_FACTORY] Deserialized with isolated session");
            
            return result;
        }
    }
}