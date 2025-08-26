using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Granville.Rpc.Configuration;
using Granville.Rpc.Telemetry;

namespace Granville.Rpc.Transport.LiteNetLib
{
    /// <summary>
    /// LiteNetLib implementation of IRpcTransport.
    /// </summary>
    public class LiteNetLibTransport : IRpcTransport, INetEventListener
    {
        static LiteNetLibTransport()
        {
            // Disable LiteNetLib's internal debug logging that outputs "[NM]" and "bad!" messages to console
            NetDebug.Logger = null;
        }
        private readonly ILogger<LiteNetLibTransport> _logger;
        private readonly RpcTransportOptions _options;
        private readonly INetworkStatisticsTracker _networkStatisticsTracker;
        private NetManager _netManager;
        private bool _isServer;
        private bool _disposed;
        private int _serverPort;
        private readonly Dictionary<int, IPEndPoint> _peerEndpoints = new();
        private readonly Dictionary<int, NetPeer> _peers = new();

        public event EventHandler<RpcDataReceivedEventArgs> DataReceived;
        public event EventHandler<RpcConnectionEventArgs> ConnectionEstablished;
        public event EventHandler<RpcConnectionEventArgs> ConnectionClosed;

        public LiteNetLibTransport(
            ILogger<LiteNetLibTransport> logger, 
            IOptions<RpcTransportOptions> options,
            INetworkStatisticsTracker networkStatisticsTracker = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _networkStatisticsTracker = networkStatisticsTracker;
        }

        public Task StartAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
        {
            if (_netManager != null)
            {
                throw new InvalidOperationException("Transport is already started.");
            }

            _isServer = true;
            _serverPort = endpoint.Port;
            _netManager = new NetManager(this)
            {
                AutoRecycle = true,
                EnableStatistics = true,
                UnconnectedMessagesEnabled = false,
                NatPunchEnabled = false
            };

            if (!_netManager.Start(endpoint.Port))
            {
                throw new InvalidOperationException($"Failed to start LiteNetLib on port {endpoint.Port}");
            }

            _logger.LogInformation("LiteNetLib transport started on {Endpoint}", endpoint);

            // Start polling thread
            _ = Task.Run(() => PollEvents(cancellationToken), cancellationToken);

            return Task.CompletedTask;
        }

        public async Task ConnectAsync(IPEndPoint remoteEndpoint, CancellationToken cancellationToken)
        {
            _logger.LogTrace("ConnectAsync called for endpoint {Endpoint}", remoteEndpoint);
            
            if (_netManager != null)
            {
                throw new InvalidOperationException("Transport is already started.");
            }

            _isServer = false;
            _logger.LogTrace("Creating NetManager for client mode");
            _netManager = new NetManager(this)
            {
                AutoRecycle = true,
                EnableStatistics = true,
                UnconnectedMessagesEnabled = false,
                NatPunchEnabled = false
            };

            _logger.LogTrace("Starting NetManager in client mode");
            if (!_netManager.Start())
            {
                _logger.LogError("Failed to start NetManager in client mode");
                throw new InvalidOperationException("Failed to start LiteNetLib in client mode");
            }
            _logger.LogTrace("NetManager started successfully in client mode");

            // Start polling thread
            _logger.LogTrace("Starting event polling thread");
            _ = Task.Run(() => PollEvents(cancellationToken), cancellationToken);

            // Connect to the server
            _logger.LogTrace("Initiating connection to {Address}:{Port}", remoteEndpoint.Address, remoteEndpoint.Port);
            var peer = _netManager.Connect(remoteEndpoint.Address.ToString(), remoteEndpoint.Port, "RpcConnection");
            if (peer == null)
            {
                _logger.LogError("NetManager.Connect returned null for {Endpoint}", remoteEndpoint);
                throw new InvalidOperationException($"Failed to connect to server at {remoteEndpoint}");
            }

            _logger.LogInformation("LiteNetLib transport connecting to {Endpoint}, initial peer state: {State}", remoteEndpoint, peer.ConnectionState);

            // Wait for connection to be established (with timeout)
            var connectionTimeout = TimeSpan.FromSeconds(5);
            var startTime = DateTime.UtcNow;
            int loopCount = 0;
            while (peer.ConnectionState != ConnectionState.Connected && DateTime.UtcNow - startTime < connectionTimeout)
            {
                loopCount++;
                if (loopCount % 100 == 0) // Log every second
                {
                    var elapsed = DateTime.UtcNow - startTime;
                    _logger.LogTrace("Connection wait loop iteration {LoopCount}, peer state: {State}, elapsed: {Elapsed}ms", 
                        loopCount, peer.ConnectionState, elapsed.TotalMilliseconds);
                }
                await Task.Delay(10, cancellationToken);
            }

            var finalElapsed = DateTime.UtcNow - startTime;
            if (peer.ConnectionState != ConnectionState.Connected)
            {
                _logger.LogError("Connection timed out after {Elapsed}ms. Final peer state: {State}, Loop iterations: {LoopCount}", 
                    finalElapsed.TotalMilliseconds, peer.ConnectionState, loopCount);
                throw new TimeoutException($"Failed to connect to server at {remoteEndpoint} within {connectionTimeout.TotalSeconds} seconds. Final state: {peer.ConnectionState}");
            }

            _logger.LogInformation("Successfully connected to {Endpoint} after {Elapsed}ms, {LoopCount} iterations", 
                remoteEndpoint, finalElapsed.TotalMilliseconds, loopCount);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            if (_netManager != null)
            {
                _netManager.Stop();
                _netManager = null;
                _logger.LogInformation("LiteNetLib transport stopped");
            }

            return Task.CompletedTask;
        }

