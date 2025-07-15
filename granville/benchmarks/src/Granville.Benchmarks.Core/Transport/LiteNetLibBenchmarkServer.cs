using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib;
using Microsoft.Extensions.Logging;

namespace Granville.Benchmarks.Core.Transport
{
    /// <summary>
    /// LiteNetLib-based benchmark server that echoes packets back to clients
    /// </summary>
    public class LiteNetLibBenchmarkServer : IBenchmarkTransportServer, INetEventListener
    {
        private readonly ILogger _logger;
        private NetManager? _netManager;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _pollingTask;
        private bool _disposed = false;
        
        public LiteNetLibBenchmarkServer(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task StartAsync(int port, CancellationToken cancellationToken = default)
        {
            if (_netManager != null)
            {
                throw new InvalidOperationException("Server is already started");
            }
            
            _cancellationTokenSource = new CancellationTokenSource();
            var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellationTokenSource.Token).Token;
            
            _netManager = new NetManager(this)
            {
                AutoRecycle = true,
                EnableStatistics = true,
                UnconnectedMessagesEnabled = false,
                NatPunchEnabled = false,
                DisconnectTimeout = 5000
            };
            
            if (!_netManager.Start(port))
            {
                throw new InvalidOperationException($"Failed to start LiteNetLib benchmark server on port {port}");
            }
            
            _logger.LogInformation("LiteNetLib benchmark server started on port {Port}", port);
            
            // Start polling task
            _pollingTask = Task.Run(() => PollEventsAsync(combinedToken), combinedToken);
            
            // Give the server a moment to start
            await Task.Delay(100, cancellationToken);
        }
        
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (_netManager == null) return;
            
            _logger.LogInformation("Stopping LiteNetLib benchmark server...");
            
            // Signal cancellation
            _cancellationTokenSource?.Cancel();
            
            // Wait for polling task to complete
            if (_pollingTask != null)
            {
                try
                {
                    await _pollingTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("Polling task did not complete within timeout");
                }
            }
            
            // Stop the NetManager
            _netManager.Stop();
            _netManager = null;
            
            _logger.LogInformation("LiteNetLib benchmark server stopped");
        }
        
        private async Task PollEventsAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Starting LiteNetLib polling loop");
            
            while (!cancellationToken.IsCancellationRequested && _netManager != null)
            {
                try
                {
                    _netManager.PollEvents();
                    await Task.Delay(1, cancellationToken); // 1ms polling interval for low latency
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in LiteNetLib polling loop");
                }
            }
            
            _logger.LogDebug("LiteNetLib polling loop stopped");
        }
        
        #region INetEventListener Implementation
        
        public void OnPeerConnected(NetPeer peer)
        {
            _logger.LogDebug("Benchmark client connected: {Address}", peer.Address);
        }
        
        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            _logger.LogDebug("Benchmark client disconnected: {Address}, Reason: {Reason}", 
                peer.Address, disconnectInfo.Reason);
        }
        
        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            _logger.LogError("Network error from {EndPoint}: {Error}", endPoint, socketError);
        }
        
        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            try
            {
                var data = new byte[reader.AvailableBytes];
                reader.GetBytes(data, reader.AvailableBytes);
                
                // Parse the benchmark packet
                var packet = BenchmarkProtocol.ParsePacket(data);
                
                // Create response packet (echo back the payload)
                var response = BenchmarkProtocol.CreateResponse(packet.RequestId, packet.Payload);
                
                // Send response back with the same delivery method
                peer.Send(response, deliveryMethod);
                
                _logger.LogTrace("Echoed benchmark packet: RequestId={RequestId}, PayloadSize={PayloadSize}", 
                    packet.RequestId, packet.Payload.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing benchmark packet from {Address}", peer.Address);
            }
            finally
            {
                reader.Recycle();
            }
        }
        
        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
            // Not used for benchmark server
            reader.Recycle();
        }
        
        public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
            _logger.LogTrace("Latency update for {Address}: {Latency}ms", peer.Address, latency);
        }
        
        public void OnConnectionRequest(ConnectionRequest request)
        {
            // Accept all benchmark connections
            request.Accept();
            _logger.LogTrace("Accepted benchmark connection from {Address}", request.RemoteEndPoint);
        }
        
        #endregion
        
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
                    _logger.LogError(ex, "Error stopping LiteNetLib benchmark server during disposal");
                }
                
                _cancellationTokenSource?.Dispose();
                _disposed = true;
            }
        }
    }
}