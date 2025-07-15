using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib;
using Microsoft.Extensions.Logging;

namespace Granville.Benchmarks.Core.Transport
{
    /// <summary>
    /// LiteNetLib implementation of IRawTransport that bypasses full RPC stack for benchmarking
    /// Still uses Granville abstractions (IRawTransport, BenchmarkProtocol) but with actual networking
    /// </summary>
    public class LiteNetLibBypassTransport : IRawTransport, INetEventListener
    {
        private readonly ILogger<LiteNetLibBypassTransport> _logger;
        private NetManager? _netManager;
        private NetPeer? _connectedPeer;
        private RawTransportConfig? _config;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _pollingTask;
        private readonly TaskCompletionSource<bool> _connectionReady = new();
        private readonly ConcurrentDictionary<int, PendingRequest> _pendingRequests = new();
        private int _requestCounter = 0;
        private bool _disposed = false;
        
        public LiteNetLibBypassTransport(ILogger<LiteNetLibBypassTransport> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task InitializeAsync(RawTransportConfig config)
        {
            if (_netManager != null)
                throw new InvalidOperationException("Transport is already initialized");
            
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _cancellationTokenSource = new CancellationTokenSource();
            
            _netManager = new NetManager(this)
            {
                AutoRecycle = true,
                EnableStatistics = true,
                UnconnectedMessagesEnabled = false,
                NatPunchEnabled = false,
                DisconnectTimeout = config.TimeoutMs
            };
            
            if (!_netManager.Start())
            {
                throw new InvalidOperationException("Failed to start LiteNetLib client");
            }
            
            _logger.LogDebug("LiteNetLib raw transport started, connecting to {Host}:{Port}", config.Host, config.Port);
            
            // Start polling task
            _pollingTask = Task.Run(() => PollEventsAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
            
            // Connect to the server
            var peer = _netManager.Connect(config.Host, config.Port, "benchmark");
            if (peer == null)
            {
                throw new InvalidOperationException($"Failed to initiate connection to {config.Host}:{config.Port}");
            }
            
            // Wait for connection to be established
            var connectionTimeout = TimeSpan.FromMilliseconds(config.TimeoutMs);
            try
            {
                await _connectionReady.Task.WaitAsync(connectionTimeout);
                _logger.LogInformation("LiteNetLib raw transport connected to {Host}:{Port}", config.Host, config.Port);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"Failed to connect to server at {config.Host}:{config.Port} within {connectionTimeout.TotalSeconds} seconds");
            }
        }
        
        public async Task<RawTransportResult> SendAsync(byte[] data, bool reliable, CancellationToken cancellationToken)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LiteNetLibBypassTransport));
            
            if (_connectedPeer == null || _connectedPeer.ConnectionState != ConnectionState.Connected)
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
                
                // Send packet
                var deliveryMethod = reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable;
                _connectedPeer.Send(packet, deliveryMethod);
                
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
            if (_netManager == null) return;
            
            _logger.LogDebug("Closing LiteNetLib raw transport...");
            
            // Disconnect if connected
            if (_connectedPeer != null)
            {
                _connectedPeer.Disconnect();
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
            
            // Stop NetManager
            _netManager.Stop();
            _netManager = null;
            
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
            
            _logger.LogInformation("LiteNetLib raw transport closed");
        }
        
        private async Task PollEventsAsync(CancellationToken cancellationToken)
        {
            _logger.LogTrace("Starting LiteNetLib polling loop");
            
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
            
            _logger.LogTrace("LiteNetLib polling loop stopped");
        }
        
        #region INetEventListener Implementation
        
        public void OnPeerConnected(NetPeer peer)
        {
            _connectedPeer = peer;
            _connectionReady.TrySetResult(true);
            _logger.LogDebug("Connected to benchmark server: {Address}", peer.Address);
        }
        
        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            _connectedPeer = null;
            _logger.LogDebug("Disconnected from benchmark server: {Address}, Reason: {Reason}", 
                peer.Address, disconnectInfo.Reason);
        }
        
        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            _logger.LogError("Network error from {EndPoint}: {Error}", endPoint, socketError);
            _connectionReady.TrySetException(new SocketException((int)socketError));
        }
        
        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            try
            {
                var data = new byte[reader.AvailableBytes];
                reader.GetBytes(data, reader.AvailableBytes);
                
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
                _logger.LogError(ex, "Error processing benchmark response");
            }
            finally
            {
                reader.Recycle();
            }
        }
        
        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
            reader.Recycle();
        }
        
        public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
            _logger.LogTrace("Latency update: {Latency}ms", latency);
        }
        
        public void OnConnectionRequest(ConnectionRequest request)
        {
            // Client mode - should not receive connection requests
            request.Reject();
        }
        
        #endregion
        
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
                    _logger.LogError(ex, "Error closing LiteNetLib bypass transport during disposal");
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