        public Task SendAsync(IPEndPoint remoteEndpoint, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        {
            if (_netManager == null)
            {
                throw new InvalidOperationException("Transport is not started.");
            }

            // For server mode, find the peer by endpoint
            NetPeer peer = null;
            if (_isServer)
            {
                // Use the stored peers dictionary instead of relying on endpoint matching
                // Since we don't know the client port, we'll use the most recently connected peer
                // or find by partial matching (address only)
                foreach (var kvp in _peers)
                {
                    var p = kvp.Value;
                    if (p.ConnectionState == ConnectionState.Connected && p.Address.Equals(remoteEndpoint.Address))
                    {
                        peer = p;
                        break;
                    }
                }
            }
            else
            {
                // For client mode, use the first peer
                peer = _netManager.FirstPeer;
            }

            if (peer == null || peer.ConnectionState != ConnectionState.Connected)
            {
                _logger.LogWarning("No connected peer available for endpoint {Endpoint}. Connection state: {State}", 
                    remoteEndpoint, peer?.ConnectionState.ToString() ?? "null");
                throw new InvalidOperationException("No connected peer available.");
            }

            var deliveryMethod = _options.EnableReliableDelivery ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable;
            var dataArray = data.ToArray();
            peer.Send(dataArray, deliveryMethod);
            
            // Track statistics
            _networkStatisticsTracker?.RecordPacketSent(dataArray.Length);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Sends data to a specific connection ID (used by server).
        /// </summary>
        public Task SendToConnectionAsync(string connectionId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        {
            if (_netManager == null)
            {
                throw new InvalidOperationException("Transport is not started.");
            }

            if (!int.TryParse(connectionId, out var peerId))
            {
                throw new ArgumentException($"Invalid connection ID: {connectionId}");
            }

            var peer = _peers.GetValueOrDefault(peerId);
            if (peer == null || peer.ConnectionState != ConnectionState.Connected)
            {
                _logger.LogWarning("No connected peer available for connection ID {ConnectionId}. Connection state: {State}", 
                    connectionId, peer?.ConnectionState.ToString() ?? "null");
                throw new InvalidOperationException($"No connected peer available for connection ID {connectionId}.");
            }

            var deliveryMethod = _options.EnableReliableDelivery ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable;
            var dataArray = data.ToArray();
            
            _logger.LogTrace("SendToConnectionAsync: Sending {ByteCount} bytes to peer {PeerId} (connectionId: {ConnectionId}) via {DeliveryMethod}, peer state: {State}", 
                dataArray.Length, peerId, connectionId, deliveryMethod, peer.ConnectionState);
            
            peer.Send(dataArray, deliveryMethod);
            
            _logger.LogTrace("SendToConnectionAsync: Send completed to peer {PeerId}", peerId);
            
            // Track statistics
            _networkStatisticsTracker?.RecordPacketSent(dataArray.Length);

            return Task.CompletedTask;
        }

        private async void PollEvents(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _netManager != null)
            {
                _netManager.PollEvents();
                await Task.Delay(15, cancellationToken);
            }
        }

        #region INetEventListener Implementation

        public void OnPeerConnected(NetPeer peer)
        {
            var connectionId = peer.Id.ToString();
            // LiteNetLib doesn't expose the client's actual port, so we use Address with port 0
            var endpoint = new IPEndPoint(peer.Address, 0);
            _peerEndpoints[peer.Id] = endpoint;
            _peers[peer.Id] = peer;
            _logger.LogInformation("Peer connected: {PeerId} from {Address} (using peer ID for connection tracking)", connectionId, peer.Address);
            ConnectionEstablished?.Invoke(this, new RpcConnectionEventArgs(endpoint, connectionId));
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            var connectionId = peer.Id.ToString();
            var endpoint = _peerEndpoints.GetValueOrDefault(peer.Id, new IPEndPoint(peer.Address, 0));
            _peerEndpoints.Remove(peer.Id);
            _peers.Remove(peer.Id);
            _logger.LogInformation("Peer disconnected: {PeerId} from {Address}, Reason: {Reason}", 
                connectionId, peer.Address, disconnectInfo.Reason);
            ConnectionClosed?.Invoke(this, new RpcConnectionEventArgs(endpoint, connectionId));
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            _logger.LogError("Network error from {Endpoint}: {Error}", endPoint, socketError);
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            try
            {
                var data = new byte[reader.AvailableBytes];
                reader.GetBytes(data, reader.AvailableBytes);
                var endpoint = _peerEndpoints.GetValueOrDefault(peer.Id, new IPEndPoint(peer.Address, 0));
                
                // Track statistics
                _networkStatisticsTracker?.RecordPacketReceived(data.Length);
                
                var eventArgs = new RpcDataReceivedEventArgs(endpoint, data)
                {
                    ConnectionId = peer.Id.ToString()
                };
                
                DataReceived?.Invoke(this, eventArgs);
            }
            finally
            {
                reader.Recycle();
            }
        }

        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
            // Not used for RPC
            reader.Recycle();
        }

        public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
            _logger.LogTrace("Latency update for peer {PeerId}: {Latency}ms", peer.Id, latency);
            
            // Track statistics
            _networkStatisticsTracker?.RecordLatency(latency);
        }

