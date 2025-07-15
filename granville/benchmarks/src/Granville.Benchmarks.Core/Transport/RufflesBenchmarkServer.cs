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
    /// Ruffles-based benchmark server that echoes packets back to clients
    /// </summary>
    public class RufflesBenchmarkServer : IBenchmarkTransportServer
    {
        private readonly ILogger _logger;
        private RuffleSocket? _socket;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _pollingTask;
        private bool _disposed = false;
        
        public RufflesBenchmarkServer(ILogger logger)
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
            
            _logger.LogInformation("Ruffles benchmark server started on port {Port}", port);
            
            // Start polling task
            _pollingTask = Task.Run(() => PollEventsAsync(combinedToken), combinedToken);
            
            // Give the server a moment to start
            await Task.Delay(100, cancellationToken);
        }
        
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (_socket == null) return;
            
            _logger.LogInformation("Stopping Ruffles benchmark server...");
            
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
            
            _logger.LogInformation("Ruffles benchmark server stopped");
        }
        
        private async Task PollEventsAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Starting Ruffles polling loop");
            
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
                    _logger.LogError(ex, "Error in Ruffles polling loop");
                }
            }
            
            _logger.LogDebug("Ruffles polling loop stopped");
        }
        
        private void HandleNetworkEvent(NetworkEvent networkEvent)
        {
            try
            {
                switch (networkEvent.Type)
                {
                    case NetworkEventType.Connect:
                        _logger.LogDebug("Benchmark client connected: {EndPoint}", networkEvent.Connection.EndPoint);
                        break;
                        
                    case NetworkEventType.Disconnect:
                        _logger.LogDebug("Benchmark client disconnected: {EndPoint}", networkEvent.Connection.EndPoint);
                        break;
                        
                    case NetworkEventType.Data:
                        HandleDataEvent(networkEvent);
                        break;
                        
                    case NetworkEventType.Timeout:
                        _logger.LogDebug("Connection timeout: {EndPoint}", networkEvent.Connection.EndPoint);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling network event: {EventType}", networkEvent.Type);
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
                var data = new byte[networkEvent.Data.Count];
                networkEvent.Data.CopyTo(data);
                
                // Parse the benchmark packet
                var packet = BenchmarkProtocol.ParsePacket(data);
                
                // Create response packet (echo back the payload)
                var response = BenchmarkProtocol.CreateResponse(packet.RequestId, packet.Payload);
                
                // Send response back with the same channel type (reliable vs unreliable)
                networkEvent.Connection.Send(
                    new ArraySegment<byte>(response), 
                    networkEvent.ChannelId, 
                    false, // Not ordered
                    0 // No sequence
                );
                
                _logger.LogTrace("Echoed benchmark packet: RequestId={RequestId}, PayloadSize={PayloadSize}", 
                    packet.RequestId, packet.Payload.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing benchmark packet from {EndPoint}", networkEvent.Connection.EndPoint);
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
                    _logger.LogError(ex, "Error stopping Ruffles benchmark server during disposal");
                }
                
                _cancellationTokenSource?.Dispose();
                _disposed = true;
            }
        }
    }
}