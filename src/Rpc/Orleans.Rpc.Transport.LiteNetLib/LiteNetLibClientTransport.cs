using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Granville.Rpc.Configuration;
using System.Net.Sockets;

namespace Granville.Rpc.Transport.LiteNetLib
{
    /// <summary>
    /// LiteNetLib client-specific transport implementation.
    /// </summary>
    public class LiteNetLibClientTransport : IRpcTransport, INetEventListener
    {
        private readonly ILogger<LiteNetLibClientTransport> _logger;
        private readonly RpcTransportOptions _transportOptions;
        private readonly LiteNetLibOptions _liteNetLibOptions;
        private NetManager _netManager;
        private NetPeer _serverPeer;
        private bool _disposed;
        private TaskCompletionSource<bool> _connectionTcs;
        private IPEndPoint _serverEndpoint;

        public event EventHandler<RpcDataReceivedEventArgs> DataReceived;
        public event EventHandler<RpcConnectionEventArgs> ConnectionEstablished;
        public event EventHandler<RpcConnectionEventArgs> ConnectionClosed;

        public LiteNetLibClientTransport(
            ILogger<LiteNetLibClientTransport> logger, 
            IOptions<RpcTransportOptions> transportOptions,
            IOptions<LiteNetLibOptions> liteNetLibOptions)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _transportOptions = transportOptions?.Value ?? throw new ArgumentNullException(nameof(transportOptions));
            _liteNetLibOptions = liteNetLibOptions?.Value ?? throw new ArgumentNullException(nameof(liteNetLibOptions));
        }

        public Task StartAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
        {
            // For client transport, StartAsync should not be used - use ConnectAsync instead
            throw new NotSupportedException("Use ConnectAsync for client transport");
        }

        public async Task ConnectAsync(IPEndPoint remoteEndpoint, CancellationToken cancellationToken)
        {
            if (_netManager != null)
            {
                throw new InvalidOperationException("Transport is already started.");
            }

            _connectionTcs = new TaskCompletionSource<bool>();

            _netManager = new NetManager(this)
            {
                AutoRecycle = true,
                EnableStatistics = _liteNetLibOptions.EnableStatistics,
                UnconnectedMessagesEnabled = false,
                NatPunchEnabled = _liteNetLibOptions.EnableNatPunchthrough
            };

            if (!_netManager.Start())
            {
                throw new InvalidOperationException("Failed to start LiteNetLib client");
            }

            _logger.LogInformation("LiteNetLib client transport started, connecting to {Endpoint}", remoteEndpoint);

            // Start polling thread
            var pollingTask = Task.Run(() => PollEvents(cancellationToken), cancellationToken);

            // Connect to server
            _serverEndpoint = remoteEndpoint;
            _serverPeer = _netManager.Connect(remoteEndpoint, "RpcConnection");
            
            // Wait for connection with timeout
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                cts.CancelAfter(_liteNetLibOptions.ConnectionTimeoutMs);
                
                try
                {
                    await _connectionTcs.Task.WaitAsync(cts.Token);
                    _logger.LogInformation("Successfully connected to RPC server at {Endpoint}", remoteEndpoint);
                }
                catch (OperationCanceledException)
                {
                    _netManager.Stop();
                    throw new TimeoutException($"Failed to connect to server at {remoteEndpoint} within {_liteNetLibOptions.ConnectionTimeoutMs}ms");
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            if (_netManager != null)
            {
                if (_serverPeer != null && _serverPeer.ConnectionState == ConnectionState.Connected)
                {
                    _serverPeer.Disconnect();
                }
                
                _netManager.Stop();
                _netManager = null;
                _logger.LogInformation("LiteNetLib client transport stopped");
            }

            return Task.CompletedTask;
        }

        public Task SendAsync(IPEndPoint remoteEndpoint, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        {
            if (_serverPeer == null || _serverPeer.ConnectionState != ConnectionState.Connected)
            {
                throw new InvalidOperationException("Not connected to server.");
            }

            var deliveryMethod = _transportOptions.EnableReliableDelivery ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable;
            _serverPeer.Send(data.ToArray(), deliveryMethod);

            return Task.CompletedTask;
        }

        public Task SendToConnectionAsync(string connectionId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        {
            // For client transport, we only have one connection to the server
            // so we ignore the connectionId and send to the server
            return SendAsync(_serverEndpoint, data, cancellationToken);
        }

        private async void PollEvents(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _netManager != null)
            {
                _netManager.PollEvents();
                await Task.Delay(_liteNetLibOptions.PollingIntervalMs, cancellationToken);
            }
        }

        #region INetEventListener Implementation

        public void OnPeerConnected(NetPeer peer)
        {
            var connectionId = peer.Id.ToString();
            _logger.LogInformation("Connected to server: {PeerId} at {Endpoint}", connectionId, _serverEndpoint);
            
            _serverPeer = peer;
            _connectionTcs?.TrySetResult(true);
            
            ConnectionEstablished?.Invoke(this, new RpcConnectionEventArgs(_serverEndpoint, connectionId));
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            var connectionId = peer.Id.ToString();
            _logger.LogInformation("Disconnected from server: {PeerId} at {Endpoint}, Reason: {Reason}", 
                connectionId, _serverEndpoint, disconnectInfo.Reason);
            
            if (_serverPeer == peer)
            {
                _serverPeer = null;
            }
            
            _connectionTcs?.TrySetResult(false);
            ConnectionClosed?.Invoke(this, new RpcConnectionEventArgs(_serverEndpoint, connectionId));
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            _logger.LogError("Network error from {Endpoint}: {Error}", endPoint, socketError);
            _connectionTcs?.TrySetException(new SocketException((int)socketError));
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            try
            {
                var data = new byte[reader.AvailableBytes];
                reader.GetBytes(data, reader.AvailableBytes);
                DataReceived?.Invoke(this, new RpcDataReceivedEventArgs(_serverEndpoint, data));
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
            _logger.LogTrace("Latency update for server: {Latency}ms", latency);
        }

        public void OnConnectionRequest(ConnectionRequest request)
        {
            // Client doesn't receive connection requests
            request.Reject();
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
