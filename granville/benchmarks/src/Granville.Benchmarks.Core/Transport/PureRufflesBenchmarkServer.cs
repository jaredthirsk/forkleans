using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Ruffles.Channeling;
using Ruffles.Configuration;
using Ruffles.Connections;
using Ruffles.Core;

namespace Granville.Benchmarks.Core.Transport
{
    /// <summary>
    /// Pure Ruffles benchmark server that echoes packets back to clients
    /// Uses direct Ruffles API without any Granville abstractions
    /// </summary>
    public class PureRufflesBenchmarkServer : IBenchmarkTransportServer
    {
        private readonly ILogger _logger;
        private RuffleSocket? _socket;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _pollingTask;
        private bool _disposed = false;
        
        public PureRufflesBenchmarkServer(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task StartAsync(int port, CancellationToken cancellationToken = default)
        {
            if (_socket != null)
            {
                throw new InvalidOperationException("Server is already started");
            }
            
            _cancellationTokenSource = new CancellationTokenSource();
            var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellationTokenSource.Token).Token;
            
            var config = new SocketConfig
            {
                DualListenPort = port,
                IPv4ListenAddress = IPAddress.Any,
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
            
            _logger.LogInformation("Pure Ruffles benchmark server started on port {Port}", port);
            
            // Start polling task
            _pollingTask = Task.Run(() => PollEventsAsync(combinedToken), combinedToken);
            
            // Give the server a moment to start
            await Task.Delay(100, cancellationToken);
        }
        
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (_socket == null) return;
            
            _logger.LogInformation("Stopping pure Ruffles benchmark server...");
            
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
            
            // Stop the socket
            _socket.Shutdown();
            _socket = null;
            
            _logger.LogInformation("Pure Ruffles benchmark server stopped");
        }
        
        private async Task PollEventsAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Starting pure Ruffles polling loop");
            
            while (!cancellationToken.IsCancellationRequested && _socket != null)
            {
                try
                {
                    // Poll for events
                    var networkEvent = _socket.Poll();
                    
                    if (networkEvent.Type != NetworkEventType.Nothing)
                    {
                        HandleNetworkEvent(networkEvent);
                    }
                    
                    await Task.Delay(1, cancellationToken); // 1ms polling interval for low latency
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in pure Ruffles polling loop");
                }
            }
            
            _logger.LogDebug("Pure Ruffles polling loop stopped");
        }
        
        private void HandleNetworkEvent(NetworkEvent networkEvent)
        {
            try
            {
                switch (networkEvent.Type)
                {
                    case NetworkEventType.Connect:
                        _logger.LogDebug("Pure Ruffles benchmark client connected: {EndPoint}", networkEvent.Connection.EndPoint);
                        break;
                        
                    case NetworkEventType.Disconnect:
                        _logger.LogDebug("Pure Ruffles benchmark client disconnected: {EndPoint}", networkEvent.Connection.EndPoint);
                        break;
                        
                    case NetworkEventType.Data:
                        HandleDataEvent(networkEvent);
                        break;
                        
                    case NetworkEventType.Timeout:
                        _logger.LogDebug("Pure Ruffles connection timeout: {EndPoint}", networkEvent.Connection.EndPoint);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling pure Ruffles network event: {EventType}", networkEvent.Type);
            }
            finally
            {
                networkEvent.Recycle();
            }
        }
        
        private void HandleDataEvent(NetworkEvent networkEvent)
        {
            try
            {
                // Parse simple message format: [4 bytes requestId][payload]
                if (networkEvent.Data.Count < 4)
                {
                    _logger.LogWarning("Received message too short: {Bytes} bytes", networkEvent.Data.Count);
                    return;
                }
                
                var requestIdBytes = new byte[4];
                Array.Copy(networkEvent.Data.Array!, networkEvent.Data.Offset, requestIdBytes, 0, 4);
                var requestId = BitConverter.ToInt32(requestIdBytes, 0);
                var payloadSize = networkEvent.Data.Count - 4;
                
                // Echo the entire message back (including request ID)
                var responseData = new byte[networkEvent.Data.Count];
                Array.Copy(networkEvent.Data.Array!, networkEvent.Data.Offset, responseData, 0, networkEvent.Data.Count);
                
                // Send response back with the same channel type (reliable vs unreliable)
                networkEvent.Connection.Send(
                    new ArraySegment<byte>(responseData), 
                    networkEvent.ChannelId, 
                    false, // Not ordered
                    0 // No sequence
                );
                
                _logger.LogTrace("Echoed pure Ruffles packet: RequestId={RequestId}, PayloadSize={PayloadSize}", 
                    requestId, payloadSize);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing pure Ruffles packet from {EndPoint}", networkEvent.Connection.EndPoint);
            }
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
                    _logger.LogError(ex, "Error stopping pure Ruffles benchmark server during disposal");
                }
                
                _cancellationTokenSource?.Dispose();
                _disposed = true;
            }
        }
    }
}