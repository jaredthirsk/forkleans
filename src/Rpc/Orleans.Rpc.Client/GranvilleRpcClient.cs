using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Rpc.Transport;
using LiteNetLib;
using Ruffles;

namespace Orleans.Rpc.Client
{
    /// <summary>
    /// Main implementation of the Granville RPC client with multi-level abstraction support.
    /// </summary>
    public class GranvilleRpcClient : IGranvilleRpcClient
    {
        private readonly ILogger<GranvilleRpcClient> _logger;
        private readonly RpcTransportType _transportType;
        private ITransport _transport;
        private bool _disposed;
        
        // Abstraction implementations
        private readonly GranvilleBypassImpl _bypass;
        private readonly DirectTransportAccessImpl _directAccess;
        private readonly RpcMetricsImpl _metrics;
        
        public GranvilleRpcClient(ILogger<GranvilleRpcClient> logger, RpcTransportType transportType)
        {
            _logger = logger;
            _transportType = transportType;
            _bypass = new GranvilleBypassImpl(this);
            _directAccess = new DirectTransportAccessImpl(this);
            _metrics = new RpcMetricsImpl();
        }
        
        public IGranvilleBypass Bypass => _bypass;
        public IDirectTransportAccess DirectAccess => _directAccess;
        public IRpcMetrics Metrics => _metrics;
        public bool IsConnected => _transport?.IsConnected ?? false;
        
        public async Task ConnectAsync(string host, int port)
        {
            _logger.LogInformation("Connecting to {Host}:{Port} using {Transport}", host, port, _transportType);
            
            // Create transport based on type
            _transport = CreateTransport(_transportType);
            await _transport.ConnectAsync(host, port);
            
            _logger.LogInformation("Connected successfully");
        }
        
        public async Task DisconnectAsync()
        {
            if (_transport != null)
            {
                await _transport.DisconnectAsync();
                _transport.Dispose();
                _transport = null;
            }
        }
        
        public async Task<TResult> InvokeAsync<TService, TResult>(Func<TService, Task<TResult>> method)
        {
            // Full RPC implementation with serialization, method dispatch, etc.
            // This is where the ~1ms overhead comes from
            _metrics.RecordSend();
            
            // Simplified for example - actual implementation would:
            // 1. Serialize method call
            // 2. Send via transport
            // 3. Wait for response
            // 4. Deserialize result
            
            throw new NotImplementedException("Full RPC implementation needed");
        }
        
        public async Task InvokeAsync<TService>(Func<TService, Task> method)
        {
            await InvokeAsync<TService, object>(async service =>
            {
                await method(service);
                return null;
            });
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                DisconnectAsync().GetAwaiter().GetResult();
                _disposed = true;
            }
        }
        
        private ITransport CreateTransport(RpcTransportType type)
        {
            return type switch
            {
                RpcTransportType.LiteNetLib => new LiteNetLibTransport(_logger),
                RpcTransportType.Ruffles => new RufflesTransport(_logger),
                _ => throw new NotSupportedException($"Transport type {type} not supported")
            };
        }
        
        // Bypass implementation
        private class GranvilleBypassImpl : IGranvilleBypass
        {
            private readonly GranvilleRpcClient _client;
            
            public GranvilleBypassImpl(GranvilleRpcClient client)
            {
                _client = client;
            }
            
            public async Task SendUnreliableAsync(byte[] data)
            {
                _client._metrics.RecordSend();
                await _client._transport.SendAsync(data, DeliveryMode.Unreliable);
            }
            
            public async Task SendReliableOrderedAsync(byte[] data, byte channel = 0)
            {
                _client._metrics.RecordSend();
                await _client._transport.SendAsync(data, DeliveryMode.ReliableOrdered, channel);
            }
            
            public async Task SendUnreliableSequencedAsync(byte[] data, byte channel = 0)
            {
                _client._metrics.RecordSend();
                await _client._transport.SendAsync(data, DeliveryMode.UnreliableSequenced, channel);
            }
            
            public async Task SendReliableUnorderedAsync(byte[] data)
            {
                _client._metrics.RecordSend();
                await _client._transport.SendAsync(data, DeliveryMode.ReliableUnordered);
            }
        }
        
        // Direct access implementation
        private class DirectTransportAccessImpl : IDirectTransportAccess
        {
            private readonly GranvilleRpcClient _client;
            
            public DirectTransportAccessImpl(GranvilleRpcClient client)
            {
                _client = client;
            }
            
            public string TransportTypeName => _client._transportType.ToString();
            
            public bool IsDirectAccessAvailable => 
                _client._transport is IDirectTransportProvider;
            
            public object? GetUnderlyingTransport()
            {
                if (_client._transport is IDirectTransportProvider provider)
                {
                    return _client._transportType switch
                    {
                        RpcTransportType.LiteNetLib => provider.GetLiteNetLibPeer(),
                        RpcTransportType.Ruffles => provider.GetRufflesConnection(),
                        _ => null
                    };
                }
                return null;
            }
            
            public bool TrySendDirect(byte[] data, string target, bool reliable = false)
            {
                if (_client._transport is IDirectTransportProvider provider)
                {
                    return _client._transportType switch
                    {
                        RpcTransportType.LiteNetLib => TrySendLiteNetLib(provider, data, reliable),
                        RpcTransportType.Ruffles => TrySendRuffles(provider, data, reliable),
                        _ => false
                    };
                }
                return false;
            }
            
            private bool TrySendLiteNetLib(IDirectTransportProvider provider, byte[] data, bool reliable)
            {
                var peer = provider.GetLiteNetLibPeer();
                if (peer != null)
                {
                    // We'd need to import LiteNetLib types here, but this is just an example
                    // In practice, this would use reflection or dynamic typing
                    return true; // Placeholder
                }
                return false;
            }
            
            private bool TrySendRuffles(IDirectTransportProvider provider, byte[] data, bool reliable)
            {
                var connection = provider.GetRufflesConnection();
                if (connection != null)
                {
                    // We'd need to import Ruffles types here, but this is just an example
                    // In practice, this would use reflection or dynamic typing  
                    return true; // Placeholder
                }
                return false;
            }
        }
        
        // Metrics implementation
        private class RpcMetricsImpl : IRpcMetrics
        {
            private long _messagesSent;
            private long _messagesReceived;
            private long _failedSends;
            private double _totalRtt;
            private long _rttCount;
            
            public double AverageRttMs => _rttCount > 0 ? _totalRtt / _rttCount : 0;
            public long MessagesSent => _messagesSent;
            public long MessagesReceived => _messagesReceived;
            public long FailedSends => _failedSends;
            
            public void RecordSend() => _messagesSent++;
            public void RecordReceive() => _messagesReceived++;
            public void RecordFailedSend() => _failedSends++;
            public void RecordRtt(double rttMs)
            {
                _totalRtt += rttMs;
                _rttCount++;
            }
        }
    }
    
    // Transport interfaces
    internal interface ITransport : IDisposable
    {
        bool IsConnected { get; }
        Task ConnectAsync(string host, int port);
        Task DisconnectAsync();
        Task SendAsync(byte[] data, DeliveryMode mode, byte channel = 0);
    }
    
    internal interface IDirectTransportProvider
    {
        NetPeer GetLiteNetLibPeer();
        Connection GetRufflesConnection();
    }
    
    internal enum DeliveryMode
    {
        Unreliable,
        ReliableUnordered,
        ReliableOrdered,
        UnreliableSequenced
    }
}