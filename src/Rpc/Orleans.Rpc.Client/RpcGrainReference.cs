using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.CodeGeneration;
using Orleans.GrainReferences;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Serialization.Invocation;
using Orleans.Serialization.Session;
using Orleans.Serialization.Buffers;

namespace Granville.Rpc
{
    /// <summary>
    /// RPC-specific grain reference that sends requests over RPC transport.
    /// </summary>
    internal class RpcGrainReference : GrainReference, ISpanFormattable
    {
        private readonly ILogger<RpcGrainReference> _logger;
        private readonly OutsideRpcClient _rpcClient;
        private readonly Serializer _serializer;
        private readonly RpcAsyncEnumerableManager _asyncEnumerableManager;

        /// <summary>
        /// Optional zone ID for zone-aware routing.
        /// </summary>
        public int? ZoneId { get; set; }

        public RpcGrainReference(
            GrainReferenceShared shared,
            IdSpan key,
            ILogger<RpcGrainReference> logger,
            OutsideRpcClient rpcClient,
            Serializer serializer,
            RpcAsyncEnumerableManager asyncEnumerableManager)
            : base(shared, key)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _rpcClient = rpcClient ?? throw new ArgumentNullException(nameof(rpcClient));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _asyncEnumerableManager = asyncEnumerableManager ?? throw new ArgumentNullException(nameof(asyncEnumerableManager));
        }

        public async Task<T> InvokeRpcMethodAsync<T>(int methodId, object[] arguments)
        {
            _logger.LogDebug("InvokeRpcMethodAsync called - MethodId: {MethodId}, GrainId: {GrainId}, InterfaceType: {InterfaceType}", 
                methodId, this.GrainId, this.InterfaceType);
            
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

                _logger.LogDebug("Sending RPC request - MessageId: {MessageId}, PayloadSize: {PayloadSize} bytes", 
                    request.MessageId, request.Arguments.Length);
                
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

        /// <summary>
        /// Invokes a method that returns IAsyncEnumerable.
        /// </summary>
        public IAsyncEnumerable<T> InvokeAsyncEnumerableMethodAsync<T>(int methodId, object[] arguments, CancellationToken cancellationToken)
        {
            var streamId = Guid.NewGuid();
            
            // Start the async enumerable operation asynchronously
            _ = Task.Run(async () =>
            {
                try
                {
                    // Serialize arguments
                    var writer = new ArrayBufferWriter<byte>();
                    _serializer.Serialize(arguments, writer);
                    
                    // Create async enumerable request
                    var request = new Protocol.RpcAsyncEnumerableRequest
                    {
                        MessageId = Guid.NewGuid(),
                        GrainId = this.GrainId,
                        InterfaceType = this.InterfaceType,
                        MethodId = methodId,
                        Arguments = writer.WrittenMemory.ToArray(),
                        StreamId = streamId
                    };

                    // Send async enumerable request
                    var response = await _rpcClient.SendRequestAsync(request);
                    
                    if (!response.Success)
                    {
                        _asyncEnumerableManager.CancelStream(streamId);
                        throw new Exception($"Async enumerable request failed: {response.ErrorMessage}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error starting async enumerable method {MethodId} on grain {GrainId}", 
                        methodId, this.GrainId);
                    _asyncEnumerableManager.CancelStream(streamId);
                }
            }, cancellationToken);

            // Return the async enumerable that will receive items
            return _asyncEnumerableManager.CreateStream<T>(streamId, cancellationToken);
        }


        string IFormattable.ToString(string format, IFormatProvider formatProvider) => ToString();

        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider provider)
            => destination.TryWrite($"RpcGrainReference:{GrainId}", out charsWritten);
    }
}