using System;
using System.Threading.Tasks;
using Orleans.Rpc;
using Orleans.Rpc.Transport;
using LiteNetLib;

namespace Examples
{
    /// <summary>
    /// Example showing how to use different abstraction levels for optimal performance.
    /// </summary>
    public class HotPathUsageExample
    {
        private IGranvilleRpcClient _client;
        private IDirectTransportAccess _directAccess;
        private IGranvilleBypass _bypass;
        
        // Pre-allocated buffers for hot paths
        private readonly byte[] _positionBuffer = new byte[32];
        private readonly byte[] _stateBuffer = new byte[256];
        
        public async Task InitializeAsync()
        {
            // Create client with desired transport
            _client = new GranvilleRpcClient(logger, RpcTransportType.LiteNetLib);
            
            // Connect to server
            await _client.ConnectAsync("game-server.example.com", 12345);
            
            // Get references for hot paths
            _directAccess = _client.DirectAccess;
            _bypass = _client.Bypass;
        }
        
        /// <summary>
        /// Ultra-high frequency updates (60Hz+) - Use direct transport access (0ms overhead)
        /// </summary>
        public void SendPositionUpdate(Vector3 position, Quaternion rotation)
        {
            // Serialize to pre-allocated buffer
            int offset = 0;
            WriteVector3(_positionBuffer, ref offset, position);
            WriteQuaternion(_positionBuffer, ref offset, rotation);
            
            // Use direct transport for zero overhead
            if (_directAccess.TrySendLiteNetLib(_positionBuffer, DeliveryMethod.Unreliable))
            {
                // Success - sent with 0ms Granville overhead
            }
            else
            {
                // Fallback to bypass API if direct access unavailable
                _bypass.SendUnreliableAsync(_positionBuffer).GetAwaiter().GetResult();
            }
        }
        
        /// <summary>
        /// Medium frequency updates (10-30Hz) - Use bypass API (~0.3ms overhead)
        /// </summary>
        public async Task SendGameStateUpdateAsync(GameState state)
        {
            // Serialize game state
            var stateData = SerializeGameState(state, _stateBuffer);
            
            // Use bypass API for minimal overhead with async support
            await _bypass.SendReliableOrderedAsync(stateData, channel: GameChannels.StateUpdates);
        }
        
        /// <summary>
        /// Low frequency or complex operations (<10Hz) - Use full RPC (~1ms overhead)
        /// </summary>
        public async Task ProcessGameEventAsync(GameEvent gameEvent)
        {
            // Use full RPC for method calls with all features
            var result = await _client.InvokeAsync<IGameService, GameEventResult>(
                service => service.ProcessGameEventAsync(gameEvent));
            
            // Handle result with full type safety
            if (result.Success)
            {
                Console.WriteLine($"Event processed: {result.EventId}");
            }
        }
        
        /// <summary>
        /// Example of adaptive performance based on metrics
        /// </summary>
        public async Task SendAdaptiveUpdateAsync(byte[] data, bool isCritical)
        {
            var metrics = _client.Metrics;
            
            // Switch to more reliable delivery if seeing failures
            if (metrics.FailedSends > metrics.MessagesSent * 0.05) // >5% failure rate
            {
                await _bypass.SendReliableOrderedAsync(data);
            }
            else if (isCritical)
            {
                await _bypass.SendReliableUnorderedAsync(data);
            }
            else
            {
                await _bypass.SendUnreliableAsync(data);
            }
        }
        
        /// <summary>
        /// Example of transport-specific optimizations
        /// </summary>
        public void OptimizeForTransport()
        {
            switch (_directAccess.TransportType)
            {
                case RpcTransportType.LiteNetLib:
                    // LiteNetLib-specific optimizations
                    var peer = _directAccess.GetLiteNetLibPeer();
                    if (peer != null)
                    {
                        // Access LiteNetLib-specific features
                        peer.GetStatistics(out var stats);
                        Console.WriteLine($"RTT: {stats.RoundTripTime}ms");
                    }
                    break;
                    
                case RpcTransportType.Ruffles:
                    // Ruffles-specific optimizations
                    var connection = _directAccess.GetRufflesConnection();
                    if (connection != null)
                    {
                        // Access Ruffles-specific features
                        Console.WriteLine($"Ruffles MTU: {connection.MTU}");
                    }
                    break;
            }
        }
        
        // Helper methods
        private void WriteVector3(byte[] buffer, ref int offset, Vector3 v)
        {
            BitConverter.GetBytes(v.X).CopyTo(buffer, offset); offset += 4;
            BitConverter.GetBytes(v.Y).CopyTo(buffer, offset); offset += 4;
            BitConverter.GetBytes(v.Z).CopyTo(buffer, offset); offset += 4;
        }
        
        private void WriteQuaternion(byte[] buffer, ref int offset, Quaternion q)
        {
            BitConverter.GetBytes(q.X).CopyTo(buffer, offset); offset += 4;
            BitConverter.GetBytes(q.Y).CopyTo(buffer, offset); offset += 4;
            BitConverter.GetBytes(q.Z).CopyTo(buffer, offset); offset += 4;
            BitConverter.GetBytes(q.W).CopyTo(buffer, offset); offset += 4;
        }
        
        private byte[] SerializeGameState(GameState state, byte[] buffer)
        {
            // Simplified serialization
            return buffer;
        }
    }
    
    // Game-specific channel definitions
    public static class GameChannels
    {
        public const byte PositionUpdates = 0;      // Unreliable, high frequency
        public const byte StateUpdates = 1;         // Reliable ordered, medium frequency
        public const byte GameEvents = 2;           // Reliable ordered, low frequency
        public const byte Chat = 3;                 // Reliable unordered
        public const byte VoiceData = 4;           // Unreliable sequenced
    }
    
    // Example types
    public struct Vector3 { public float X, Y, Z; }
    public struct Quaternion { public float X, Y, Z, W; }
    public class GameState { }
    public class GameEvent { }
    public class GameEventResult { public bool Success; public string EventId; }
    public interface IGameService 
    { 
        Task<GameEventResult> ProcessGameEventAsync(GameEvent evt);
    }
}