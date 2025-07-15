using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib;
using LiteNetLib.Utils;
using Microsoft.Extensions.Logging;

namespace Granville.Benchmarks.Core.Transport
{
    /// <summary>
    /// Pure LiteNetLib benchmark server that echoes packets back to clients
    /// Uses direct LiteNetLib API without any Granville abstractions
    /// </summary>
    public class PureLiteNetLibBenchmarkServer : IBenchmarkTransportServer, INetEventListener
    {
        private readonly ILogger _logger;
        private NetManager? _netManager;
        private bool _disposed = false;
        
        public PureLiteNetLibBenchmarkServer(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task StartAsync(int port, CancellationToken cancellationToken = default)
        {
            if (_netManager != null)
            {
                throw new InvalidOperationException("Server is already started");
            }
            
            _netManager = new NetManager(this)
            {
                UnconnectedMessagesEnabled = false,
                UpdateTime = 1, // 1ms polling for low latency
                AutoRecycle = true,
                IPv6Enabled = false,
                BroadcastReceiveEnabled = false
            };
            
            var success = _netManager.Start(port);
            if (!success)
            {
                throw new InvalidOperationException($"Failed to start pure LiteNetLib server on port {port}");
            }
            
            _logger.LogInformation("Pure LiteNetLib benchmark server started on port {Port}", port);
            
            // Give the server a moment to start
            await Task.Delay(100, cancellationToken);
        }
        
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (_netManager == null) return Task.CompletedTask;
            
            _logger.LogInformation("Stopping pure LiteNetLib benchmark server...");
            
            // Stop the manager
            _netManager.Stop();
            _netManager = null;
            
            _logger.LogInformation("Pure LiteNetLib benchmark server stopped");
            return Task.CompletedTask;
        }
        
        // INetEventListener implementation
        public void OnPeerConnected(NetPeer peer)
        {
            _logger.LogDebug("Pure LiteNetLib benchmark client connected: {EndPoint}", peer.Address);
        }
        
        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            _logger.LogDebug("Pure LiteNetLib benchmark client disconnected: {EndPoint}", peer.Address);
        }
        
        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            try
            {
                // Parse simple message format: [4 bytes requestId][payload]
                if (reader.AvailableBytes < 4)
                {
                    _logger.LogWarning("Received message too short: {Bytes} bytes", reader.AvailableBytes);
                    return;
                }
                
                var requestId = reader.GetInt();
                var payloadSize = reader.AvailableBytes;
                
                // Echo the entire message back (including request ID)
                var responseData = new byte[4 + payloadSize];
                BitConverter.GetBytes(requestId).CopyTo(responseData, 0);
                if (payloadSize > 0)
                {
                    reader.GetBytes(responseData, 4, payloadSize);
                }
                
                // Send response back with the same delivery method
                peer.Send(responseData, deliveryMethod);
                
                _logger.LogTrace("Echoed pure LiteNetLib packet: RequestId={RequestId}, PayloadSize={PayloadSize}", 
                    requestId, payloadSize);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing pure LiteNetLib packet from {EndPoint}", peer.Address);
            }
        }
        
        public void OnNetworkError(IPEndPoint endPoint, System.Net.Sockets.SocketError socketError)
        {
            _logger.LogError("Pure LiteNetLib network error from {EndPoint}: {Error}", endPoint, socketError);
        }
        
        public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
            // Optional: could log network latency updates
        }
        
        public void OnConnectionRequest(ConnectionRequest request)
        {
            // Accept all connections for benchmarking
            request.Accept();
        }
        
        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
            // Not used in benchmark server
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    StopAsync().Wait(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error stopping pure LiteNetLib benchmark server during disposal");
                }
                
                _disposed = true;
            }
        }
    }
}