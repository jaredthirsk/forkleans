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
            _logger.LogTrace("[RPC_SESSION_FACTORY] Creating isolated client session for value-based serialization");
            
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
            _logger.LogTrace("[RPC_SESSION_FACTORY] Creating isolated server session for value-based deserialization");
            
            // Create a fresh session with no pre-existing references
            var session = new SerializerSession(_typeCodec, _wellKnownTypes, _codecProvider);
            
            // Fresh sessions ensure that any references in the serialized data
            // will fail to resolve, forcing Orleans to fall back to value-based
            // deserialization for cross-runtime scenarios
            
            return session;
        }

        /// <summary>
        /// Serializes arguments using an isolated session to ensure value-based serialization.
        /// Always uses Orleans binary serialization to support all types with generated serializers.
        /// </summary>
        public byte[] SerializeArgumentsWithIsolatedSession(Serializer serializer, object[] args)
        {
            _logger.LogTrace("[RPC_SESSION_FACTORY] Serializing {Count} arguments with isolated session", args.Length);
            
            // Log the actual argument values
            for (int i = 0; i < args.Length; i++)
            {
                _logger.LogTrace("[RPC_SESSION_FACTORY] Argument[{Index}]: Type={Type}, Value={Value}", 
                    i, args[i]?.GetType()?.FullName ?? "null", args[i]?.ToString() ?? "null");
            }
            
            // IMPORTANT: Serialize each argument with its own fresh session to avoid reference tracking
            // Orleans StringCodec always tries to use references if the string was seen before in the session.
            // By using a fresh session for each argument, we ensure value-based serialization.
            var segments = new System.Collections.Generic.List<byte[]>();
            var totalLength = 0;
            
            for (int i = 0; i < args.Length; i++)
            {
                using var session = CreateClientSession();
                var writer = new System.Buffers.ArrayBufferWriter<byte>();
                
                // Serialize individual argument to force value serialization
                serializer.Serialize(args[i], writer, session);
                var segment = writer.WrittenMemory.ToArray();
                
                _logger.LogTrace("[RPC_SESSION_FACTORY] Argument[{Index}] serialized to {Length} bytes", i, segment.Length);
                
                segments.Add(segment);
                totalLength += segment.Length + 4; // 4 bytes for length prefix
            }
            
            // Combine segments with length prefixes into a custom format
            // Format: [marker][count][length1][data1][length2][data2]...
            var result = new byte[totalLength + 5]; // +5 for marker and array count
            result[0] = 0xFF; // Custom RPC arguments marker (not 0x00 which is Orleans binary)
            
            // Write argument count (4 bytes, big-endian)
            result[1] = (byte)(args.Length >> 24);
            result[2] = (byte)(args.Length >> 16);
            result[3] = (byte)(args.Length >> 8);
            result[4] = (byte)args.Length;
            
            var offset = 5;
            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                // Write segment length (4 bytes, big-endian)
                result[offset++] = (byte)(segment.Length >> 24);
                result[offset++] = (byte)(segment.Length >> 16);
                result[offset++] = (byte)(segment.Length >> 8);
                result[offset++] = (byte)segment.Length;
                
                // Write segment data
                Array.Copy(segment, 0, result, offset, segment.Length);
                offset += segment.Length;
            }
            
            _logger.LogTrace("[RPC_SESSION_FACTORY] Total serialized to {Length} bytes with individual sessions per argument", result.Length);
            
            return result;
        }

        /// <summary>
        /// Deserializes arguments using an isolated session to handle value-based deserialization.
        /// Supports both Orleans binary format and custom RPC format with individual argument sessions.
        /// </summary>
        public T DeserializeWithIsolatedSession<T>(Serializer serializer, ReadOnlyMemory<byte> data)
        {
            if (data.Length == 0)
            {
                _logger.LogTrace("[RPC_SESSION_FACTORY] Empty data, returning default");
                return default(T)!;
            }
            
            _logger.LogTrace("[RPC_SESSION_FACTORY] Deserializing {Length} bytes with isolated session", data.Length);
            
            var dataSpan = data.Span;
            var marker = dataSpan[0];
            
            if (marker == 0xFF && typeof(T) == typeof(object[])) // Custom RPC arguments format
            {
                _logger.LogTrace("[RPC_SESSION_FACTORY] Detected custom RPC arguments format");
                
                if (data.Length < 5)
                {
                    throw new InvalidOperationException("Invalid RPC arguments format: insufficient data");
                }
                
                // Read argument count (4 bytes, big-endian)
                var argCount = (dataSpan[1] << 24) | (dataSpan[2] << 16) | (dataSpan[3] << 8) | dataSpan[4];
                _logger.LogTrace("[RPC_SESSION_FACTORY] Deserializing {Count} arguments", argCount);
                
                var args = new object[argCount];
                var offset = 5;
                
                for (int i = 0; i < argCount; i++)
                {
                    if (offset + 4 > data.Length)
                    {
                        throw new InvalidOperationException($"Invalid RPC arguments format: insufficient data for argument {i} length");
                    }
                    
                    // Read segment length (4 bytes, big-endian)
                    var segmentLength = (dataSpan[offset] << 24) | (dataSpan[offset + 1] << 16) | 
                                      (dataSpan[offset + 2] << 8) | dataSpan[offset + 3];
                    offset += 4;
                    
                    if (offset + segmentLength > data.Length)
                    {
                        throw new InvalidOperationException($"Invalid RPC arguments format: insufficient data for argument {i} content");
                    }
                    
                    // Deserialize individual argument with fresh session
                    var segmentData = data.Slice(offset, segmentLength);
                    using var session = CreateServerSession();
                    args[i] = serializer.Deserialize<object>(segmentData, session);
                    
                    _logger.LogTrace("[RPC_SESSION_FACTORY] Deserialized argument[{Index}]: Type={Type}, Value={Value}",
                        i, args[i]?.GetType()?.Name ?? "null", args[i]?.ToString() ?? "null");
                    
                    offset += segmentLength;
                }
                
                return (T)(object)args;
            }
            else if (marker == 0x00) // Orleans binary marker
            {
                _logger.LogTrace("[RPC_SESSION_FACTORY] Detected Orleans binary serialization, using isolated session for type {Type}", typeof(T).Name);
                
                // Skip the marker byte - Orleans deserializer expects the raw serialized data without the marker
                var orleansData = data.Slice(1);
                
                try
                {
                    using var session = CreateServerSession();
                    var result = serializer.Deserialize<T>(orleansData, session);
                    
                    _logger.LogTrace("[RPC_SESSION_FACTORY] Orleans binary deserialized with isolated session");
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[RPC_SESSION_FACTORY] Orleans deserialization failed for type {Type}. Data length: {Length}, First 20 bytes: {Bytes}", 
                        typeof(T).Name, orleansData.Length, Convert.ToHexString(orleansData.Slice(0, Math.Min(20, orleansData.Length)).ToArray()));
                    throw;
                }
            }
            else
            {
                // Backward compatibility: no marker byte, assume Orleans binary
                _logger.LogTrace("[RPC_SESSION_FACTORY] No marker detected (marker: 0x{Marker:X2}), assuming Orleans binary format for backward compatibility", marker);
                
                using var session = CreateServerSession();
                var result = serializer.Deserialize<T>(data, session);
                
                _logger.LogTrace("[RPC_SESSION_FACTORY] Legacy Orleans binary deserialized with isolated session");
                return result;
            }
        }
    }
}