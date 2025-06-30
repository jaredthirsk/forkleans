using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Granville.Rpc.Configuration;
using Ruffles;
using Ruffles.Configuration;
using Ruffles.Connections;
using Ruffles.Core;
using Ruffles.Channeling;

namespace Granville.Rpc.Transport.Ruffles
{
    /// <summary>
    /// Ruffles implementation of IRpcTransport.
    /// </summary>
    public class RufflesTransport : IRpcTransport
    {
        private readonly ILogger<RufflesTransport> _logger;
        private readonly RpcTransportOptions _options;
        private RuffleSocket _socket;
        private bool _disposed;
        private readonly ConcurrentDictionary<ulong, Connection> _connections = new();
        private readonly ConcurrentDictionary<string, ulong> _connectionIdMap = new();
        private readonly ConcurrentDictionary<IPEndPoint, ulong> _endpointToConnection = new();
        private CancellationTokenSource _pollCancellation;

        public event EventHandler<RpcDataReceivedEventArgs> DataReceived;
        public event EventHandler<RpcConnectionEventArgs> ConnectionEstablished;
        public event EventHandler<RpcConnectionEventArgs> ConnectionClosed;

        public RufflesTransport(ILogger<RufflesTransport> logger, IOptions<RpcTransportOptions> options)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        }

        public Task StartAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
        {
            if (_socket != null)
            {
                throw new InvalidOperationException("Transport is already started.");
            }

            var config = new SocketConfig
            {
                DualListenPort = endpoint.Port,
                IPv4ListenAddress = endpoint.Address,
                ChallengeDifficulty = 0, // Disable challenge for simplicity
                ChannelTypes = new ChannelType[]
                {
                    ChannelType.Reliable,
                    ChannelType.ReliableSequenced,
                    ChannelType.Unreliable
                },
                UseSimulator = false,
                AllowUnconnectedMessages = false,
                EnableTimeouts = true,
                HeartbeatDelay = 5000, // 5 seconds
                HandshakeTimeout = 5000, // 5 seconds
                ConnectionRequestTimeout = 5000, // 5 seconds
                HandshakeResendDelay = 500,
                MaxHandshakeResends = 10,
                MaxFragments = 512,
                MaxBufferSize = 1024 * 1024, // 1MB
                MinimumMTU = 512,
                MaximumMTU = 4096,
                ReliabilityWindowSize = 512,
                ReliableAckFlowWindowSize = 1024,
                ReliabilityMaxResendAttempts = 30,
                ReliabilityResendRoundtripMultiplier = 1.2,
                ReliabilityMinPacketResendDelay = 100,
                ReliabilityMinAckResendDelay = 100,
                EnableChannelUpdates = true,
                LogicDelay = 0,
                ProcessingQueueSize = 1024,
                HeapMemoryPoolSize = 1024,
                MemoryWrapperPoolSize = 1024,
                EventQueueSize = 1024
            };

            _socket = new RuffleSocket(config);
            _logger.LogInformation("Ruffles transport started on {Endpoint}", endpoint);

            // Start polling thread
            _pollCancellation = new CancellationTokenSource();
            _ = Task.Run(() => PollEvents(_pollCancellation.Token), _pollCancellation.Token);

            return Task.CompletedTask;
        }

        public async Task ConnectAsync(IPEndPoint remoteEndpoint, CancellationToken cancellationToken)
        {
            if (_socket != null)
            {
                throw new InvalidOperationException("Transport is already started.");
            }

            var config = new SocketConfig
            {
                ChallengeDifficulty = 0,
                ChannelTypes = new ChannelType[]
                {
                    ChannelType.Reliable,
                    ChannelType.ReliableSequenced,
                    ChannelType.Unreliable
                },
                UseSimulator = false,
                AllowUnconnectedMessages = false,
                EnableTimeouts = true,
                HeartbeatDelay = 5000,
                HandshakeTimeout = 5000,
                ConnectionRequestTimeout = 5000,
                HandshakeResendDelay = 500,
                MaxHandshakeResends = 10,
                MaxFragments = 512,
                MaxBufferSize = 1024 * 1024,
                MinimumMTU = 512,
                MaximumMTU = 4096,
                ReliabilityWindowSize = 512,
                ReliableAckFlowWindowSize = 1024,
                ReliabilityMaxResendAttempts = 30,
                ReliabilityResendRoundtripMultiplier = 1.2,
                ReliabilityMinPacketResendDelay = 100,
                ReliabilityMinAckResendDelay = 100,
                EnableChannelUpdates = true,
                LogicDelay = 0,
                ProcessingQueueSize = 1024,
                HeapMemoryPoolSize = 1024,
                MemoryWrapperPoolSize = 1024,
                EventQueueSize = 1024
            };

            _socket = new RuffleSocket(config);
            
            // Start polling thread
            _pollCancellation = new CancellationTokenSource();
            _ = Task.Run(() => PollEvents(_pollCancellation.Token), _pollCancellation.Token);

            // Connect to the server
            var connection = _socket.Connect(remoteEndpoint);
            if (connection == null)
            {
                throw new InvalidOperationException($"Failed to connect to server at {remoteEndpoint}");
            }

            _logger.LogInformation("Ruffles transport connecting to {Endpoint}", remoteEndpoint);

            // Wait for connection to be established
            var connectionTimeout = TimeSpan.FromSeconds(5);
            var startTime = DateTime.UtcNow;
            while (connection.State != ConnectionState.Connected && DateTime.UtcNow - startTime < connectionTimeout)
            {
                await Task.Delay(10, cancellationToken);
            }

            if (connection.State != ConnectionState.Connected)
            {
                throw new TimeoutException($"Failed to connect to server at {remoteEndpoint} within {connectionTimeout.TotalSeconds} seconds");
            }

            // Store the connection
            _connections[connection.Id] = connection;
            var connectionId = connection.Id.ToString();
            _connectionIdMap[connectionId] = connection.Id;
            _endpointToConnection[remoteEndpoint] = connection.Id;

            _logger.LogInformation("Successfully connected to {Endpoint}", remoteEndpoint);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            if (_socket != null)
            {
                _pollCancellation?.Cancel();
                
                // Disconnect all connections
                foreach (var connection in _connections.Values)
                {
                    connection.Disconnect(true);
                }
                
                _socket.Shutdown();
                _socket = null;
                _logger.LogInformation("Ruffles transport stopped");
            }

            return Task.CompletedTask;
        }

