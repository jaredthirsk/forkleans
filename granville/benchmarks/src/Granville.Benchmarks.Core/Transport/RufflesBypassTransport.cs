using System;
using System.Collections.Concurrent;
using System.Diagnostics;
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
    /// Ruffles implementation of IRawTransport that bypasses full RPC stack for benchmarking
    /// Still uses Granville abstractions (IRawTransport, BenchmarkProtocol) but with actual networking
    /// </summary>
    public class RufflesBypassTransport : IRawTransport
    {
        private readonly ILogger<RufflesBypassTransport> _logger;
        private RuffleSocket? _socket;
        private Connection? _connectedPeer;
        private RawTransportConfig? _config;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _pollingTask;
        private readonly TaskCompletionSource<bool> _connectionReady = new();
        private readonly ConcurrentDictionary<int, PendingRequest> _pendingRequests = new();
        private int _requestCounter = 0;
        private bool _disposed = false;
        
        public RufflesBypassTransport(ILogger<RufflesBypassTransport> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task InitializeAsync(RawTransportConfig config)
        {
            if (_socket != null)
                throw new InvalidOperationException("Transport is already initialized");
            
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _cancellationTokenSource = new CancellationTokenSource();
            
            var socketConfig = new SocketConfig
            {
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
                HandshakeTimeout = config.TimeoutMs,
                ConnectionRequestTimeout = config.TimeoutMs,
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
            
            _socket = new RuffleSocket(socketConfig);
            
            _logger.LogDebug("Ruffles raw transport started, connecting to {Host}:{Port}", config.Host, config.Port);
            
            // Start polling task
            _pollingTask = Task.Run(() => PollEventsAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
            
            // Connect to the server
            var endPoint = new IPEndPoint(IPAddress.Parse(config.Host), config.Port);
            var connection = _socket.Connect(endPoint);
            
            if (connection == null)
            {
                throw new InvalidOperationException($"Failed to initiate connection to {config.Host}:{config.Port}");
            }
            
            // Wait for connection to be established
            var connectionTimeout = TimeSpan.FromMilliseconds(config.TimeoutMs);
            try
            {
                await _connectionReady.Task.WaitAsync(connectionTimeout);
                _logger.LogInformation("Ruffles raw transport connected to {Host}:{Port}", config.Host, config.Port);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"Failed to connect to server at {config.Host}:{config.Port} within {connectionTimeout.TotalSeconds} seconds");
            }
        }
        
        public async Task<RawTransportResult> SendAsync(byte[] data, bool reliable, CancellationToken cancellationToken)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RufflesBypassTransport));
            
            if (_connectedPeer == null || _connectedPeer.State != ConnectionState.Connected)
                throw new InvalidOperationException("Not connected to server");
            
            var stopwatch = Stopwatch.StartNew();
            var requestId = Interlocked.Increment(ref _requestCounter);
            
            try
            {
                // Create benchmark packet
                var packet = BenchmarkProtocol.CreateRequest(requestId, data);
                
                // Create pending request tracker
                var pendingRequest = new PendingRequest(requestId, stopwatch);
                _pendingRequests[requestId] = pendingRequest;
                
                // Send packet - use channel 0 for reliable, channel 2 for unreliable
                byte channelId = reliable ? (byte)0 : (byte)2;
                _connectedPeer.Send(new ArraySegment<byte>(packet), channelId, false, 0);
                
                _logger.LogTrace("Sent benchmark request {RequestId}, PayloadSize={PayloadSize}, Reliable={Reliable}", 
                    requestId, data.Length, reliable);
                
                // Wait for response
                var timeout = TimeSpan.FromMilliseconds(_config?.TimeoutMs ?? 5000);
                var result = await pendingRequest.CompletionSource.Task.WaitAsync(timeout, cancellationToken);
                
                result.LatencyMicroseconds = stopwatch.Elapsed.TotalMicroseconds;
                result.BytesSent = packet.Length;
                
                _logger.LogTrace("Completed benchmark request {RequestId}, Latency={Latency}μs", 
                    requestId, result.LatencyMicroseconds);
                
                return result;
            }
            catch (TimeoutException)
            {
                return new RawTransportResult
                {
                    Success = false,
                    ErrorMessage = "Request timeout",
                    LatencyMicroseconds = stopwatch.Elapsed.TotalMicroseconds,
                    BytesSent = data.Length,
                    BytesReceived = 0
                };
            }
            catch (OperationCanceledException)
            {
                return new RawTransportResult
                {
                    Success = false,
                    ErrorMessage = "Operation cancelled",
                    LatencyMicroseconds = stopwatch.Elapsed.TotalMicroseconds,
                    BytesSent = data.Length,
                    BytesReceived = 0
                };
            }
            catch (Exception ex)
            {
                return new RawTransportResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    LatencyMicroseconds = stopwatch.Elapsed.TotalMicroseconds,
                    BytesSent = data.Length,
                    BytesReceived = 0
                };
            }
            finally
            {
                _pendingRequests.TryRemove(requestId, out _);
            }
        }
        
        public async Task CloseAsync()
        {
            if (_socket == null) return;
            
            _logger.LogDebug("Closing Ruffles raw transport...");
            
            // Disconnect if connected
            if (_connectedPeer != null)
            {
                _connectedPeer.Disconnect(false);
                _connectedPeer = null;
            }
            
            // Cancel polling
            _cancellationTokenSource?.Cancel();
            
            // Wait for polling task
            if (_pollingTask != null)
            {
                try
                {
                    await _pollingTask.WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("Polling task did not complete within timeout");
                }
            }
            
            // Stop socket
            _socket.Shutdown();
            _socket = null;
            
            // Complete any pending requests with error
            foreach (var pending in _pendingRequests.Values)
            {
                pending.CompletionSource.TrySetResult(new RawTransportResult
                {
                    Success = false,
                    ErrorMessage = "Transport closed",
                    LatencyMicroseconds = pending.Stopwatch.Elapsed.TotalMicroseconds,
                    BytesSent = 0,
                    BytesReceived = 0
                });
            }
            _pendingRequests.Clear();
            
            _logger.LogInformation("Ruffles raw transport closed");
        }
        
        private async Task PollEventsAsync(CancellationToken cancellationToken)
        {
            _logger.LogTrace("Starting Ruffles polling loop");
            
            while (!cancellationToken.IsCancellationRequested && _socket != null)
            {
                try
                {
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
            
            _logger.LogTrace("Ruffles polling loop stopped");
        }
        
        private void HandleNetworkEvent(NetworkEvent networkEvent)
        {
            try
            {
                switch (networkEvent.Type)
                {
                    case NetworkEventType.Connect:
                        _connectedPeer = networkEvent.Connection;
                        _connectionReady.TrySetResult(true);
                        _logger.LogDebug("Connected to benchmark server: {EndPoint}", networkEvent.Connection.EndPoint);
                        break;
                        
                    case NetworkEventType.Disconnect:
                        _connectedPeer = null;
                        _logger.LogDebug("Disconnected from benchmark server: {EndPoint}", networkEvent.Connection.EndPoint);
                        break;
                        
                    case NetworkEventType.Data:
                        HandleDataEvent(networkEvent);
                        break;
                        
                    case NetworkEventType.Timeout:
                        _logger.LogDebug("Connection timeout: {EndPoint}", networkEvent.Connection.EndPoint);
                        _connectionReady.TrySetException(new TimeoutException("Connection timeout"));
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
                
                // Parse response packet
                var packet = BenchmarkProtocol.ParsePacket(data);
                
                // Find and complete the pending request
                if (_pendingRequests.TryGetValue(packet.RequestId, out var pendingRequest))
                {
                    var result = new RawTransportResult
                    {
                        Success = true,
                        LatencyMicroseconds = 0, // Will be set by caller
                        BytesSent = 0, // Will be set by caller
                        BytesReceived = data.Length
                    };
                    
                    pendingRequest.CompletionSource.TrySetResult(result);
                    
                    _logger.LogTrace("Received benchmark response {RequestId}, PayloadSize={PayloadSize}", 
                        packet.RequestId, packet.Payload.Length);
                }
                else
                {
                    _logger.LogWarning("Received response for unknown request ID: {RequestId}", packet.RequestId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing benchmark response from {EndPoint}", networkEvent.Connection.EndPoint);
            }
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    CloseAsync().Wait(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error closing Ruffles bypass transport during disposal");
                }
                
                _cancellationTokenSource?.Dispose();
                _disposed = true;
            }
        }
        
        private class PendingRequest
        {
            public int RequestId { get; }
            public Stopwatch Stopwatch { get; }
            public TaskCompletionSource<RawTransportResult> CompletionSource { get; }
            
            public PendingRequest(int requestId, Stopwatch stopwatch)
            {
                RequestId = requestId;
                Stopwatch = stopwatch;
                CompletionSource = new TaskCompletionSource<RawTransportResult>();
            }
        }
    }
}