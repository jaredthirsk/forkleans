using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Forkleans.CodeGeneration;
using Forkleans.Configuration;
using Forkleans.Rpc.Transport;
using Forkleans.Runtime;
using Forkleans.Runtime.Messaging;
using Forkleans.Serialization.Invocation;

namespace Forkleans.Rpc
{
    /// <summary>
    /// Represents a datagram-based RPC connection.
    /// Unlike Orleans' Connection class which reads from a PipeReader,
    /// this handles discrete messages received from the transport.
    /// </summary>
    internal sealed class RpcConnection : IDisposable
    {
        private readonly ILogger<RpcConnection> _logger;
        private readonly RpcCatalog _catalog;
        private readonly MessageFactory _messageFactory;
        private readonly MessagingOptions _messagingOptions;
        private readonly IPEndPoint _remoteEndPoint;
        private readonly string _connectionId;
        private readonly IRpcTransport _transport;
        private readonly CancellationTokenSource _cancellationTokenSource;
        
        private int _disposed;

        public RpcConnection(
            string connectionId,
            IPEndPoint remoteEndPoint,
            IRpcTransport transport,
            RpcCatalog catalog,
            MessageFactory messageFactory,
            MessagingOptions messagingOptions,
            ILogger<RpcConnection> logger)
        {
            _connectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
            _remoteEndPoint = remoteEndPoint ?? throw new ArgumentNullException(nameof(remoteEndPoint));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _messageFactory = messageFactory ?? throw new ArgumentNullException(nameof(messageFactory));
            _messagingOptions = messagingOptions ?? throw new ArgumentNullException(nameof(messagingOptions));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cancellationTokenSource = new CancellationTokenSource();
        }

        /// <summary>
        /// Gets the connection ID.
        /// </summary>
        public string ConnectionId => _connectionId;

        /// <summary>
        /// Gets the remote endpoint.
        /// </summary>
        public IPEndPoint RemoteEndPoint => _remoteEndPoint;

        /// <summary>
        /// Processes an incoming RPC request message.
        /// </summary>
        public async Task ProcessRequestAsync(Protocol.RpcRequest request)
        {
            if (_disposed != 0)
            {
                _logger.LogWarning("Ignoring request on disposed connection {ConnectionId}", _connectionId);
                return;
            }

            try
            {
                _logger.LogDebug("Processing request {MessageId} for grain {GrainId} method {MethodId}",
                    request.MessageId, request.GrainId, request.MethodId);

                // For now, we'll dispatch the message directly and handle the response differently
                // TODO: Implement proper request/response correlation
                
                // Create Orleans message from RPC request
                var message = CreateOrleansMessage(request);
                
                // Dispatch to grain
                await _catalog.DispatchMessage(message);
                
                // For now, send an empty success response
                // TODO: Properly capture and serialize the grain method response
                var response = new Protocol.RpcResponse
                {
                    RequestId = request.MessageId,
                    Success = true,
                    Payload = Array.Empty<byte>()
                };
                
                await SendResponseAsync(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing request {MessageId}", request.MessageId);
                
                // Send error response
                var errorResponse = new Protocol.RpcResponse
                {
                    RequestId = request.MessageId,
                    Success = false,
                    ErrorMessage = ex.Message
                };
                
                await SendResponseAsync(errorResponse);
            }
        }

        /// <summary>
        /// Sends a response message to the remote endpoint.
        /// </summary>
        public async Task SendResponseAsync(Protocol.RpcResponse response)
        {
            try
            {
                var messageSerializer = _catalog.ServiceProvider.GetRequiredService<Protocol.RpcMessageSerializer>();
                var responseData = messageSerializer.SerializeMessage(response);
                await _transport.SendAsync(_remoteEndPoint, responseData, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send response {MessageId}", response.MessageId);
            }
        }

        /// <summary>
        /// Creates an Orleans Message from an RPC request.
        /// </summary>
        private Message CreateOrleansMessage(Protocol.RpcRequest request)
        {
            // Create a MethodRequest as the body object
            var methodRequest = new MethodRequest
            {
                InterfaceType = request.InterfaceType,
                MethodId = request.MethodId,
                Arguments = DeserializeArguments(request.Arguments)
            };

            // Create the message
            var message = _messageFactory.CreateMessage(
                methodRequest,
                InvokeMethodOptions.None);

            // Set message properties
            message.Id = CorrelationId.GetNext();
            message.TargetGrain = request.GrainId;
            message.InterfaceType = request.InterfaceType;
            message.IsReadOnly = false;
            message.IsUnordered = false;
            
            // TODO: Set up proper response handling
            // For now, we'll handle responses separately

            return message;
        }

        private object[] DeserializeArguments(byte[] serializedArguments)
        {
            if (serializedArguments == null || serializedArguments.Length == 0)
            {
                return Array.Empty<object>();
            }

            // TODO: Implement proper deserialization
            // For now, assume the arguments are already deserialized or empty
            return Array.Empty<object>();
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _logger.LogDebug("RPC connection {ConnectionId} disposed", _connectionId);
            }
        }
    }

}