        public void OnConnectionRequest(ConnectionRequest request)
        {
            var connectionKey = request.Data.GetString();
            _logger.LogInformation("Connection request received from {Endpoint} with key '{Key}' (server mode: {IsServer})", 
                request.RemoteEndPoint, connectionKey, _isServer);
            
            if (_isServer)
            {
                // Accept connections with proper key or empty key for backward compatibility
                if (string.IsNullOrEmpty(connectionKey) || connectionKey == "RpcConnection")
                {
                    _logger.LogTrace("Accepting connection request from {Endpoint} with key '{Key}'", request.RemoteEndPoint, connectionKey);
                    request.Accept();
                    _logger.LogTrace("Connection request accepted from {Endpoint}", request.RemoteEndPoint);
                }
                else
                {
                    _logger.LogWarning("Rejecting connection request from {Endpoint} with invalid key '{Key}'", request.RemoteEndPoint, connectionKey);
                    request.Reject();
                }
            }
            else
            {
                _logger.LogWarning("Connection request received in client mode from {Endpoint} - rejecting", request.RemoteEndPoint);
                request.Reject();
            }
        }

        #endregion

        public void Dispose()
        {
            if (!_disposed)
            {
                _netManager?.Stop();
                _disposed = true;
            }
        }
    }
}
