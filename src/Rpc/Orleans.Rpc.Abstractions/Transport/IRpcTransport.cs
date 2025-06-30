using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Granville.Rpc.Transport
{
    /// <summary>
    /// Abstraction for RPC transport implementations (UDP-based or otherwise).
    /// </summary>
    public interface IRpcTransport : IDisposable
    {
        /// <summary>
        /// Starts the transport listening on the specified endpoint.
        /// </summary>
        Task StartAsync(IPEndPoint endpoint, CancellationToken cancellationToken);

        /// <summary>
        /// Connects to a remote server endpoint (client mode).
        /// </summary>
        Task ConnectAsync(IPEndPoint remoteEndpoint, CancellationToken cancellationToken);

        /// <summary>
        /// Stops the transport.
        /// </summary>
        Task StopAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Sends data to a remote endpoint.
        /// </summary>
        Task SendAsync(IPEndPoint remoteEndpoint, ReadOnlyMemory<byte> data, CancellationToken cancellationToken);

        /// <summary>
        /// Sends data to a specific connection ID (used by server).
        /// </summary>
        Task SendToConnectionAsync(string connectionId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken);

        /// <summary>
        /// Event raised when data is received.
        /// </summary>
        event EventHandler<RpcDataReceivedEventArgs> DataReceived;

        /// <summary>
        /// Event raised when a connection is established.
        /// </summary>
        event EventHandler<RpcConnectionEventArgs> ConnectionEstablished;

        /// <summary>
        /// Event raised when a connection is closed.
        /// </summary>
        event EventHandler<RpcConnectionEventArgs> ConnectionClosed;
    }

    /// <summary>
    /// Event arguments for data received events.
    /// </summary>
    public class RpcDataReceivedEventArgs : EventArgs
    {
        public IPEndPoint RemoteEndPoint { get; }
        public ReadOnlyMemory<byte> Data { get; }
        public string ConnectionId { get; set; } = string.Empty;

        public RpcDataReceivedEventArgs(IPEndPoint remoteEndPoint, ReadOnlyMemory<byte> data)
        {
            RemoteEndPoint = remoteEndPoint;
            Data = data;
        }
    }

    /// <summary>
    /// Event arguments for connection events.
    /// </summary>
    public class RpcConnectionEventArgs : EventArgs
    {
        public IPEndPoint RemoteEndPoint { get; }
        public string ConnectionId { get; set; } = string.Empty;

        public RpcConnectionEventArgs(IPEndPoint remoteEndPoint)
        {
            RemoteEndPoint = remoteEndPoint;
        }

        public RpcConnectionEventArgs(IPEndPoint remoteEndPoint, string connectionId) : this(remoteEndPoint)
        {
            ConnectionId = connectionId;
        }
    }
}