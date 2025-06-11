using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Forkleans.Configuration;
using Forkleans.Rpc.Configuration;
using Forkleans.Rpc.Transport;
using Forkleans.Runtime;
using Forkleans.Serialization;
using Forkleans.Serialization.Session;
using Forkleans.Serialization.Buffers;
using Forkleans.Serialization.Invocation;

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
        private readonly IOptions<MessagingOptions> _messagingOptions;
        private readonly ILoggerFactory _loggerFactory;
        
        private IRpcTransport _transport;
        private readonly ConcurrentDictionary<string, RpcConnection> _connections = new();

        public RpcServer(
            ILogger<RpcServer> logger,
            ILocalRpcServerDetails serverDetails,
            IOptions<RpcServerOptions> options,
            IRpcTransportFactory transportFactory,
            IRpcServerLifecycle lifecycle,
            RpcCatalog catalog,
            MessageFactory messageFactory,
            Forkleans.Serialization.Serializer serializer,
            IOptions<MessagingOptions> messagingOptions,
            ILoggerFactory loggerFactory)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serverDetails = serverDetails ?? throw new ArgumentNullException(nameof(serverDetails));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
            _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _messageFactory = messageFactory ?? throw new ArgumentNullException(nameof(messageFactory));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _messagingOptions = messagingOptions ?? throw new ArgumentNullException(nameof(messagingOptions));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            
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
            
            // Clean up all connections
            foreach (var kvp in _connections)
            {
                kvp.Value.Dispose();
            }
            _connections.Clear();
            _logger.LogDebug("Cleaned up all RPC connections");
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
            _logger.LogInformation("Handling RPC request {MessageId} for grain {GrainId} method {MethodId} from {Endpoint}", 
                request.MessageId, request.GrainId, request.MethodId, remoteEndpoint);

            // Get or create connection for this endpoint
            var connectionId = remoteEndpoint.ToString();
            var connection = GetOrCreateConnection(connectionId, remoteEndpoint);
            
            // Process the request through the connection
            await connection.ProcessRequestAsync(request);
        }

        private RpcConnection GetOrCreateConnection(string connectionId, IPEndPoint remoteEndpoint)
        {
            return _connections.GetOrAdd(connectionId, id =>
            {
                _logger.LogDebug("Creating new RPC connection for {ConnectionId}", id);
                
                var connectionLogger = _loggerFactory.CreateLogger<RpcConnection>();
                var interfaceToImplementationMapping = _catalog.ServiceProvider.GetRequiredService<InterfaceToImplementationMappingCache>();
                
                return new RpcConnection(
                    id,
                    remoteEndpoint,
                    _transport,
                    _catalog,
                    _messageFactory,
                    _messagingOptions.Value,
                    interfaceToImplementationMapping,
                    connectionLogger);
            });
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
                
            // Remove connection from dictionary
            var connectionId = e.RemoteEndPoint.ToString();
            if (_connections.TryRemove(connectionId, out var connection))
            {
                connection.Dispose();
                _logger.LogDebug("Removed and disposed connection {ConnectionId}", connectionId);
            }
        }
    }
}