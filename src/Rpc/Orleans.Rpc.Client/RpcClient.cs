using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Forkleans.Rpc.Configuration;
using Forkleans.Rpc.Transport;
using Forkleans.Runtime;

namespace Forkleans.Rpc
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
            
            _logger.LogInformation("RpcClient created with manifest provider: {ManifestProviderType}", 
                _manifestProvider.GetType().FullName);
            
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            _connectionManager = new RpcConnectionManager(loggerFactory.CreateLogger<RpcConnectionManager>());
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting RPC client {ClientId}", _clientOptions.ClientId);
            
            try
            {
                // Call ConsumeServices on the runtime client to break circular dependencies
                var runtimeClient = _serviceProvider.GetService<IRuntimeClient>() as OutsideRpcRuntimeClient;
                if (runtimeClient != null)
                {
                    runtimeClient.ConsumeServices();
                    _logger.LogDebug("ConsumeServices called on OutsideRpcRuntimeClient");
                }
                
                await (_lifecycle as ILifecycleSubject)?.OnStart(cancellationToken);
                await ConnectToInitialServersAsync(cancellationToken);
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
            
            _logger.LogInformation("Connecting to RPC server {ServerId} at {Endpoint}", serverId, endpoint);
            
            var transport = _transportFactory.CreateTransport(_serviceProvider);
            
            // Create connection wrapper
            var connection = new RpcConnection(serverId, endpoint, transport, _logger);
            connection.DataReceived += OnDataReceived;
            connection.ConnectionEstablished += OnConnectionEstablished;
            connection.ConnectionClosed += OnConnectionClosed;
            
            try
            {
                // Connect to the server
                await transport.ConnectAsync(endpoint, cancellationToken);
                
                // Track the transport so we can dispose it later
                _transports[serverId] = transport;
                
                // Add to connection manager
                await _connectionManager.AddConnectionAsync(serverId, connection);
                
                // Send handshake
                await SendHandshake(connection);
                
                _logger.LogInformation("Successfully connected to RPC server {ServerId}", serverId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to RPC server {ServerId} at {Endpoint}", serverId, endpoint);
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
                _logger.LogInformation("RPC Client OnDataReceived: Received {ByteCount} bytes from {Endpoint}, ConnectionId: {ConnectionId}", 
                    e.Data.Length, e.RemoteEndPoint, e.ConnectionId);
                
                // Deserialize the message
                var messageSerializer = _serviceProvider.GetRequiredService<Protocol.RpcMessageSerializer>();
                var message = messageSerializer.DeserializeMessage(e.Data);
                _logger.LogInformation("RPC Client OnDataReceived: Deserialized message type: {MessageType}", message.GetType().Name);

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
            _logger.LogInformation("RPC Client HandleResponse: Received response for request {RequestId}, Success: {Success}, ErrorMessage: {ErrorMessage}", 
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
            var handshake = new Protocol.RpcHandshake
            {
                ClientId = _clientOptions.ClientId,
                ProtocolVersion = 1,
                Features = new[] { "basic-rpc" }
            };

            var messageSerializer = _serviceProvider.GetRequiredService<Protocol.RpcMessageSerializer>();
            var data = messageSerializer.SerializeMessage(handshake);
            
            _logger.LogInformation("Sent handshake to server {ServerId}", connection.ServerId);
            return connection.SendAsync(data, CancellationToken.None);
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
            
            // Get the appropriate connection for this request
            var connection = _connectionManager.GetConnectionForRequest(request);
            if (connection == null)
            {
                throw new InvalidOperationException("No suitable connection found for request");
            }

            var tcs = new TaskCompletionSource<Protocol.RpcResponse>();
            if (!_pendingRequests.TryAdd(request.MessageId, tcs))
            {
                throw new InvalidOperationException($"Duplicate request ID: {request.MessageId}");
            }

            try
            {
                // Serialize and send the request
                var messageSerializer = _serviceProvider.GetRequiredService<Protocol.RpcMessageSerializer>();
                var data = messageSerializer.SerializeMessage(request);
                _logger.LogInformation("RPC Client: Sending request {MessageId} to server {ServerId}, data size: {DataSize} bytes", 
                    request.MessageId, connection.ServerId, data.Length);
                await connection.SendAsync(data, CancellationToken.None);

                // Wait for response with timeout
                using (var cts = new CancellationTokenSource(request.TimeoutMs))
                {
                    var response = await tcs.Task.WaitAsync(cts.Token);
                    return response;
                }
            }
            catch (OperationCanceledException)
            {
                _pendingRequests.TryRemove(request.MessageId, out _);
                _logger.LogError("Request {MessageId} timed out after {TimeoutMs}ms", request.MessageId, request.TimeoutMs);
                throw new TimeoutException($"RPC request {request.MessageId} timed out after {request.TimeoutMs}ms");
            }
            catch (Exception ex)
            {
                _pendingRequests.TryRemove(request.MessageId, out _);
                _logger.LogError(ex, "Error in SendRequestAsync for request {MessageId}", request.MessageId);
                throw;
            }
        }

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
