using System;
using System.Buffers;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Forkleans;
using Forkleans.CodeGeneration;
using Forkleans.GrainReferences;
using Forkleans.Runtime;
using Forkleans.Serialization;
using Forkleans.Serialization.Invocation;
using Forkleans.Serialization.Session;
using Forkleans.Serialization.Buffers;

namespace Forkleans.Rpc
{
    /// <summary>
    /// RPC-specific grain reference that sends requests over RPC transport.
    /// </summary>
    internal class RpcGrainReference : GrainReference, ISpanFormattable
    {
        private readonly ILogger<RpcGrainReference> _logger;
        private readonly RpcClient _rpcClient;
        private readonly Serializer _serializer;

        /// <summary>
        /// Optional zone ID for zone-aware routing.
        /// </summary>
        public int? ZoneId { get; set; }

        public RpcGrainReference(
            GrainReferenceShared shared,
            IdSpan key,
            ILogger<RpcGrainReference> logger,
            RpcClient rpcClient,
            Serializer serializer)
            : base(shared, key)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _rpcClient = rpcClient ?? throw new ArgumentNullException(nameof(rpcClient));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        public async Task<T> InvokeRpcMethodAsync<T>(int methodId, object[] arguments)
        {
            try
            {
                // Serialize arguments
                var writer = new ArrayBufferWriter<byte>();
                _serializer.Serialize(arguments, writer);
                
                // Create RPC request
                var request = new Protocol.RpcRequest
                {
                    MessageId = Guid.NewGuid(),
                    GrainId = this.GrainId,
                    InterfaceType = this.InterfaceType,
                    MethodId = methodId,
                    Arguments = writer.WrittenMemory.ToArray(),
                    TimeoutMs = 30000, // Default 30 seconds
                    TargetZoneId = ZoneId // Include zone ID if set
                };

                // Send request and wait for response
                var response = await _rpcClient.SendRequestAsync(request);
                
                if (!response.Success)
                {
                    throw new Exception($"RPC call failed: {response.ErrorMessage}");
                }

                // Deserialize response
                if (response.Payload != null && response.Payload.Length > 0)
                {
                    return _serializer.Deserialize<T>(response.Payload);
                }
                
                return default(T);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error invoking RPC method {MethodId} on grain {GrainId}", 
                    methodId, this.GrainId);
                throw;
            }
        }

        public async Task InvokeRpcMethodAsync(int methodId, object[] arguments)
        {
            await InvokeRpcMethodAsync<object>(methodId, arguments);
        }

        // TODO: Implement proper invocation once we have RpcConnection
        // For now, just return a simple response
        public ValueTask<Response> InvokeRpcAsync(object request)
        {
            return new ValueTask<Response>(Response.FromResult("RPC invocation not yet implemented"));
        }

        string IFormattable.ToString(string format, IFormatProvider formatProvider) => ToString();

        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider provider)
            => destination.TryWrite($"RpcGrainReference:{GrainId}", out charsWritten);
    }
}