using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Granville.Benchmarks.Core.Metrics;
using Granville.Benchmarks.Core.Workloads;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Granville.Benchmarks.EndToEnd.Workloads
{
    public class MobaGameWorkload : GameWorkloadBase
    {
        public override string Name => "MOBA Game Simulation";
        public override string Description => "Simulates a MOBA game with mixed reliable/unreliable messages for abilities, movement, and game events";
        
        private readonly Random _random = new();
        
        public MobaGameWorkload(ILogger<MobaGameWorkload> logger, IServiceProvider serviceProvider) 
            : base(logger, serviceProvider)
        {
        }
        
        protected override async Task RunClientAsync(int clientId, MetricsCollector metricsCollector, CancellationToken cancellationToken)
        {
            var updateInterval = TimeSpan.FromMilliseconds(1000.0 / _configuration.MessagesPerSecond);
            var reliabilityMix = _configuration.CustomSettings.TryGetValue("reliabilityMix", out var mix) ? Convert.ToDouble(mix) : 0.7;
            
            _logger.LogDebug("MOBA Client {ClientId} starting with {ReliabilityMix:P0} reliable messages", clientId, reliabilityMix);
            
            var messageCount = 0;
            
            while (!cancellationToken.IsCancellationRequested)
            {
                var sw = Stopwatch.StartNew();
                
                try
                {
                    var isReliable = _random.NextDouble() < reliabilityMix;
                    var messageType = SelectMessageType(messageCount++);
                    var payload = GenerateMessage(clientId, messageType, isReliable);
                    
                    metricsCollector.RecordBytesSent(payload.Length);
                    metricsCollector.RecordPacketSent();
                    
                    // TODO: Actual RPC call here with reliability setting
                    await SimulateNetworkCall(isReliable, cancellationToken);
                    
                    metricsCollector.RecordSuccess();
                    
                    // Reliable messages get larger ACKs
                    var ackSize = isReliable ? 128 : 32;
                    metricsCollector.RecordBytesReceived(ackSize);
                    metricsCollector.RecordPacketReceived();
                    
                    var latencyMicros = sw.Elapsed.TotalMicroseconds;
                    metricsCollector.RecordLatency(latencyMicros);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (TimeoutException)
                {
                    metricsCollector.RecordTimeout();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "MOBA Client {ClientId} encountered error", clientId);
                    metricsCollector.RecordFailure();
                }
                
                // Wait for next update
                var elapsed = sw.Elapsed;
                if (elapsed < updateInterval)
                {
                    try
                    {
                        await Task.Delay(updateInterval - elapsed, cancellationToken);
                    }
                    catch (TaskCanceledException)
                    {
                        // Expected when cancellation is requested - break out of loop
                        break;
                    }
                }
            }
            
            _logger.LogDebug("MOBA Client {ClientId} stopped", clientId);
        }
        
        private async Task SimulateNetworkCall(bool isReliable, CancellationToken cancellationToken)
        {
            // TODO: Replace with actual RPC call
            // Reliable messages have slightly higher latency due to ACKs
            var baseLatency = isReliable ? 5 : 3;
            var latency = _random.NextDouble() * 10 + baseLatency;
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(latency), cancellationToken);
            }
            catch (TaskCanceledException)
            {
                // Re-throw cancellation exceptions so they can be handled by the caller
                throw;
            }
            
            // Unreliable messages have higher packet loss
            var lossRate = isReliable ? 0.001 : 0.02; // 0.1% vs 2%
            if (_random.NextDouble() < lossRate)
            {
                throw new TimeoutException("Simulated packet loss");
            }
        }
        
        private MessageType SelectMessageType(int messageCount)
        {
            // Simulate realistic MOBA message distribution
            return (messageCount % 10) switch
            {
                < 5 => MessageType.Movement,      // 50% movement updates
                < 7 => MessageType.Animation,     // 20% animation states
                < 8 => MessageType.Ability,       // 10% ability casts
                < 9 => MessageType.GameEvent,     // 10% game events
                _ => MessageType.Chat             // 10% chat/pings
            };
        }
        
        private byte[] GenerateMessage(int clientId, MessageType type, bool isReliable)
        {
            return type switch
            {
                MessageType.Movement => GenerateMovementMessage(clientId),
                MessageType.Animation => GenerateAnimationMessage(clientId),
                MessageType.Ability => GenerateAbilityMessage(clientId),
                MessageType.GameEvent => GenerateGameEventMessage(clientId),
                MessageType.Chat => GenerateChatMessage(clientId),
                _ => new byte[64]
            };
        }
        
        private byte[] GenerateMovementMessage(int clientId)
        {
            // Movement: clientId(4) + position(12) + destination(12) + timestamp(8) = 36 bytes
            var buffer = new byte[36];
            BitConverter.TryWriteBytes(buffer.AsSpan(0, 4), clientId);
            // Add position and destination data
            _random.NextBytes(buffer.AsSpan(4, 24));
            BitConverter.TryWriteBytes(buffer.AsSpan(28, 8), DateTime.UtcNow.Ticks);
            return buffer;
        }
        
        private byte[] GenerateAnimationMessage(int clientId)
        {
            // Animation: clientId(4) + animationId(4) + progress(4) + params(16) = 28 bytes
            var buffer = new byte[28];
            BitConverter.TryWriteBytes(buffer.AsSpan(0, 4), clientId);
            BitConverter.TryWriteBytes(buffer.AsSpan(4, 4), _random.Next(1, 20)); // animationId
            BitConverter.TryWriteBytes(buffer.AsSpan(8, 4), _random.NextSingle()); // progress
            _random.NextBytes(buffer.AsSpan(12, 16)); // params
            return buffer;
        }
        
        private byte[] GenerateAbilityMessage(int clientId)
        {
            // Ability cast: clientId(4) + abilityId(4) + target(12) + params(32) = 52 bytes
            var buffer = new byte[52];
            BitConverter.TryWriteBytes(buffer.AsSpan(0, 4), clientId);
            BitConverter.TryWriteBytes(buffer.AsSpan(4, 4), _random.Next(1, 5)); // abilityId (Q,W,E,R)
            _random.NextBytes(buffer.AsSpan(8, 44));
            return buffer;
        }
        
        private byte[] GenerateGameEventMessage(int clientId)
        {
            // Game event: eventType(4) + clientId(4) + data(120) = 128 bytes
            var buffer = new byte[128];
            BitConverter.TryWriteBytes(buffer.AsSpan(0, 4), _random.Next(1, 10)); // eventType
            BitConverter.TryWriteBytes(buffer.AsSpan(4, 4), clientId);
            _random.NextBytes(buffer.AsSpan(8, 120));
            return buffer;
        }
        
        private byte[] GenerateChatMessage(int clientId)
        {
            // Chat/ping: clientId(4) + messageType(1) + data(59) = 64 bytes
            var buffer = new byte[64];
            BitConverter.TryWriteBytes(buffer.AsSpan(0, 4), clientId);
            buffer[4] = (byte)_random.Next(1, 5); // chat type
            _random.NextBytes(buffer.AsSpan(5, 59));
            return buffer;
        }
        
        private enum MessageType
        {
            Movement,
            Animation,
            Ability,
            GameEvent,
            Chat
        }
    }
}