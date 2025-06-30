using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Configuration;
using Granville.Rpc.Configuration;
using Granville.Rpc.Telemetry;
using Granville.Rpc.Transport;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Serialization.Session;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Invocation;
using Orleans.Metadata;

namespace Granville.Rpc
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
        private readonly Orleans.Serialization.Serializer _serializer;
        private readonly MessagingOptions _messagingOptions;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IClusterManifestProvider _manifestProvider;
        private readonly GrainInterfaceTypeToGrainTypeResolver _interfaceToGrainResolver;
        private readonly IServiceProvider _serviceProvider;
        
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
            Orleans.Serialization.Serializer serializer,
            IOptions<ClientMessagingOptions> messagingOptions,
            ILoggerFactory loggerFactory,
            IClusterManifestProvider manifestProvider,
            GrainInterfaceTypeToGrainTypeResolver interfaceToGrainResolver,
            IServiceProvider serviceProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serverDetails = serverDetails ?? throw new ArgumentNullException(nameof(serverDetails));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
            _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _messageFactory = messageFactory ?? throw new ArgumentNullException(nameof(messageFactory));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _messagingOptions = messagingOptions?.Value ?? throw new ArgumentNullException(nameof(messagingOptions));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            _manifestProvider = manifestProvider ?? throw new ArgumentNullException(nameof(manifestProvider));
            _interfaceToGrainResolver = interfaceToGrainResolver ?? throw new ArgumentNullException(nameof(interfaceToGrainResolver));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            
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
                _logger.LogDebug("RPC Server OnDataReceived: Received {ByteCount} bytes from {Endpoint}, ConnectionId: {ConnectionId}", 
                    e.Data.Length, e.RemoteEndPoint, e.ConnectionId);
                
                // Record message size metric
                RpcServerTelemetry.MessageSize.Record(e.Data.Length, new KeyValuePair<string, object?>("direction", "inbound"));
                
                // Deserialize the message
                var messageSerializer = _catalog.ServiceProvider.GetRequiredService<Protocol.RpcMessageSerializer>();
                var message = messageSerializer.DeserializeMessage(e.Data);
                
                _logger.LogDebug("RPC Server OnDataReceived: Deserialized message of type {MessageType} from {Endpoint}", 
                    message?.GetType().Name ?? "null", e.RemoteEndPoint);

                // Handle different message types
                switch (message)
                {
                    case Protocol.RpcHandshake handshake:
                        await HandleHandshake(handshake, e.RemoteEndPoint, e.ConnectionId);
                        break;
                        
                    case Protocol.RpcRequest request:
                        _logger.LogDebug("RPC Server OnDataReceived: Received RPC request {MessageId} for grain {GrainId}, method {MethodId}", 
                            request.MessageId, request.GrainId, request.MethodId);
                        RpcServerTelemetry.RequestsReceived.Add(1, 
                            new KeyValuePair<string, object?>("grain.id", request.GrainId.ToString()),
                            new KeyValuePair<string, object?>("method.id", request.MethodId.ToString()));
                        await HandleRequest(request, e.RemoteEndPoint, e.ConnectionId);
                        break;
                        
                    case Protocol.RpcHeartbeat heartbeat:
                        await HandleHeartbeat(heartbeat, e.RemoteEndPoint, e.ConnectionId);
                        break;
                        
                    case Protocol.RpcAsyncEnumerableRequest asyncEnumRequest:
                        _logger.LogDebug("RPC Server OnDataReceived: Received async enumerable request {StreamId} for grain {GrainId}, method {MethodId}", 
                            asyncEnumRequest.StreamId, asyncEnumRequest.GrainId, asyncEnumRequest.MethodId);
                        await HandleAsyncEnumerableRequest(asyncEnumRequest, e.RemoteEndPoint, e.ConnectionId);
                        break;
                        
                    case Protocol.RpcAsyncEnumerableCancel asyncEnumCancel:
                        _logger.LogDebug("RPC Server OnDataReceived: Received async enumerable cancel for stream {StreamId}", 
                            asyncEnumCancel.StreamId);
                        await HandleAsyncEnumerableCancel(asyncEnumCancel, e.RemoteEndPoint, e.ConnectionId);
                        break;
                        
                    default:
                        _logger.LogWarning("Received unexpected message type: {Type}", message.GetType().Name);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing received data from {Endpoint}", e.RemoteEndPoint);
                RpcServerTelemetry.TransportErrors.Add(1, 
                    new KeyValuePair<string, object?>("error.type", ex.GetType().Name),
                    new KeyValuePair<string, object?>("remote.endpoint", e.RemoteEndPoint.ToString()));
            }
        }

        private async Task HandleHandshake(Protocol.RpcHandshake handshake, IPEndPoint remoteEndpoint, string connectionId)
        {
            using var activity = RpcServerTelemetry.StartActivityIfEnabled(RpcServerTelemetry.HandshakeActivity);
            activity?.SetTag("client.id", handshake.ClientId);
            activity?.SetTag("protocol.version", handshake.ProtocolVersion);
            activity?.SetTag("remote.endpoint", remoteEndpoint.ToString());
            
            var stopwatch = Stopwatch.StartNew();
            
            _logger.LogInformation("Received handshake from client {ClientId}, protocol version {Version}", 
                handshake.ClientId, handshake.ProtocolVersion);

            try
            {

            // Build grain manifest for the client
            var manifest = BuildGrainManifest();

            // Send handshake acknowledgment with manifest
            var response = new Protocol.RpcHandshakeAck
            {
                ServerId = _serverDetails.ServerId,
                ProtocolVersion = 1,
                GrainManifest = manifest
            };

            // Check if this server is zone-aware
            // First, check for the generic IZoneAwareRpcServer interface
            var zoneAwareServer = _serviceProvider.GetService<IZoneAwareRpcServer>();
            if (zoneAwareServer != null)
            {
                var zoneId = zoneAwareServer.GetZoneId();
                if (zoneId.HasValue)
                {
                    response.ZoneId = zoneId.Value;
                    _logger.LogInformation("Server is zone-aware (via IZoneAwareRpcServer), assigned to ZoneId: {ZoneId}", zoneId.Value);
                }
            }
            else
            {
                // Fallback: Check for IWorldSimulation service (e.g., Shooter sample)
                var worldSimulationType = Type.GetType("Shooter.ActionServer.Simulation.IWorldSimulation, Shooter.ActionServer");
                if (worldSimulationType != null)
                {
                    var worldSimulation = _serviceProvider.GetService(worldSimulationType);
                    if (worldSimulation != null)
                    {
                        try
                        {
                            // Use reflection to call GetAssignedSquare
                            var getAssignedSquareMethod = worldSimulationType.GetMethod("GetAssignedSquare");
                            if (getAssignedSquareMethod != null)
                            {
                                var assignedSquare = getAssignedSquareMethod.Invoke(worldSimulation, null);
                                if (assignedSquare != null)
                                {
                                    // Get X and Y properties
                                    var xProp = assignedSquare.GetType().GetProperty("X");
                                    var yProp = assignedSquare.GetType().GetProperty("Y");
                                    if (xProp != null && yProp != null)
                                    {
                                        var x = (int)xProp.GetValue(assignedSquare);
                                        var y = (int)yProp.GetValue(assignedSquare);
                                        
                                        // Convert GridSquare (X,Y) to a single zone ID
                                        // Using a simple formula: zoneId = y * 1000 + x
                                        // This assumes zones are in a reasonable range (e.g., -500 to 500)
                                        var zoneId = y * 1000 + x;
                                        response.ZoneId = zoneId;
                                        
                                        _logger.LogInformation("Server is zone-aware (via IWorldSimulation), assigned to zone ({X},{Y}) -> ZoneId: {ZoneId}", 
                                            x, y, zoneId);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to get zone information from IWorldSimulation");
                        }
                    }
                }
            }

            var messageSerializer = _catalog.ServiceProvider.GetRequiredService<Protocol.RpcMessageSerializer>();
            var responseData = messageSerializer.SerializeMessage(response);
            _logger.LogInformation("Sending handshake acknowledgment to {Endpoint} (ConnectionId: {ConnectionId}) with {GrainCount} grains in manifest, ZoneId: {ZoneId}", 
                remoteEndpoint, connectionId, manifest.GrainProperties.Count, response.ZoneId);
            
            // Use SendToConnectionAsync when we have a connection ID
            if (!string.IsNullOrEmpty(connectionId))
            {
                await _transport.SendToConnectionAsync(connectionId, responseData, CancellationToken.None);
            }
            else
            {
                await _transport.SendAsync(remoteEndpoint, responseData, CancellationToken.None);
            }
            
            stopwatch.Stop();
            RpcServerTelemetry.RecordHandshake(true, stopwatch.Elapsed.TotalMilliseconds, handshake.ClientId);
            
            // Zone ID will be available via the observable gauge when implemented
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error handling handshake from client {ClientId}", handshake.ClientId);
                RpcServerTelemetry.RecordHandshake(false, stopwatch.Elapsed.TotalMilliseconds, handshake.ClientId, ex.GetType().Name);
                throw;
            }
        }

        private Protocol.RpcGrainManifest BuildGrainManifest()
        {
            var manifest = new Protocol.RpcGrainManifest();
            var currentManifest = _manifestProvider.Current;

            // Build interface to grain type mappings
            foreach (var grainManifest in currentManifest.AllGrainManifests)
            {
                foreach (var grain in grainManifest.Grains)
                {
                    // Extract interfaces implemented by this grain
                    var grainProperties = grain.Value.Properties;
                    foreach (var prop in grainProperties)
                    {
                        if (prop.Key.StartsWith("interface.") && prop.Key != "interface.count")
                        {
                            var interfaceType = prop.Value;
                            manifest.InterfaceToGrainMappings[interfaceType] = grain.Key.ToString();
                        }
                    }
                    
                    // Store grain properties
                    manifest.GrainProperties[grain.Key.ToString()] = new Dictionary<string, string>(grain.Value.Properties);
                }

                // Store interface properties
                foreach (var iface in grainManifest.Interfaces)
                {
                    manifest.InterfaceProperties[iface.Key.ToString()] = new Dictionary<string, string>(iface.Value.Properties);
                }
            }

            _logger.LogDebug("Built grain manifest with {GrainCount} grains and {InterfaceCount} interfaces",
                manifest.GrainProperties.Count, manifest.InterfaceProperties.Count);

            return manifest;
        }

        private async Task HandleRequest(Protocol.RpcRequest request, IPEndPoint remoteEndpoint, string actualConnectionId = null)
        {
            using var activity = RpcServerTelemetry.StartActivityIfEnabled(RpcServerTelemetry.RequestActivity);
            activity?.SetTag("request.id", request.MessageId.ToString());
            activity?.SetTag("grain.id", request.GrainId.ToString());
            activity?.SetTag("method.id", request.MethodId.ToString());
            activity?.SetTag("remote.endpoint", remoteEndpoint.ToString());
            
            var stopwatch = Stopwatch.StartNew();
            
            _logger.LogDebug("RPC Server HandleRequest: Processing request {MessageId} for grain {GrainId} method {MethodId} from {Endpoint}, ConnectionId: {ConnectionId}", 
                request.MessageId, request.GrainId, request.MethodId, remoteEndpoint, actualConnectionId);

            RpcServerTelemetry.PendingRequests.Add(1);
            
            try
            {
                // Use the actual connection ID if provided (from transport), otherwise use endpoint
                var connectionId = actualConnectionId ?? remoteEndpoint.ToString();
                var connection = GetOrCreateConnection(connectionId, remoteEndpoint);
                
                // Process the request through the connection
                await connection.ProcessRequestAsync(request);
                
                stopwatch.Stop();
                RpcServerTelemetry.RecordRequest("unknown", request.MethodId.ToString(), stopwatch.Elapsed.TotalMilliseconds, true);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error handling request {MessageId}", request.MessageId);
                RpcServerTelemetry.RecordRequest("unknown", request.MethodId.ToString(), stopwatch.Elapsed.TotalMilliseconds, false, ex.GetType().Name);
                throw;
            }
            finally
            {
                RpcServerTelemetry.PendingRequests.Add(-1);
            }
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
                    _messagingOptions,
                    interfaceToImplementationMapping,
                    connectionLogger);
            });
        }

        private async Task HandleHeartbeat(Protocol.RpcHeartbeat heartbeat, IPEndPoint remoteEndpoint, string connectionId)
        {
            _logger.LogDebug("Received heartbeat from {SourceId}", heartbeat.SourceId);
            
            // Echo heartbeat back
            var response = new Protocol.RpcHeartbeat
            {
                SourceId = _serverDetails.ServerId
            };

            var messageSerializer = _catalog.ServiceProvider.GetRequiredService<Protocol.RpcMessageSerializer>();
            var responseData = messageSerializer.SerializeMessage(response);
            
            // Use SendToConnectionAsync when we have a connection ID
            if (!string.IsNullOrEmpty(connectionId))
            {
                await _transport.SendToConnectionAsync(connectionId, responseData, CancellationToken.None);
            }
            else
            {
                await _transport.SendAsync(remoteEndpoint, responseData, CancellationToken.None);
            }
        }

        private async Task HandleAsyncEnumerableRequest(Protocol.RpcAsyncEnumerableRequest request, IPEndPoint remoteEndpoint, string connectionId)
        {
            _logger.LogDebug("RPC Server HandleAsyncEnumerableRequest: Processing async enumerable request {StreamId} for grain {GrainId} method {MethodId}", 
                request.StreamId, request.GrainId, request.MethodId);

            // Get or create connection for async enumerable
            var connection = GetOrCreateConnection(connectionId ?? remoteEndpoint.ToString(), remoteEndpoint);
            
            // Process the async enumerable request through the connection
            await connection.ProcessAsyncEnumerableRequestAsync(request);
        }
        
        private async Task HandleAsyncEnumerableCancel(Protocol.RpcAsyncEnumerableCancel cancel, IPEndPoint remoteEndpoint, string connectionId)
        {
            _logger.LogDebug("RPC Server HandleAsyncEnumerableCancel: Cancelling stream {StreamId}", cancel.StreamId);

            // Get connection
            var connection = GetOrCreateConnection(connectionId ?? remoteEndpoint.ToString(), remoteEndpoint);
            
            // Cancel the stream
            await connection.CancelAsyncEnumerableAsync(cancel.StreamId);
        }

        private void OnConnectionEstablished(object sender, RpcConnectionEventArgs e)
        {
            using var activity = RpcServerTelemetry.StartActivityIfEnabled(RpcServerTelemetry.ConnectionActivity);
            activity?.SetTag("connection.id", e.ConnectionId);
            activity?.SetTag("remote.endpoint", e.RemoteEndPoint.ToString());
            activity?.SetTag("connection.event", "established");
            
            _logger.LogInformation("Connection established with {Endpoint} (ID: {ConnectionId})", 
                e.RemoteEndPoint, e.ConnectionId);
                
            RpcServerTelemetry.RecordConnection(true, e.RemoteEndPoint.ToString());
        }

        private void OnConnectionClosed(object sender, RpcConnectionEventArgs e)
        {
            using var activity = RpcServerTelemetry.StartActivityIfEnabled(RpcServerTelemetry.ConnectionActivity);
            activity?.SetTag("connection.id", e.ConnectionId);
            activity?.SetTag("remote.endpoint", e.RemoteEndPoint.ToString());
            activity?.SetTag("connection.event", "closed");
            
            _logger.LogInformation("Connection closed with {Endpoint} (ID: {ConnectionId})", 
                e.RemoteEndPoint, e.ConnectionId);
                
            RpcServerTelemetry.RecordConnection(false, e.RemoteEndPoint.ToString());
                
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
