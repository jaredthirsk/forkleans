using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib;
using LiteNetLib.Utils;
using Microsoft.Extensions.Logging;

namespace Granville.Benchmarks.Core.Transport
{
    /// <summary>
    /// Pure LiteNetLib transport with direct API usage - no Granville abstractions
    /// This provides true baseline performance measurements for overhead analysis
    /// </summary>
    public class PureLiteNetLibTransport : IRawTransport, INetEventListener
    {
        private readonly ILogger<PureLiteNetLibTransport> _logger;
        private NetManager? _netManager;
        private NetPeer? _connectedPeer;
        private RawTransportConfig? _config;
        private readonly TaskCompletionSource<bool> _connectionReady = new();
        private readonly ConcurrentDictionary<int, PendingRequest> _pendingRequests = new();
        private int _requestCounter = 0;
        private bool _disposed = false;
        
        public PureLiteNetLibTransport(ILogger<PureLiteNetLibTransport> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task InitializeAsync(RawTransportConfig config)
        {
            if (_netManager != null)
                throw new InvalidOperationException("Transport is already initialized");
            
            _config = config ?? throw new ArgumentNullException(nameof(config));
            
            _netManager = new NetManager(this)
            {
                UnconnectedMessagesEnabled = false,
                UpdateTime = 1, // 1ms polling for low latency
                AutoRecycle = true,
                IPv6Enabled = false
            };
            
            _logger.LogDebug("Pure LiteNetLib transport started, connecting to {Host}:{Port}", config.Host, config.Port);
            
            _netManager.Start();
            
            // Connect to the server
            var endpoint = new IPEndPoint(IPAddress.Parse(config.Host), config.Port);
            _connectedPeer = _netManager.Connect(endpoint, "benchmark");
            
            if (_connectedPeer == null)
            {
                throw new InvalidOperationException($"Failed to initiate connection to {config.Host}:{config.Port}");
            }
            
            // Wait for connection to be established
            var connectionTimeout = TimeSpan.FromMilliseconds(config.TimeoutMs);
            try
            {
                await _connectionReady.Task.WaitAsync(connectionTimeout);
                _logger.LogInformation("Pure LiteNetLib transport connected to {Host}:{Port}", config.Host, config.Port);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"Failed to connect to server at {config.Host}:{config.Port} within {connectionTimeout.TotalSeconds} seconds");
            }
        }
        
        public async Task<RawTransportResult> SendAsync(byte[] data, bool reliable, CancellationToken cancellationToken)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PureLiteNetLibTransport));
            
            if (_connectedPeer == null || _connectedPeer.ConnectionState != ConnectionState.Connected)
                throw new InvalidOperationException("Not connected to server");
            
            var stopwatch = Stopwatch.StartNew();
            var requestId = Interlocked.Increment(ref _requestCounter);
            
            try
            {
                // Create simple message format: [4 bytes requestId][payload]
                var message = new byte[4 + data.Length];
                BitConverter.GetBytes(requestId).CopyTo(message, 0);
                data.CopyTo(message, 4);
                
                // Create pending request tracker
                var pendingRequest = new PendingRequest(requestId, stopwatch);
                _pendingRequests[requestId] = pendingRequest;
                
                // Send directly using LiteNetLib API - no Granville abstractions
                var deliveryMethod = reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable;
                _connectedPeer.Send(message, deliveryMethod);
                
                _logger.LogTrace("Sent pure LiteNetLib request {RequestId}, PayloadSize={PayloadSize}, Reliable={Reliable}", 
                    requestId, data.Length, reliable);
                
                // Wait for response
                var timeout = TimeSpan.FromMilliseconds(_config?.TimeoutMs ?? 5000);
                var result = await pendingRequest.CompletionSource.Task.WaitAsync(timeout, cancellationToken);
                
                result.LatencyMicroseconds = stopwatch.Elapsed.TotalMicroseconds;
                result.BytesSent = message.Length;
                
                _logger.LogTrace("Completed pure LiteNetLib request {RequestId}, Latency={Latency}μs", 
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
        
        public Task CloseAsync()
        {
            if (_netManager == null) return Task.CompletedTask;
            
            _logger.LogDebug("Closing pure LiteNetLib transport...");
            
            // Disconnect
            if (_connectedPeer != null)
            {
                _connectedPeer.Disconnect();
                _connectedPeer = null;
            }
            
            // Stop manager
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
            
            _logger.LogInformation("Pure LiteNetLib transport closed");
            return Task.CompletedTask;
        }
        
        // INetEventListener implementation
        public void OnPeerConnected(NetPeer peer)
        {
            _connectedPeer = peer;
            _connectionReady.TrySetResult(true);
            _logger.LogDebug("Pure LiteNetLib connected to benchmark server: {EndPoint}", peer.Address);
        }
        
        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            _connectedPeer = null;
            _logger.LogDebug("Pure LiteNetLib disconnected from benchmark server: {EndPoint}, Reason: {Reason}", 
                peer.Address, disconnectInfo.Reason);
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
                
                // Find and complete the pending request
                if (_pendingRequests.TryGetValue(requestId, out var pendingRequest))
                {
                    var result = new RawTransportResult
                    {
                        Success = true,
                        LatencyMicroseconds = 0, // Will be set by caller
                        BytesSent = 0, // Will be set by caller
                        BytesReceived = payloadSize + 4 // Include request ID
                    };
                    
                    pendingRequest.CompletionSource.TrySetResult(result);
                    
                    _logger.LogTrace("Received pure LiteNetLib response {RequestId}, PayloadSize={PayloadSize}", 
                        requestId, payloadSize);
                }
                else
                {
                    _logger.LogWarning("Received response for unknown request ID: {RequestId}", requestId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing pure LiteNetLib response from {EndPoint}", peer.Address);
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
            // Client doesn't handle incoming connections
            request.Reject();
        }
        
        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
            // Not used in benchmark client
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
                    _logger.LogError(ex, "Error closing pure LiteNetLib transport during disposal");
                }
                
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