using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Granville.Rpc.Configuration;
using Granville.Rpc.Telemetry;
using Granville.Rpc.Transport;
using Granville.Rpc.Zones;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Serialization.Session;
using Orleans.Metadata;

namespace Granville.Rpc
{
    /// <summary>
    /// Outside runtime client for RPC.
    /// This follows Orleans' pattern of separating implementation from public API.
    /// </summary>
    internal sealed class OutsideRpcClient : IDisposable
    {
        private readonly ILogger<OutsideRpcClient> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly RpcClientOptions _clientOptions;
        private readonly RpcTransportOptions _transportOptions;
        private readonly IRpcTransportFactory _transportFactory;
        private readonly RpcConnectionManager _connectionManager;
        private IClusterManifestProvider _manifestProvider;
        private readonly RpcAsyncEnumerableManager _asyncEnumerableManager;
        
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<Protocol.RpcResponse>> _pendingRequests 
            = new ConcurrentDictionary<Guid, TaskCompletionSource<Protocol.RpcResponse>>();
        private readonly ConcurrentDictionary<string, IRpcTransport> _transports 
            = new ConcurrentDictionary<string, IRpcTransport>();
        
        private IInternalGrainFactory _internalGrainFactory;
        private IZoneDetectionStrategy _zoneDetectionStrategy;
        private Serializer _serializer;
        private SerializerSessionPool _sessionPool;
        private RpcSerializationSessionFactory _sessionFactory;
        private bool _servicesConsumed = false;
        private readonly object _servicesLock = new object();

        public bool IsInitialized => _connectionManager?.GetAllConnections().Count > 0;
        public IServiceProvider ServiceProvider => _serviceProvider;
        public IInternalGrainFactory InternalGrainFactory 
        {
            get
            {
                EnsureServicesResolved();
                return _internalGrainFactory;
            }
        }

        public OutsideRpcClient(
            ILogger<OutsideRpcClient> logger,
            IServiceProvider serviceProvider,
            IOptions<RpcClientOptions> clientOptions,
            IOptions<RpcTransportOptions> transportOptions,
            IRpcTransportFactory transportFactory,
            ILoggerFactory loggerFactory,
            RpcSerializationSessionFactory sessionFactory)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _clientOptions = clientOptions?.Value ?? throw new ArgumentNullException(nameof(clientOptions));
            _transportOptions = transportOptions?.Value ?? throw new ArgumentNullException(nameof(transportOptions));
            _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
            
            _logger.LogInformation("OutsideRpcClient constructor called. ClientId: {ClientId}, Endpoints: {EndpointCount}", 
                _clientOptions.ClientId, _clientOptions.ServerEndpoints.Count);
            _connectionManager = new RpcConnectionManager(loggerFactory.CreateLogger<RpcConnectionManager>());
            _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
            
            _serializer = serviceProvider.GetRequiredService<Orleans.Serialization.Serializer>();
            _sessionPool = serviceProvider.GetRequiredService<SerializerSessionPool>();
            _asyncEnumerableManager = new RpcAsyncEnumerableManager(loggerFactory.CreateLogger<RpcAsyncEnumerableManager>(), _serializer);
            
            _logger.LogDebug("OutsideRpcClient constructor completed");
        }

        /// <summary>
        /// Consume services from the service provider to break circular dependencies.
        /// This must be called after the DI container is fully built.
        /// </summary>
        public void ConsumeServices()
        {
            lock (_servicesLock)
            {
                if (_servicesConsumed)
                {
                    return;
                }
                
                try
                {
                    _logger.LogDebug("ConsumeServices called");
                    
                    // Note: We delay the actual service resolution to avoid circular dependencies during startup
                    // The services will be resolved on first use instead
                    _logger.LogDebug("ConsumeServices marking as consumed without resolving services (lazy initialization)");
                    
                    _servicesConsumed = true;
                    _logger.LogInformation("ConsumeServices completed (services will be resolved on first use)");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in ConsumeServices. Stack trace: {StackTrace}", ex.StackTrace);
                    throw;
                }
            }
        }
        
