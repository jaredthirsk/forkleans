using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Granville.Rpc.Transport;

namespace Granville.Rpc
{
    /// <summary>
    /// Represents a connection to a single RPC server.
    /// </summary>
    internal class RpcConnection : IDisposable
    {
        private readonly ILogger _logger;
        private readonly IRpcTransport _transport;
        private bool _disposed;

        public string ServerId { get; }
        public IPEndPoint Endpoint { get; }
        public bool IsConnected => _transport != null;

        // Events that forward transport events with connection context
        public event EventHandler<RpcDataReceivedEventArgs> DataReceived;
        public event EventHandler<RpcConnectionEventArgs> ConnectionEstablished;
        public event EventHandler<RpcConnectionEventArgs> ConnectionClosed;

        public RpcConnection(string serverId, IPEndPoint endpoint, IRpcTransport transport, ILogger logger)
        {
            ServerId = serverId ?? throw new ArgumentNullException(nameof(serverId));
            Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Subscribe to transport events
            _transport.DataReceived += OnTransportDataReceived;
            _transport.ConnectionEstablished += OnTransportConnectionEstablished;
            _transport.ConnectionClosed += OnTransportConnectionClosed;
        }

        public Task SendAsync(byte[] data, CancellationToken cancellationToken)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RpcConnection));

            return _transport.SendAsync(Endpoint, data, cancellationToken);
        }

        private void OnTransportDataReceived(object sender, RpcDataReceivedEventArgs e)
        {
            // Add connection context to the event
            var newArgs = new RpcDataReceivedEventArgs(e.RemoteEndPoint, e.Data)
            {
                ConnectionId = ServerId
            };
            DataReceived?.Invoke(this, newArgs);
        }

        private void OnTransportConnectionEstablished(object sender, RpcConnectionEventArgs e)
        {
            var newArgs = new RpcConnectionEventArgs(e.RemoteEndPoint)
            {
                ConnectionId = ServerId
            };
            ConnectionEstablished?.Invoke(this, newArgs);
        }

        private void OnTransportConnectionClosed(object sender, RpcConnectionEventArgs e)
        {
            var newArgs = new RpcConnectionEventArgs(e.RemoteEndPoint)
            {
                ConnectionId = ServerId
            };
            ConnectionClosed?.Invoke(this, newArgs);
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            // Unsubscribe from events
            _transport.DataReceived -= OnTransportDataReceived;
            _transport.ConnectionEstablished -= OnTransportConnectionEstablished;
            _transport.ConnectionClosed -= OnTransportConnectionClosed;

            // Note: We don't stop or dispose the transport here because:
            // 1. The transport might be shared between multiple connections
            // 2. The RpcClient manages the transport lifecycle
            // 3. Disposing the transport here causes "ObjectDisposedException" when trying to send subsequent messages
            _logger.LogDebug("RpcConnection {ServerId} disposed (transport not disposed)", ServerId);
        }
    }
}