        public Task SendAsync(IPEndPoint remoteEndpoint, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        {
            if (_socket == null)
            {
                throw new InvalidOperationException("Transport is not started.");
            }

            Connection connection = null;
            
            // Find connection by endpoint
            if (_endpointToConnection.TryGetValue(remoteEndpoint, out var connectionId))
            {
                _connections.TryGetValue(connectionId, out connection);
            }
            
            if (connection == null || connection.State != ConnectionState.Connected)
            {
                throw new InvalidOperationException($"No active connection to {remoteEndpoint}");
            }

            // Send data using reliable channel
            var dataArray = data.ToArray();
            connection.Send(new ArraySegment<byte>(dataArray), 0, false, 0); // Channel 0 = Reliable, notificationKey = 0

            return Task.CompletedTask;
        }

        public Task SendToConnectionAsync(string connectionId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        {
            if (_socket == null)
            {
                throw new InvalidOperationException("Transport is not started.");
            }

            if (!_connectionIdMap.TryGetValue(connectionId, out var connId))
            {
                throw new ArgumentException($"Unknown connection ID: {connectionId}");
            }

            if (!_connections.TryGetValue(connId, out var connection))
            {
                throw new InvalidOperationException($"Connection {connectionId} not found");
            }

            if (connection.State != ConnectionState.Connected)
            {
                throw new InvalidOperationException($"Connection {connectionId} is not connected");
            }

            // Send data using reliable channel
            var dataArray = data.ToArray();
            connection.Send(new ArraySegment<byte>(dataArray), 0, false, 0); // Channel 0 = Reliable, notificationKey = 0

            return Task.CompletedTask;
        }

        private async Task PollEvents(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _socket != null)
            {
                try
                {
                    var @event = _socket.Poll();
                    if (@event.Type != NetworkEventType.Nothing)
                    {
                        switch (@event.Type)
                        {
                            case NetworkEventType.Connect:
                                HandleConnect(@event);
                                break;
                                
                            case NetworkEventType.Disconnect:
                                HandleDisconnect(@event);
                                break;
                                
                            case NetworkEventType.Data:
                                HandleData(@event);
                                break;
                                
                            case NetworkEventType.Nothing:
                                break;
                        }
                        
                        @event.Recycle();
                    }
                    
                    await Task.Delay(1, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Ruffles poll loop");
                }
            }
        }

        private void HandleConnect(NetworkEvent @event)
        {
            var connection = @event.Connection;
            _connections[connection.Id] = connection;
            
            var connectionId = connection.Id.ToString();
            _connectionIdMap[connectionId] = connection.Id;
            
            if (connection.EndPoint != null)
            {
                _endpointToConnection[connection.EndPoint] = connection.Id;
            }
            
            _logger.LogInformation("Connection established: {ConnectionId} from {Endpoint}", 
                connectionId, connection.EndPoint);
            
            ConnectionEstablished?.Invoke(this, new RpcConnectionEventArgs(connection.EndPoint, connectionId));
        }

        private void HandleDisconnect(NetworkEvent @event)
        {
            var connection = @event.Connection;
            var connectionId = connection.Id.ToString();
            
            _connections.TryRemove(connection.Id, out _);
            _connectionIdMap.TryRemove(connectionId, out _);
            
            if (connection.EndPoint != null)
            {
                _endpointToConnection.TryRemove(connection.EndPoint, out _);
            }
            
            _logger.LogInformation("Connection closed: {ConnectionId} from {Endpoint}", 
                connectionId, connection.EndPoint);
            
            ConnectionClosed?.Invoke(this, new RpcConnectionEventArgs(connection.EndPoint, connectionId));
        }

        private void HandleData(NetworkEvent @event)
        {
            var data = @event.Data;
            var connection = @event.Connection;
            var connectionId = connection.Id.ToString();
            
            // Copy data to byte array
            var buffer = new byte[data.Count];
            Buffer.BlockCopy(data.Array, data.Offset, buffer, 0, data.Count);
            
            _logger.LogDebug("Received {ByteCount} bytes from connection {ConnectionId}", 
                buffer.Length, connectionId);
            
            // Create a proper endpoint - Ruffles might not expose the actual client port
            var endpoint = connection.EndPoint ?? new IPEndPoint(IPAddress.Any, 0);
            
            var eventArgs = new RpcDataReceivedEventArgs(endpoint, buffer)
            {
                ConnectionId = connectionId
            };
            DataReceived?.Invoke(this, eventArgs);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _pollCancellation?.Cancel();
                _pollCancellation?.Dispose();
                
                if (_socket != null)
                {
                    // Disconnect all connections
                    foreach (var connection in _connections.Values)
                    {
                        connection.Disconnect(true);
                    }
                    
                    _socket.Shutdown();
                    _socket = null;
                }
                
                _disposed = true;
            }
        }
    }
}