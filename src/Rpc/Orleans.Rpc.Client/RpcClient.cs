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
using Orleans.Runtime;

namespace Granville.Rpc
{
    /// <summary>
    /// RPC client implementation.
    /// </summary>
    internal sealed class RpcClient : IClusterClient, IRpcClient, IHostedService
    {
        private readonly ILogger<RpcClient> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly RpcClientOptions _clientOptions;
        private readonly RpcTransportOptions _transportOptions;
        private readonly IRpcTransportFactory _transportFactory;
        private readonly IClusterClientLifecycle _lifecycle;
        private readonly RpcConnectionManager _connectionManager;
        private readonly IClusterManifestProvider _manifestProvider;
        private readonly RpcAsyncEnumerableManager _asyncEnumerableManager;
        
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<Protocol.RpcResponse>> _pendingRequests 
            = new ConcurrentDictionary<Guid, TaskCompletionSource<Protocol.RpcResponse>>();
        private readonly ConcurrentDictionary<string, IRpcTransport> _transports 
            = new ConcurrentDictionary<string, IRpcTransport>();

        public bool IsInitialized => _connectionManager?.GetAllConnections().Count > 0;
        public IServiceProvider ServiceProvider => _serviceProvider;

        public RpcClient(
            ILogger<RpcClient> logger,
            IServiceProvider serviceProvider,
            IOptions<RpcClientOptions> clientOptions,
            IOptions<RpcTransportOptions> transportOptions,
            IRpcTransportFactory transportFactory,
            IClusterClientLifecycle lifecycle,
            [FromKeyedServices("rpc")] IClusterManifestProvider manifestProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _clientOptions = clientOptions?.Value ?? throw new ArgumentNullException(nameof(clientOptions));
            _transportOptions = transportOptions?.Value ?? throw new ArgumentNullException(nameof(transportOptions));
            _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
            _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            _manifestProvider = manifestProvider ?? throw new ArgumentNullException(nameof(manifestProvider));
            
            _logger.LogInformation("RpcClient constructor called. ClientId: {ClientId}, Endpoints: {EndpointCount}", 
                _clientOptions.ClientId, _clientOptions.ServerEndpoints.Count);
            
            _logger.LogInformation("RpcClient created with manifest provider: {ManifestProviderType}", 
                _manifestProvider.GetType().FullName);
            
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            _connectionManager = new RpcConnectionManager(loggerFactory.CreateLogger<RpcConnectionManager>());
            
            var serializer = serviceProvider.GetRequiredService<Orleans.Serialization.Serializer>();
            _asyncEnumerableManager = new RpcAsyncEnumerableManager(loggerFactory.CreateLogger<RpcAsyncEnumerableManager>(), serializer);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting RPC client {ClientId}", _clientOptions.ClientId);
            
            try
            {
                // REMOVED: ConsumeServices was causing a circular dependency instead of breaking one
                // RpcClient → OutsideRpcRuntimeClient.ConsumeServices() → IGrainReferenceRuntime → 
                // RpcGrainReferenceRuntime → needs RpcClient (circular!)
                // The GrainReferenceRuntime will be lazily initialized when first needed instead
                _logger.LogDebug("Skipping ConsumeServices to avoid circular dependency");
                
                // Start lifecycle if available
                var lifecycleSubject = _lifecycle as ILifecycleSubject;
                if (lifecycleSubject != null)
                {
                    _logger.LogDebug("Lifecycle subject found, calling OnStart");
                    await lifecycleSubject.OnStart(cancellationToken);
                    _logger.LogDebug("Lifecycle OnStart completed");
                }
                else
                {
                    _logger.LogDebug("No lifecycle subject found");
                }
                
                // Check if we have any configured endpoints
                if (_clientOptions.ServerEndpoints.Count == 0)
                {
                    _logger.LogWarning("RPC client started but no server endpoints configured. Client will not connect to any servers.");
                    return;
                }
                
                _logger.LogDebug("Connecting to {EndpointCount} initial servers", _clientOptions.ServerEndpoints.Count);
                _logger.LogDebug("RPC Client: About to call ConnectToInitialServersAsync");
                await ConnectToInitialServersAsync(cancellationToken);
                _logger.LogDebug("RPC Client: ConnectToInitialServersAsync completed successfully");
                _logger.LogInformation("RPC client started successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start RPC client");
                throw;
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping RPC client");
            
            try
            {
                await DisconnectAllAsync();
                await (_lifecycle as ILifecycleSubject)?.OnStop(cancellationToken);
                _logger.LogInformation("RPC client stopped successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while stopping RPC client");
                throw;
            }
        }

        public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string grainClassNamePrefix = null) 
            where TGrainInterface : IGrainWithGuidKey
        {
            EnsureConnected();
            // Use keyed service to ensure we get RPC's grain factory
            var grainFactory = _serviceProvider.GetRequiredKeyedService<IGrainFactory>("rpc");
            return grainFactory.GetGrain<TGrainInterface>(primaryKey, grainClassNamePrefix);
        }

