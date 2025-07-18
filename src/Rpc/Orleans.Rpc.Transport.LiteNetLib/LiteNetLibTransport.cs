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
            if (_netManager != null)
            {
                throw new InvalidOperationException("Transport is already started.");
            }

            _isServer = false;
            _netManager = new NetManager(this)
            {
                AutoRecycle = true,
                EnableStatistics = true,
                UnconnectedMessagesEnabled = false,
                NatPunchEnabled = false
            };

            if (!_netManager.Start())
            {
                throw new InvalidOperationException("Failed to start LiteNetLib in client mode");
            }

            // Start polling thread
            _ = Task.Run(() => PollEvents(cancellationToken), cancellationToken);

            // Connect to the server
            var peer = _netManager.Connect(remoteEndpoint.Address.ToString(), remoteEndpoint.Port, string.Empty);
            if (peer == null)
            {
                throw new InvalidOperationException($"Failed to connect to server at {remoteEndpoint}");
            }

            _logger.LogInformation("LiteNetLib transport connecting to {Endpoint}", remoteEndpoint);

            // Wait for connection to be established (with timeout)
            var connectionTimeout = TimeSpan.FromSeconds(5);
            var startTime = DateTime.UtcNow;
            while (peer.ConnectionState != ConnectionState.Connected && DateTime.UtcNow - startTime < connectionTimeout)
            {
                await Task.Delay(10, cancellationToken);
            }

            if (peer.ConnectionState != ConnectionState.Connected)
            {
                throw new TimeoutException($"Failed to connect to server at {remoteEndpoint} within {connectionTimeout.TotalSeconds} seconds");
            }

            _logger.LogInformation("Successfully connected to {Endpoint}", remoteEndpoint);
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
            peer.Send(dataArray, deliveryMethod);
            
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
            if (_isServer)
            {
                // Accept all connections for now
                // TODO: Add authentication/validation
                request.Accept();
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