        private void EnsureServicesResolved()
        {
            lock (_servicesLock)
            {
                if (_manifestProvider != null && _internalGrainFactory != null)
                {
                    return;
                }
                
                try
                {
                    _logger.LogDebug("EnsureServicesResolved: Resolving services on first use");
                    
                    if (_manifestProvider == null)
                    {
                        _logger.LogDebug("Attempting to resolve IClusterManifestProvider with key 'rpc'");
                        _manifestProvider = ServiceProvider.GetRequiredKeyedService<IClusterManifestProvider>("rpc");
                        _logger.LogDebug("Successfully resolved IClusterManifestProvider: {Type}", _manifestProvider?.GetType().FullName ?? "null");
                    }
                    
                    if (_internalGrainFactory == null)
                    {
                        _logger.LogDebug("Attempting to resolve IGrainFactory with key 'rpc'");
                        _internalGrainFactory = ServiceProvider.GetRequiredKeyedService<IGrainFactory>("rpc") as IInternalGrainFactory;
                        _logger.LogDebug("Successfully resolved IGrainFactory: {Type}", _internalGrainFactory?.GetType().FullName ?? "null");
                    }
                    
                    if (_zoneDetectionStrategy == null)
                    {
                        _logger.LogDebug("Attempting to resolve IZoneDetectionStrategy");
                        _zoneDetectionStrategy = ServiceProvider.GetService<IZoneDetectionStrategy>();
                        _logger.LogDebug("Resolved IZoneDetectionStrategy: {Type}", _zoneDetectionStrategy?.GetType().FullName ?? "null");
                        
                        // Set zone detection strategy if available
                        if (_zoneDetectionStrategy != null)
                        {
                            _connectionManager.SetZoneDetectionStrategy(_zoneDetectionStrategy);
                            _logger.LogInformation("Configured with zone detection strategy: {StrategyType}", 
                                _zoneDetectionStrategy.GetType().Name);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error resolving services. Stack trace: {StackTrace}", ex.StackTrace);
                    throw;
                }
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting OutsideRpcClient {ClientId}", _clientOptions.ClientId);
            
            try
            {
                // Note: We don't call ConsumeServices here anymore to avoid potential deadlocks
                // during IHostedService startup. Services will be resolved on first use instead.
                _logger.LogDebug("StartAsync: Marking services as consumed for lazy initialization");
                lock (_servicesLock)
                {
                    _servicesConsumed = true;
                }
                
                // Check if we have any configured endpoints
                _logger.LogInformation("RPC client has {EndpointCount} configured endpoints", _clientOptions.ServerEndpoints.Count);
                if (_clientOptions.ServerEndpoints.Count == 0)
                {
                    _logger.LogWarning("RPC client started but no server endpoints configured. Client will not connect to any servers.");
                    return;
                }
                
                // Log the endpoints
                foreach (var endpoint in _clientOptions.ServerEndpoints)
                {
                    _logger.LogInformation("Configured endpoint: {Endpoint}", endpoint);
                }
                
                _logger.LogInformation("Connecting to {EndpointCount} initial servers", _clientOptions.ServerEndpoints.Count);
                await ConnectToInitialServersAsync(cancellationToken);
                _logger.LogInformation("OutsideRpcClient started successfully");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("StartAsync was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start OutsideRpcClient. Exception type: {ExceptionType}, Message: {Message}", 
                    ex.GetType().FullName, ex.Message);
                throw;
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping OutsideRpcClient");
            
            try
            {
                await DisconnectAllAsync();
                _logger.LogInformation("OutsideRpcClient stopped successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while stopping OutsideRpcClient");
                throw;
            }
        }

        // GetGrain methods removed - these are now in RpcClusterClient which delegates to InternalGrainFactory

        private async Task ConnectToInitialServersAsync(CancellationToken cancellationToken)
        {
            if (_clientOptions.ServerEndpoints.Count == 0)
            {
                throw new InvalidOperationException("No server endpoints configured");
            }

            // Connect to all configured endpoints
            var connectTasks = new System.Collections.Generic.List<Task>();
            foreach (var endpoint in _clientOptions.ServerEndpoints)
            {
                connectTasks.Add(ConnectToServerAsync(endpoint, null, cancellationToken));
            }

            await Task.WhenAll(connectTasks);
        }

        /// <summary>
        /// Connects to a specific server endpoint.
        /// </summary>
        public async Task ConnectToServerAsync(IPEndPoint endpoint, string serverId = null, CancellationToken cancellationToken = default)
        {
            serverId ??= $"server-{endpoint.Address}:{endpoint.Port}";
            
            using var activity = RpcClientTelemetry.StartActivityIfEnabled(RpcClientTelemetry.ConnectionActivity);
            activity?.SetTag("server.id", serverId);
            activity?.SetTag("server.endpoint", endpoint.ToString());
            
            var stopwatch = Stopwatch.StartNew();
            
            _logger.LogInformation("Connecting to RPC server {ServerId} at {Endpoint}", serverId, endpoint);
            
            RpcClientTelemetry.RecordConnection("attempt", true, 0, serverId);
            
            var transport = _transportFactory.CreateTransport(_serviceProvider);
            
            // Create connection wrapper and subscribe to events BEFORE connecting
            var connection = new RpcConnection(serverId, endpoint, transport, _logger);
            connection.DataReceived += OnDataReceived;
            connection.ConnectionEstablished += OnConnectionEstablished;
            connection.ConnectionClosed += OnConnectionClosed;
            
            // Track the transport BEFORE connecting so event handlers are ready
            _logger.LogDebug("RPC Client: Tracking transport for {ServerId} before connection", serverId);
            _transports[serverId] = transport;
            
            // Add to connection manager BEFORE connecting so it's ready to handle events
            _logger.LogDebug("RPC Client: Adding connection to manager for {ServerId} before connection", serverId);
            await _connectionManager.AddConnectionAsync(serverId, connection);
            
            try
            {
                // Connect to the server - now all event handlers are ready
                _logger.LogDebug("RPC Client: About to call transport.ConnectAsync for {ServerId} at {Endpoint}", serverId, endpoint);
                await transport.ConnectAsync(endpoint, cancellationToken);
                _logger.LogDebug("RPC Client: transport.ConnectAsync completed successfully for {ServerId}", serverId);
                
                stopwatch.Stop();
                
                // Send handshake
                _logger.LogDebug("RPC Client: About to send handshake to {ServerId}", serverId);
                await SendHandshake(connection);
                _logger.LogDebug("RPC Client: Handshake sent successfully to {ServerId}", serverId);
                
                _logger.LogInformation("Successfully connected to RPC server {ServerId}", serverId);
                
                RpcClientTelemetry.RecordConnection("established", true, stopwatch.Elapsed.TotalMilliseconds, serverId);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Failed to connect to RPC server {ServerId} at {Endpoint}", serverId, endpoint);
                
                RpcClientTelemetry.RecordConnection("established", false, stopwatch.Elapsed.TotalMilliseconds, serverId, ex.GetType().Name);
                
                // Clean up on failure
                await _connectionManager.RemoveConnectionAsync(serverId);
                _transports.TryRemove(serverId, out _);
                
                connection.Dispose();
                transport.Dispose();
                throw;
            }
        }

        private async Task DisconnectAllAsync()
        {
            var connections = _connectionManager.GetAllConnections();
            foreach (var kvp in connections)
            {
                await _connectionManager.RemoveConnectionAsync(kvp.Key);
                
                // Dispose the transport for this connection
                if (_transports.TryRemove(kvp.Key, out var transport))
                {
                    try
                    {
                        await transport.StopAsync(CancellationToken.None);
                        transport.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error disposing transport for server {ServerId}", kvp.Key);
                    }
                }
            }
        }

        private void EnsureConnected()
        {
            var connectionCount = _connectionManager?.GetAllConnections().Count ?? 0;
            _logger.LogDebug("EnsureConnected called, connection count = {ConnectionCount}", connectionCount);
            if (connectionCount == 0)
            {
                throw new InvalidOperationException("RPC client is not connected to any servers");
            }
        }

        // GetGrainFactory removed - now using InternalGrainFactory property set in ConsumeServices

        private async void OnDataReceived(object sender, RpcDataReceivedEventArgs e)
        {
            try
            {
                _logger.LogDebug("RPC Client OnDataReceived: Received {ByteCount} bytes from {Endpoint}, ConnectionId: {ConnectionId}", 
                    e.Data.Length, e.RemoteEndPoint, e.ConnectionId);
                
                // Deserialize the message
                var messageSerializer = _serviceProvider.GetRequiredService<Protocol.RpcMessageSerializer>();
                var message = messageSerializer.DeserializeMessage(e.Data);
                _logger.LogDebug("RPC Client OnDataReceived: Deserialized message type: {MessageType}", message.GetType().Name);

                // Handle different message types
                switch (message)
                {
                    case Protocol.RpcHandshake handshake:
                        _logger.LogInformation("Received handshake response from server {ServerId}", handshake.ClientId);
                        break;
                        
                    case Protocol.RpcHandshakeAck handshakeAck:
                        // Extract server ID from connection that sent this
                        var serverId = e.ConnectionId ?? "unknown";
                        await HandleHandshakeAck(handshakeAck, serverId);
                        break;
                        
                    case Protocol.RpcResponse response:
                        HandleResponse(response);
                        break;
                        
                    case Protocol.RpcHeartbeat heartbeat:
                        _logger.LogDebug("Received heartbeat from server {ServerId}", heartbeat.SourceId);
                        break;
                        
                    case Protocol.RpcAsyncEnumerableItem asyncEnumerableItem:
                        await _asyncEnumerableManager.ProcessAsyncEnumerableItem(asyncEnumerableItem);
                        break;
                        
                    default:
                        _logger.LogWarning("Received unexpected message type: {Type}", message.GetType().Name);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing received data from server");
            }
        }

        private async Task HandleHandshakeAck(Protocol.RpcHandshakeAck handshakeAck, string serverId)
        {
            _logger.LogInformation("Received handshake acknowledgment from server {ServerId}, manifest included: {HasManifest}, zone: {ZoneId}", 
                handshakeAck.ServerId, handshakeAck.GrainManifest != null, handshakeAck.ZoneId);
            
            if (handshakeAck.GrainManifest != null)
            {
                _logger.LogInformation("Handshake manifest details: {GrainCount} grains, {InterfaceCount} interfaces, {MappingCount} mappings",
                    handshakeAck.GrainManifest.GrainProperties?.Count ?? 0,
                    handshakeAck.GrainManifest.InterfaceProperties?.Count ?? 0,
                    handshakeAck.GrainManifest.InterfaceToGrainMappings?.Count ?? 0);
            }
            
            // Update zone mappings if provided
            if (handshakeAck.ZoneId.HasValue)
            {
                await _connectionManager.AddConnectionAsync(serverId, 
                    _connectionManager.GetConnection(serverId), 
                    handshakeAck.ZoneId.Value);
            }
            
            if (handshakeAck.ZoneToServerMapping != null)
            {
                _connectionManager.UpdateZoneMappings(handshakeAck.ZoneToServerMapping);
            }
            
            // Update the manifest provider with server's grain manifest
            if (handshakeAck.GrainManifest != null)
            {
                EnsureServicesResolved();
                // Cast to the concrete type to access the UpdateFromServerAsync method
                if (_manifestProvider is MultiServerManifestProvider multiServerManifestProvider)
                {
                    await multiServerManifestProvider.UpdateFromServerAsync(serverId, handshakeAck.GrainManifest);
                    _logger.LogInformation("Updated manifest for server {ServerId} with {GrainCount} grains and {InterfaceCount} interfaces",
                        serverId,
                        handshakeAck.GrainManifest.GrainProperties.Count,
                        handshakeAck.GrainManifest.InterfaceProperties.Count);
                }
                else
                {
                    _logger.LogWarning("Manifest provider is not MultiServerManifestProvider, cannot update from server");
                }
            }
            else
            {
                _logger.LogWarning("Server handshake acknowledgment did not include grain manifest");
            }
        }

        private void HandleResponse(Protocol.RpcResponse response)
        {
            _logger.LogDebug("RPC Client HandleResponse: Received response for request {RequestId}, Success: {Success}, ErrorMessage: {ErrorMessage}", 
                response.RequestId, response.Success, response.ErrorMessage);
                
            if (_pendingRequests.TryRemove(response.RequestId, out var tcs))
            {
                
                if (response.Success)
                {
                    tcs.SetResult(response);
                }
                else
                {
                    tcs.SetException(new Exception(response.ErrorMessage ?? "RPC call failed"));
                }
            }
            else
            {
                _logger.LogWarning("Received response for unknown request {RequestId}", response.RequestId);
            }
        }

        private Task SendHandshake(RpcConnection connection)
        {
            _logger.LogDebug("RPC Client SendHandshake: Creating handshake message for {ServerId}", connection.ServerId);
            var handshake = new Protocol.RpcHandshake
            {
                ClientId = _clientOptions.ClientId,
                ProtocolVersion = 1,
                Features = new[] { "basic-rpc" }
            };

            _logger.LogDebug("RPC Client SendHandshake: Getting message serializer");
            var messageSerializer = _serviceProvider.GetRequiredService<Protocol.RpcMessageSerializer>();
            _logger.LogDebug("RPC Client SendHandshake: Serializing handshake message");
            var data = messageSerializer.SerializeMessage(handshake);
            _logger.LogDebug("RPC Client SendHandshake: Handshake serialized to {ByteCount} bytes", data.Length);
            
            _logger.LogInformation("Sent handshake to server {ServerId}", connection.ServerId);
            _logger.LogDebug("RPC Client SendHandshake: About to call connection.SendAsync");
            var sendTask = connection.SendAsync(data, CancellationToken.None);
            _logger.LogDebug("RPC Client SendHandshake: connection.SendAsync call initiated");
            return sendTask;
        }

        private void OnConnectionEstablished(object sender, RpcConnectionEventArgs e)
        {
            _logger.LogInformation("Connected to RPC server {ServerId} at {Endpoint}", e.ConnectionId, e.RemoteEndPoint);
        }

        private void OnConnectionClosed(object sender, RpcConnectionEventArgs e)
        {
            _logger.LogWarning("Connection to RPC server {ServerId} at {Endpoint} closed", e.ConnectionId, e.RemoteEndPoint);
            
            // Remove the closed connection
            if (e.ConnectionId != null)
            {
                _ = Task.Run(async () =>
                {
                    await _connectionManager.RemoveConnectionAsync(e.ConnectionId);
                    
                    // Remove the manifest for this server to ensure clean state on reconnection
                    EnsureServicesResolved();
                    if (_manifestProvider is MultiServerManifestProvider multiServerManifestProvider)
                    {
                        await multiServerManifestProvider.RemoveServerManifestAsync(e.ConnectionId);
                        _logger.LogInformation("Removed manifest for disconnected server {ServerId}", e.ConnectionId);
                    }
                    
                    // Dispose the transport for this connection
                    if (_transports.TryRemove(e.ConnectionId, out var transport))
                    {
                        try
                        {
                            await transport.StopAsync(CancellationToken.None);
                            transport.Dispose();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error disposing transport for server {ServerId}", e.ConnectionId);
                        }
                    }
                });
            }
            
            // TODO: Implement reconnection logic if needed
        }

        internal async Task<Protocol.RpcResponse> SendRequestAsync(Protocol.RpcRequest request)
        {
            EnsureConnected();
            
            using var activity = RpcClientTelemetry.StartActivityIfEnabled(RpcClientTelemetry.RequestActivity);
            activity?.SetTag("request.id", request.MessageId.ToString());
            activity?.SetTag("grain.id", request.GrainId.ToString());
            activity?.SetTag("method.id", request.MethodId.ToString());
            
            var stopwatch = Stopwatch.StartNew();
            
            // Get the appropriate connection for this request
            var connection = _connectionManager.GetConnectionForRequest(request);
            if (connection == null)
            {
                throw new InvalidOperationException("No suitable connection found for request");
            }

            activity?.SetTag("server.id", connection.ServerId);

            var tcs = new TaskCompletionSource<Protocol.RpcResponse>();
            if (!_pendingRequests.TryAdd(request.MessageId, tcs))
            {
                throw new InvalidOperationException($"Duplicate request ID: {request.MessageId}");
            }

            RpcClientTelemetry.RecordPendingRequest(true);

            try
            {
                // Serialize and send the request
                var messageSerializer = _serviceProvider.GetRequiredService<Protocol.RpcMessageSerializer>();
                var data = messageSerializer.SerializeMessage(request);
                
                RpcClientTelemetry.MessageSize.Record(data.Length, new KeyValuePair<string, object?>("direction", "outbound"));
                
                _logger.LogInformation("RPC Client: Sending request {MessageId} to server {ServerId}, data size: {DataSize} bytes", 
                    request.MessageId, connection.ServerId, data.Length);
                    
                RpcClientTelemetry.RequestsSent.Add(1,
                    new KeyValuePair<string, object?>("grain.id", request.GrainId.ToString()),
                    new KeyValuePair<string, object?>("method.id", request.MethodId.ToString()),
                    new KeyValuePair<string, object?>("server.id", connection.ServerId));
                    
                await connection.SendAsync(data, CancellationToken.None);

                // Wait for response with timeout
                using (var cts = new CancellationTokenSource(request.TimeoutMs))
                {
                    var response = await tcs.Task.WaitAsync(cts.Token);
                    
                    stopwatch.Stop();
                    RpcClientTelemetry.RecordRequest("unknown", request.MethodId.ToString(), stopwatch.Elapsed.TotalMilliseconds, response.Success);
                    
                    return response;
                }
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                _pendingRequests.TryRemove(request.MessageId, out _);
                _logger.LogError("Request {MessageId} timed out after {TimeoutMs}ms", request.MessageId, request.TimeoutMs);
                
                RpcClientTelemetry.RecordRequest("unknown", request.MethodId.ToString(), stopwatch.Elapsed.TotalMilliseconds, false, true);
                
                throw new TimeoutException($"RPC request {request.MessageId} timed out after {request.TimeoutMs}ms");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _pendingRequests.TryRemove(request.MessageId, out _);
                _logger.LogError(ex, "Error in SendRequestAsync for request {MessageId}", request.MessageId);
                
                RpcClientTelemetry.RecordRequest("unknown", request.MethodId.ToString(), stopwatch.Elapsed.TotalMilliseconds, false, false, ex.GetType().Name);
                
                throw;
            }
            finally
            {
                RpcClientTelemetry.RecordPendingRequest(false);
            }
        }

        /// <summary>
        /// Sends any RPC message and returns a response.
        /// </summary>
        internal async Task<Protocol.RpcResponse> SendRequestAsync(Protocol.RpcMessage message)
        {
            EnsureConnected();
            
            // For non-request messages, we need to send them and potentially wait for acknowledgment
            var messageId = message.MessageId;
            RpcConnection connection = null;
            
            // Determine which connection to use based on message type
            if (message is Protocol.RpcRequest request)
            {
                return await SendRequestAsync(request);
            }
            else if (message is Protocol.RpcAsyncEnumerableRequest asyncEnumReq)
            {
                // For async enumerable requests, route based on grain ID
                connection = _connectionManager.GetConnectionForRequest(new Protocol.RpcRequest 
                { 
                    GrainId = asyncEnumReq.GrainId,
                    InterfaceType = asyncEnumReq.InterfaceType 
                });
            }
            else if (message is Protocol.RpcAsyncEnumerableCancel)
            {
                // For async enumerable cancel, use any available connection
                var connections = _connectionManager.GetAllConnections();
                if (connections.Count == 0)
                {
                    throw new InvalidOperationException("No connections available");
                }
                connection = connections.Values.First();
            }
            else
            {
                throw new NotSupportedException($"Unsupported message type for SendRequestAsync: {message.GetType()}");
            }
            
            if (connection == null)
            {
                throw new InvalidOperationException("No suitable connection found for message");
            }
            
            var tcs = new TaskCompletionSource<Protocol.RpcResponse>();
            if (!_pendingRequests.TryAdd(messageId, tcs))
            {
                throw new InvalidOperationException($"Duplicate message ID: {messageId}");
            }

            try
            {
                _logger.LogDebug("Sending {MessageType} {MessageId} via connection to {Endpoint}", 
                    message.GetType().Name, messageId, connection.Endpoint);
                
                // Serialize and send the message
                var messageSerializer = _serviceProvider.GetRequiredService<Protocol.RpcMessageSerializer>();
                var messageData = messageSerializer.SerializeMessage(message);
                await connection.SendAsync(messageData, CancellationToken.None);
                
                // Wait for response with timeout
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                return await tcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                _pendingRequests.TryRemove(messageId, out _);
                _logger.LogError("Message {MessageId} timed out", messageId);
                throw new TimeoutException($"RPC message {messageId} timed out");
            }
            catch (Exception ex)
            {
                _pendingRequests.TryRemove(messageId, out _);
                _logger.LogError(ex, "Error in SendRequestAsync for message {MessageId}", messageId);
                throw;
            }
        }
        
        /// <summary>
        /// Gets the async enumerable manager for this client.
        /// </summary>
        internal RpcAsyncEnumerableManager AsyncEnumerableManager => _asyncEnumerableManager;

        public async Task WaitForManifestAsync(TimeSpan timeout = default)
        {
            EnsureServicesResolved();
            var manifestProvider = _manifestProvider as MultiServerManifestProvider;
            if (manifestProvider == null) 
            {
                _logger.LogWarning("Manifest provider is not MultiServerManifestProvider, cannot wait for manifest");
                return;
            }
            
            timeout = timeout == default ? TimeSpan.FromSeconds(10) : timeout;
            var cts = new CancellationTokenSource(timeout);
            var startTime = Stopwatch.StartNew();
            
            _logger.LogInformation("Waiting for manifest to be populated (timeout: {Timeout})", timeout);
            
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var manifest = manifestProvider.Current;
                    if (manifest?.AllGrainManifests.Any(m => m.Grains.Count > 0) == true)
                    {
                        _logger.LogInformation("Manifest ready after {ElapsedMs}ms - {GrainCount} grains, {InterfaceCount} interfaces",
                            startTime.ElapsedMilliseconds,
                            manifest.AllGrainManifests.Sum(m => m.Grains.Count),
                            manifest.AllGrainManifests.Sum(m => m.Interfaces.Count));
                        return;
                    }
                    
                    await Task.Delay(50, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Timeout reached
            }
            
            throw new TimeoutException($"Manifest not populated within {timeout}. Ensure at least one RPC server is running and accessible.");
        }

        /// <summary>
        /// Invokes an RPC method on a grain.
        /// </summary>
        /// <summary>
        /// Invoke RPC method using grain type directly (for Orleans-generated proxies)
        /// </summary>
        internal async Task<T> InvokeRpcMethodAsync<T>(string grainKey, GrainType grainType, int methodId, object[] arguments)
        {
            _logger.LogDebug("InvokeRpcMethodAsync called with {ArgCount} arguments for grain {GrainType}/{GrainKey} method {MethodId}", 
                arguments?.Length ?? 0, grainType, grainKey, methodId);
            
            // Log the actual arguments
            if (arguments != null)
            {
                for (int i = 0; i < arguments.Length; i++)
                {
                    _logger.LogDebug("Argument[{Index}]: Type={Type}, Value={Value}", 
                        i, arguments[i]?.GetType()?.FullName ?? "null", arguments[i]?.ToString() ?? "null");
                }
            }
            
            // Serialize the arguments using isolated session for value-based serialization
            var serializedArgs = new byte[0];
            if (arguments != null && arguments.Length > 0)
            {
                serializedArgs = _sessionFactory.SerializeArgumentsWithIsolatedSession(_serializer, arguments);
            }

            EnsureServicesResolved();
            
            _logger.LogDebug("InvokeRpcMethodAsync (GrainType): grainKey={GrainKey}, grainType={GrainType}", grainKey, grainType);
            
            // Use the grain type directly
            var grainId = GrainId.Create(grainType, grainKey);
            
            // Create and send the RPC request  
            var request = new Protocol.RpcRequest
            {
                MessageId = Guid.NewGuid(),
                GrainId = grainId,
                MethodId = methodId,
                Arguments = serializedArgs,
                TimeoutMs = 30000
            };
            
            _logger.LogDebug("Sending RPC request {MessageId} for grain {GrainId} method {MethodId}", 
                request.MessageId, grainId, methodId);
            
            var response = await SendRequestAsync(request);
            
            if (!response.Success)
            {
                throw new Exception(response.ErrorMessage ?? "Unknown RPC error");
            }
            
            // Deserialize the response
            if (response.Payload != null && response.Payload.Length > 0)
            {
                using var responseSession = _sessionPool.GetSession();
                
                // Check for Orleans binary marker and skip it if present
                var payload = response.Payload;
                if (payload.Length > 0 && payload[0] == 0x00)
                {
                    // Skip the Orleans binary marker
                    payload = payload[1..];
                    _logger.LogDebug("Skipping Orleans binary marker in response payload");
                }
                
                return _serializer.Deserialize<T>(payload, responseSession);
            }
            
            return default(T);
        }

        internal async Task<T> InvokeRpcMethodAsync<T>(string grainKey, GrainInterfaceType interfaceType, int methodId, object[] arguments)
        {
            // Serialize the arguments using isolated session for value-based serialization
            var serializedArgs = new byte[0];
            if (arguments != null && arguments.Length > 0)
            {
                serializedArgs = _sessionFactory.SerializeArgumentsWithIsolatedSession(_serializer, arguments);
            }

            // Resolve the grain type from the interface type
            EnsureServicesResolved();
            
            _logger.LogDebug("InvokeRpcMethodAsync: grainKey={GrainKey}, interfaceType={InterfaceType}", grainKey, interfaceType);
            
            // Get the GrainInterfaceTypeToGrainTypeResolver
            var interfaceToTypeResolver = _serviceProvider.GetRequiredKeyedService<GrainInterfaceTypeToGrainTypeResolver>("rpc");
            
            // Resolve the grain type from the interface type
            GrainType grainType;
            if (interfaceToTypeResolver.TryGetGrainType(interfaceType, out var resolvedGrainType))
            {
                grainType = resolvedGrainType;
                _logger.LogInformation("Successfully resolved grain type {GrainType} for interface {InterfaceType}", grainType, interfaceType);
            }
            else
            {
                // Fallback: if we can't resolve, log an error and try to continue
                _logger.LogError("Could not resolve grain type for interface {InterfaceType}, using convention-based fallback", interfaceType);
                
                // First try to look up in manifest if available
                if (_manifestProvider.Current != null)
                {
                    _logger.LogDebug("Trying to find grain type in manifest...");
                    foreach (var grainManifest in _manifestProvider.Current.AllGrainManifests)
                    {
                        _logger.LogDebug("Checking manifest with {GrainCount} grains", grainManifest.Grains.Count);
                        foreach (var grain in grainManifest.Grains)
                        {
                            _logger.LogDebug("Checking grain {GrainType} with {PropCount} properties", grain.Key, grain.Value.Properties.Count);
                            // Check if this grain implements the interface
                            // Look for interface properties (e.g., "interface.0", "interface.1", etc.)
                            foreach (var prop in grain.Value.Properties)
                            {
                                if (prop.Key.StartsWith("interface.") && prop.Key != "interface.count")
                                {
                                    var implementedInterface = prop.Value;
                                    _logger.LogDebug("  Grain {GrainType} implements interface {Interface}", grain.Key, implementedInterface);
                                    
                                    if (implementedInterface == interfaceType.ToString())
                                    {
                                        grainType = grain.Key;
                                        _logger.LogInformation("Found grain type {GrainType} in manifest for interface {InterfaceType}", grainType, interfaceType);
                                        goto foundInManifest;
                                    }
                                }
                            }
                        }
                    }
                }
                
                // If not found in manifest, use convention
                var typeName = char.ToUpper(grainKey[0]) + grainKey.Substring(1);
                grainType = GrainType.Create($"Shooter.ActionServer.Grains.{typeName}RpcGrain"); 
                _logger.LogWarning("Using convention-based grain type {GrainType} for interface {InterfaceType}", grainType, interfaceType);
                
                foundInManifest:;
            }
            
            _logger.LogInformation("Creating GrainId with grainType={GrainType}, grainKey={GrainKey}", grainType, grainKey);
            var grainId = GrainId.Create(grainType, IdSpan.Create(grainKey));

            var request = new Protocol.RpcRequest
            {
                MessageId = Guid.NewGuid(),
                GrainId = grainId,
                InterfaceType = interfaceType,
                MethodId = methodId,
                Arguments = serializedArgs,
                Timestamp = DateTime.UtcNow,
                ReturnTypeName = typeof(T).FullName ?? string.Empty
            };

            var response = await SendRequestAsync(request);
            
            if (!response.Success)
            {
                // Deserialize the error
                if (response.Payload != null && response.Payload.Length > 0)
                {
                    try
                    {
                        var error = _serializer.Deserialize<string>(response.Payload);
                        throw new Exception($"RPC call failed: {error}");
                    }
                    catch
                    {
                        throw new Exception($"RPC call failed with unknown error");
                    }
                }
                throw new Exception($"RPC call failed: {response.ErrorMessage}");
            }

            if (response.Payload == null || response.Payload.Length == 0)
            {
                return default(T);
            }

            // Deserialize the result
            try
            {
                using var responseSession = _sessionPool.GetSession();
                
                // Check for Orleans binary marker and skip it if present
                var payload = response.Payload;
                if (payload.Length > 0 && payload[0] == 0x00)
                {
                    // Skip the Orleans binary marker
                    payload = payload[1..];
                    _logger.LogDebug("Skipping Orleans binary marker in response payload");
                }
                
                var result = _serializer.Deserialize<T>(payload, responseSession);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize RPC result to {ExpectedType}", typeof(T).FullName);
                throw new InvalidCastException($"Cannot deserialize RPC result to {typeof(T)}", ex);
            }
        }

        public void Dispose()
        {
            DisconnectAllAsync().GetAwaiter().GetResult();
            _connectionManager?.Dispose();
        }
    }
}