        public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string grainClassNamePrefix = null) 
            where TGrainInterface : IGrainWithIntegerKey
        {
            EnsureConnected();
            // Use keyed service to ensure we get RPC's grain factory
            var grainFactory = _serviceProvider.GetRequiredKeyedService<IGrainFactory>("rpc");
            return grainFactory.GetGrain<TGrainInterface>(primaryKey, grainClassNamePrefix);
        }

        public TGrainInterface GetGrain<TGrainInterface>(string primaryKey, string grainClassNamePrefix = null) 
            where TGrainInterface : IGrainWithStringKey
        {
            EnsureConnected();
            // Use keyed service to ensure we get RPC's grain factory
            var grainFactory = _serviceProvider.GetRequiredKeyedService<IGrainFactory>("rpc");
            return grainFactory.GetGrain<TGrainInterface>(primaryKey, grainClassNamePrefix);
        }

        public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string keyExtension, string grainClassNamePrefix = null) 
            where TGrainInterface : IGrainWithGuidCompoundKey
        {
            EnsureConnected();
            // Use keyed service to ensure we get RPC's grain factory
            var grainFactory = _serviceProvider.GetRequiredKeyedService<IGrainFactory>("rpc");
            return grainFactory.GetGrain<TGrainInterface>(primaryKey, keyExtension, grainClassNamePrefix);
        }

        public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string keyExtension, string grainClassNamePrefix = null) 
            where TGrainInterface : IGrainWithIntegerCompoundKey
        {
            EnsureConnected();
            // Use keyed service to ensure we get RPC's grain factory
            var grainFactory = _serviceProvider.GetRequiredKeyedService<IGrainFactory>("rpc");
            return grainFactory.GetGrain<TGrainInterface>(primaryKey, keyExtension, grainClassNamePrefix);
        }

        public TGrainInterface GetGrain<TGrainInterface>(GrainId grainId) where TGrainInterface : IAddressable
        {
            EnsureConnected();
            // Use keyed service to ensure we get RPC's grain factory
            var grainFactory = _serviceProvider.GetRequiredKeyedService<IGrainFactory>("rpc");
            return grainFactory.GetGrain<TGrainInterface>(grainId);
        }

        public IAddressable GetGrain(GrainId grainId)
        {
            EnsureConnected();
            // Use keyed service to ensure we get RPC's grain factory
            var grainFactory = _serviceProvider.GetRequiredKeyedService<IGrainFactory>("rpc");
            return grainFactory.GetGrain(grainId);
        }

        public IAddressable GetGrain(GrainId grainId, GrainInterfaceType interfaceType)
        {
            EnsureConnected();
            // Use keyed service to ensure we get RPC's grain factory
            var grainFactory = _serviceProvider.GetRequiredKeyedService<IGrainFactory>("rpc");
            return grainFactory.GetGrain(grainId, interfaceType);
        }

        public IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey)
        {
            EnsureConnected();
            // Use keyed service to ensure we get RPC's grain factory
            var grainFactory = _serviceProvider.GetRequiredKeyedService<IGrainFactory>("rpc");
            return grainFactory.GetGrain(grainInterfaceType, grainPrimaryKey);
        }

        public IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey)
        {
            EnsureConnected();
            // Use keyed service to ensure we get RPC's grain factory
            var grainFactory = _serviceProvider.GetRequiredKeyedService<IGrainFactory>("rpc");
            return grainFactory.GetGrain(grainInterfaceType, grainPrimaryKey);
        }

        public IGrain GetGrain(Type grainInterfaceType, string grainPrimaryKey)
        {
            EnsureConnected();
            // Use keyed service to ensure we get RPC's grain factory
            var grainFactory = _serviceProvider.GetRequiredKeyedService<IGrainFactory>("rpc");
            return grainFactory.GetGrain(grainInterfaceType, grainPrimaryKey);
        }

        public IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey, string grainClassNamePrefix)
        {
            EnsureConnected();
            // Use keyed service to ensure we get RPC's grain factory
            var grainFactory = _serviceProvider.GetRequiredKeyedService<IGrainFactory>("rpc");
            return grainFactory.GetGrain(grainInterfaceType, grainPrimaryKey, grainClassNamePrefix);
        }

        public IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey, string grainClassNamePrefix)
        {
            EnsureConnected();
            // Use keyed service to ensure we get RPC's grain factory
            var grainFactory = _serviceProvider.GetRequiredKeyedService<IGrainFactory>("rpc");
            return grainFactory.GetGrain(grainInterfaceType, grainPrimaryKey, grainClassNamePrefix);
        }

        public TGrainObserverInterface CreateObjectReference<TGrainObserverInterface>(IGrainObserver obj) 
            where TGrainObserverInterface : IGrainObserver
        {
            throw new NotSupportedException("Grain observers are not supported in RPC mode");
        }

        public void DeleteObjectReference<TGrainObserverInterface>(IGrainObserver obj) 
            where TGrainObserverInterface : IGrainObserver
        {
            // Not supported in RPC mode
        }

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

        public void Dispose()
        {
            DisconnectAllAsync().GetAwaiter().GetResult();
            _connectionManager?.Dispose();
        }
    }

    /// <summary>
    /// RPC client interface.
    /// </summary>
    public interface IRpcClient : IClusterClient
    {
    }
}
