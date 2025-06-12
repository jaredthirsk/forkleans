using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Forkleans.Rpc.Configuration;

namespace Forkleans.Rpc.Transport.LiteNetLib
{
    /// <summary>
    /// LiteNetLib implementation of IRpcTransport.
    /// </summary>
    public class LiteNetLibTransport : IRpcTransport, INetEventListener
    {
        private readonly ILogger<LiteNetLibTransport> _logger;
        private readonly RpcTransportOptions _options;
        private NetManager _netManager;
        private bool _isServer;
        private bool _disposed;
        private int _serverPort;
        private readonly Dictionary<int, IPEndPoint> _peerEndpoints = new();

        public event EventHandler<RpcDataReceivedEventArgs> DataReceived;
        public event EventHandler<RpcConnectionEventArgs> ConnectionEstablished;
        public event EventHandler<RpcConnectionEventArgs> ConnectionClosed;

        public LiteNetLibTransport(ILogger<LiteNetLibTransport> logger, IOptions<RpcTransportOptions> options)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
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
            Task.Run(() => PollEvents(cancellationToken), cancellationToken);

            return Task.CompletedTask;
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

            var peer = _netManager.FirstPeer;
            if (peer == null || peer.ConnectionState != ConnectionState.Connected)
            {
                throw new InvalidOperationException("No connected peer available.");
            }

            var deliveryMethod = _options.EnableReliableDelivery ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable;
            peer.Send(data.ToArray(), deliveryMethod);

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
            // For server, we don't know the client's port, so we use 0 as placeholder
            var endpoint = new IPEndPoint(peer.Address, 0);
            _peerEndpoints[peer.Id] = endpoint;
            _logger.LogInformation("Peer connected: {PeerId} from {Address}", connectionId, peer.Address);
            ConnectionEstablished?.Invoke(this, new RpcConnectionEventArgs(endpoint, connectionId));
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            var connectionId = peer.Id.ToString();
            var endpoint = _peerEndpoints.GetValueOrDefault(peer.Id, new IPEndPoint(peer.Address, 0));
            _peerEndpoints.Remove(peer.Id);
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
                
                _logger.LogInformation("LiteNetLib OnNetworkReceive: Received {ByteCount} bytes from peer {PeerId} ({Endpoint}), raising DataReceived event", 
                    data.Length, peer.Id, endpoint);
                
                DataReceived?.Invoke(this, new RpcDataReceivedEventArgs(endpoint, data));
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
