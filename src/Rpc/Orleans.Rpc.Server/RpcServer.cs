using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Forkleans.Rpc.Configuration;
using Forkleans.Rpc.Transport;
using Forkleans.Runtime;
using Forkleans.Serialization;
using Forkleans.Serialization.Session;
using Forkleans.Serialization.Buffers;

namespace Forkleans.Rpc
{
    /// <summary>
    /// Main RPC server implementation.
    /// </summary>
    internal sealed class RpcServer : ILifecycleParticipant<IRpcServerLifecycle>
    {
        private readonly ILogger<RpcServer> _logger;
        private readonly ILocalRpcServerDetails _serverDetails;
        private readonly RpcServerOptions _options;
        private readonly IRpcTransportFactory _transportFactory;
        private readonly IRpcServerLifecycle _lifecycle;
        private readonly RpcCatalog _catalog;
        private readonly MessageFactory _messageFactory;
        private readonly Forkleans.Serialization.Serializer _serializer;
        
        private IRpcTransport _transport;

        public RpcServer(
            ILogger<RpcServer> logger,
            ILocalRpcServerDetails serverDetails,
            IOptions<RpcServerOptions> options,
            IRpcTransportFactory transportFactory,
            IRpcServerLifecycle lifecycle,
            RpcCatalog catalog,
            MessageFactory messageFactory,
            Forkleans.Serialization.Serializer serializer)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serverDetails = serverDetails ?? throw new ArgumentNullException(nameof(serverDetails));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
            _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _messageFactory = messageFactory ?? throw new ArgumentNullException(nameof(messageFactory));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            
            _logger.LogInformation("RpcServer created, registering with lifecycle");
        }

        public void Participate(IRpcServerLifecycle lifecycle)
        {
            lifecycle.Subscribe<RpcServer>(
                RpcServerLifecycleStage.TransportInit,
                OnTransportInit,
                OnTransportStop);

            lifecycle.Subscribe<RpcServer>(
                RpcServerLifecycleStage.TransportStart,
                OnTransportStart,
                _ => Task.CompletedTask);
        }

        private Task OnTransportInit(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Initializing RPC transport for server {ServerName}", _serverDetails.ServerName);
            
            _transport = _transportFactory.CreateTransport(_catalog.ServiceProvider);
            _transport.DataReceived += OnDataReceived;
            _transport.ConnectionEstablished += OnConnectionEstablished;
            _transport.ConnectionClosed += OnConnectionClosed;

            return Task.CompletedTask;
        }

        private async Task OnTransportStart(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting RPC transport on {Endpoint}", _serverDetails.ServerEndpoint);
            
            await _transport.StartAsync(_serverDetails.ServerEndpoint, cancellationToken);
            
            _logger.LogInformation("RPC server {ServerName} is listening on {Endpoint}", 
                _serverDetails.ServerName, _serverDetails.ServerEndpoint);
        }

        private async Task OnTransportStop(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping RPC transport");
            
            if (_transport != null)
            {
                _transport.DataReceived -= OnDataReceived;
                _transport.ConnectionEstablished -= OnConnectionEstablished;
                _transport.ConnectionClosed -= OnConnectionClosed;
                
                await _transport.StopAsync(cancellationToken);
                _transport.Dispose();
                _transport = null;
            }
        }

        private async void OnDataReceived(object sender, RpcDataReceivedEventArgs e)
        {
            try
            {
                _logger.LogDebug("Received {ByteCount} bytes from {Endpoint}", 
                    e.Data.Length, e.RemoteEndPoint);
                
                // Deserialize the message
                var messageSerializer = _catalog.ServiceProvider.GetRequiredService<Protocol.RpcMessageSerializer>();
                var message = messageSerializer.DeserializeMessage(e.Data);

                // Handle different message types
                switch (message)
                {
                    case Protocol.RpcHandshake handshake:
                        await HandleHandshake(handshake, e.RemoteEndPoint);
                        break;
                        
                    case Protocol.RpcRequest request:
                        await HandleRequest(request, e.RemoteEndPoint);
                        break;
                        
                    case Protocol.RpcHeartbeat heartbeat:
                        await HandleHeartbeat(heartbeat, e.RemoteEndPoint);
                        break;
                        
                    default:
                        _logger.LogWarning("Received unexpected message type: {Type}", message.GetType().Name);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing received data from {Endpoint}", e.RemoteEndPoint);
            }
        }

        private async Task HandleHandshake(Protocol.RpcHandshake handshake, IPEndPoint remoteEndpoint)
        {
            _logger.LogInformation("Received handshake from client {ClientId}, protocol version {Version}", 
                handshake.ClientId, handshake.ProtocolVersion);

            // Send handshake response
            var response = new Protocol.RpcHandshake
            {
                ClientId = _serverDetails.ServerId,
                ProtocolVersion = 1,
                Features = new[] { "basic-rpc" }
            };

            var messageSerializer = _catalog.ServiceProvider.GetRequiredService<Protocol.RpcMessageSerializer>();
            var responseData = messageSerializer.SerializeMessage(response);
            await _transport.SendAsync(remoteEndpoint, responseData, CancellationToken.None);
        }

        private async Task HandleRequest(Protocol.RpcRequest request, IPEndPoint remoteEndpoint)
        {
            _logger.LogDebug("Handling RPC request {MessageId} for grain {GrainId}", 
                request.MessageId, request.GrainId);

            var response = new Protocol.RpcResponse
            {
                RequestId = request.MessageId
            };

            try
            {
                // Get or create the grain activation
                var grainContext = await _catalog.GetOrCreateActivationAsync(request.GrainId);
                
                // Get the grain reference
                var grainReference = grainContext.GrainReference;
                
                // For now, just return a dummy response until we implement proper invocation
                // TODO: Implement proper grain method invocation using RpcConnection
                response.Success = true;
                response.Payload = System.Text.Encoding.UTF8.GetBytes($"Hello from grain {request.GrainId}! Method {request.MethodId} called.");
                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling request for grain {GrainId}", request.GrainId);
                response.Success = false;
                response.ErrorMessage = ex.Message;
            }

            // Send response
            var messageSerializer = _catalog.ServiceProvider.GetRequiredService<Protocol.RpcMessageSerializer>();
            var responseData = messageSerializer.SerializeMessage(response);
            await _transport.SendAsync(remoteEndpoint, responseData, CancellationToken.None);
        }

        private async Task HandleHeartbeat(Protocol.RpcHeartbeat heartbeat, IPEndPoint remoteEndpoint)
        {
            _logger.LogDebug("Received heartbeat from {SourceId}", heartbeat.SourceId);
            
            // Echo heartbeat back
            var response = new Protocol.RpcHeartbeat
            {
                SourceId = _serverDetails.ServerId
            };

            var messageSerializer = _catalog.ServiceProvider.GetRequiredService<Protocol.RpcMessageSerializer>();
            var responseData = messageSerializer.SerializeMessage(response);
            await _transport.SendAsync(remoteEndpoint, responseData, CancellationToken.None);
        }

        private void OnConnectionEstablished(object sender, RpcConnectionEventArgs e)
        {
            _logger.LogInformation("Connection established with {Endpoint} (ID: {ConnectionId})", 
                e.RemoteEndPoint, e.ConnectionId);
        }

        private void OnConnectionClosed(object sender, RpcConnectionEventArgs e)
        {
            _logger.LogInformation("Connection closed with {Endpoint} (ID: {ConnectionId})", 
                e.RemoteEndPoint, e.ConnectionId);
        }
    }
}