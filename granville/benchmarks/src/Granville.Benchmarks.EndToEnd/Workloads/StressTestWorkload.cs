using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Granville.Benchmarks.Core.Metrics;
using Granville.Benchmarks.Core.Workloads;
using Granville.Benchmarks.Core.Transport;
// NetworkEmulator moved to Core.Transport namespace
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Granville.Benchmarks.EndToEnd.Workloads
{
    /// <summary>
    /// Stress test workload that simulates extreme conditions:
    /// - Connection storms (rapid connect/disconnect cycles)
    /// - Burst traffic (sudden spikes in message volume)
    /// - Error injection (simulated network failures)
    /// - Resource exhaustion (memory/connection pressure)
    /// - Recovery testing (behavior after failures)
    /// </summary>
    public class StressTestWorkload : GameWorkloadBase
    {
        public override string Name => "Stress Test";
        public override string Description => "Tests transport behavior under extreme conditions including connection storms, burst traffic, and error injection";
        
        private readonly Random _random = new();
        private readonly ConcurrentDictionary<int, ClientState> _clients = new();
        private readonly List<IRawTransport> _transports = new();
        private readonly ConcurrentQueue<StressEvent> _stressEvents = new();
        
        // Stress test configuration
        private StressTestType _testType;
        private TimeSpan _stressInterval;
        private double _errorInjectionRate;
        private int _burstSize;
        private TimeSpan _connectionStormDuration;
        private int _maxConcurrentConnections;
        
        public StressTestWorkload(ILogger<StressTestWorkload> logger, IServiceProvider serviceProvider) 
            : base(logger, serviceProvider)
        {
        }
        
        public override async Task InitializeAsync(WorkloadConfiguration configuration)
        {
            await base.InitializeAsync(configuration);
            
            // Extract stress test settings
            _testType = GetSetting<StressTestType>("TestType", StressTestType.Mixed);
            _stressInterval = GetSetting<TimeSpan>("StressInterval", TimeSpan.FromSeconds(30));
            _errorInjectionRate = GetSetting<double>("ErrorInjectionRate", 0.05);
            _burstSize = GetSetting<int>("BurstSize", 100);
            _connectionStormDuration = GetSetting<TimeSpan>("ConnectionStormDuration", TimeSpan.FromSeconds(10));
            _maxConcurrentConnections = Math.Min(configuration.ClientCount, GetSetting<int>("MaxConcurrentConnections", 1000));
            
            _logger.LogInformation("Stress Test Configuration: Type={TestType}, Interval={Interval}s, ErrorRate={ErrorRate:P2}",
                _testType, _stressInterval.TotalSeconds, _errorInjectionRate);
            
            // Initialize clients
            for (int i = 0; i < configuration.ClientCount; i++)
            {
                var client = new ClientState
                {
                    ClientId = i,
                    IsConnected = false,
                    MessagesSent = 0,
                    ErrorsEncountered = 0,
                    LastActivity = DateTime.UtcNow,
                    ConnectionAttempts = 0,
                    CurrentBurstCount = 0
                };
                
                _clients[i] = client;
            }
            
            // Initialize transport pool
            await InitializeTransportPool(configuration);
        }
        
        protected override async Task RunClientAsync(int clientId, MetricsCollector metricsCollector, CancellationToken cancellationToken)
        {
            if (!_clients.TryGetValue(clientId, out var client))
            {
                _logger.LogError("Client {ClientId} not found", clientId);
                return;
            }
            
            var nextStressEvent = DateTime.UtcNow.Add(_stressInterval);
            var transport = await GetOrCreateTransport(clientId);
            
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var now = DateTime.UtcNow;
                    
                    // Trigger stress events
                    if (now >= nextStressEvent)
                    {
                        await TriggerStressEvent(client, transport, metricsCollector);
                        nextStressEvent = now.Add(_stressInterval);
                    }
                    
                    // Normal operation with potential stress conditions
                    await PerformClientOperation(client, transport, metricsCollector, cancellationToken);
                    
                    // Variable delay based on current stress level
                    var delay = CalculateDelay(client);
                    await Task.Delay(delay, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in stress test client {ClientId}", clientId);
                client.ErrorsEncountered++;
                metricsCollector.RecordError($"Client {clientId} error: {ex.Message}");
            }
            
            _logger.LogDebug("Stress test client {ClientId} completed. Messages: {Messages}, Errors: {Errors}",
                clientId, client.MessagesSent, client.ErrorsEncountered);
        }
        
        private async Task TriggerStressEvent(ClientState client, IRawTransport transport, MetricsCollector metricsCollector)
        {
            var eventType = DetermineStressEventType();
            var stressEvent = new StressEvent
            {
                Type = eventType,
                ClientId = client.ClientId,
                Timestamp = DateTime.UtcNow,
                Parameters = new Dictionary<string, object>()
            };
            
            _logger.LogDebug("Triggering stress event: {EventType} for client {ClientId}", eventType, client.ClientId);
            
            try
            {
                switch (eventType)
                {
                    case StressEventType.ConnectionStorm:
                        await SimulateConnectionStorm(client, transport, metricsCollector);
                        break;
                        
                    case StressEventType.BurstTraffic:
                        await SimulateBurstTraffic(client, transport, metricsCollector);
                        break;
                        
                    case StressEventType.ErrorInjection:
                        await SimulateErrorCondition(client, transport, metricsCollector);
                        break;
                        
                    case StressEventType.ResourceExhaustion:
                        await SimulateResourceExhaustion(client, transport, metricsCollector);
                        break;
                        
                    case StressEventType.NetworkPartition:
                        await SimulateNetworkPartition(client, transport, metricsCollector);
                        break;
                }
                
                stressEvent.Success = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Stress event {EventType} failed for client {ClientId}", eventType, client.ClientId);
                stressEvent.Success = false;
                stressEvent.ErrorMessage = ex.Message;
                client.ErrorsEncountered++;
            }
            
            _stressEvents.Enqueue(stressEvent);
        }
        
        private async Task SimulateConnectionStorm(ClientState client, IRawTransport transport, MetricsCollector metricsCollector)
        {
            var stopwatch = Stopwatch.StartNew();
            var connectAttempts = _random.Next(5, 20);
            
            _logger.LogDebug("Connection storm: {Attempts} rapid connect/disconnect cycles", connectAttempts);
            
            for (int i = 0; i < connectAttempts; i++)
            {
                try
                {
                    // Rapid disconnect/reconnect
                    if (client.IsConnected)
                    {
                        transport.Dispose();
                        client.IsConnected = false;
                    }
                    
                    // Short delay
                    await Task.Delay(_random.Next(10, 100));
                    
                    // Attempt reconnect
                    transport = await GetOrCreateTransport(client.ClientId);
                    client.IsConnected = true;
                    client.ConnectionAttempts++;
                    
                    // Record connection time
                    metricsCollector.RecordMessage(stopwatch.ElapsedMilliseconds, 0);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Connection attempt {Attempt} failed: {Error}", i + 1, ex.Message);
                    client.ErrorsEncountered++;
                }
            }
        }
        
        private async Task SimulateBurstTraffic(ClientState client, IRawTransport transport, MetricsCollector metricsCollector)
        {
            var burstSize = _random.Next(_burstSize / 2, _burstSize * 2);
            var messageData = CreateStressMessage(client, StressEventType.BurstTraffic);
            
            _logger.LogDebug("Burst traffic: {BurstSize} rapid messages", burstSize);
            
            var tasks = new List<Task>();
            var stopwatch = Stopwatch.StartNew();
            
            // Send burst of messages concurrently
            for (int i = 0; i < burstSize; i++)
            {
                var task = Task.Run(async () =>
                {
                    try
                    {
                        var result = await transport.SendAsync(messageData, true, CancellationToken.None);
                        if (result.Success)
                        {
                            client.MessagesSent++;
                            client.CurrentBurstCount++;
                            metricsCollector.RecordMessage(result.LatencyMicroseconds, messageData.Length);
                        }
                        else
                        {
                            client.ErrorsEncountered++;
                            metricsCollector.RecordError($"Burst send failed: {result.ErrorMessage}");
                        }
                    }
                    catch (Exception ex)
                    {
                        client.ErrorsEncountered++;
                        metricsCollector.RecordError($"Burst send exception: {ex.Message}");
                    }
                });
                
                tasks.Add(task);
            }
            
            // Wait for all burst messages to complete
            await Task.WhenAll(tasks);
            stopwatch.Stop();
            
            _logger.LogDebug("Burst completed in {ElapsedMs}ms, {Sent} sent, {Errors} errors",
                stopwatch.ElapsedMilliseconds, client.CurrentBurstCount, client.ErrorsEncountered);
            
            client.CurrentBurstCount = 0;
        }
        
        private async Task SimulateErrorCondition(ClientState client, IRawTransport transport, MetricsCollector metricsCollector)
        {
            var errorType = _random.Next(1, 5);
            
            switch (errorType)
            {
                case 1: // Invalid message
                    var invalidData = new byte[0]; // Empty message
                    await SendWithErrorHandling(transport, invalidData, "invalid_empty", client, metricsCollector);
                    break;
                    
                case 2: // Oversized message
                    var oversizedData = new byte[65536]; // Very large message
                    _random.NextBytes(oversizedData);
                    await SendWithErrorHandling(transport, oversizedData, "invalid_oversized", client, metricsCollector);
                    break;
                    
                case 3: // Rapid successive sends
                    var rapidData = CreateStressMessage(client, StressEventType.ErrorInjection);
                    for (int i = 0; i < 10; i++)
                    {
                        _ = Task.Run(async () => await SendWithErrorHandling(transport, rapidData, $"rapid_{i}", client, metricsCollector));
                    }
                    break;
                    
                case 4: // Connection during send
                    var disconnectData = CreateStressMessage(client, StressEventType.ErrorInjection);
                    var sendTask = SendWithErrorHandling(transport, disconnectData, "disconnect_during_send", client, metricsCollector);
                    await Task.Delay(50); // Brief delay then force disconnect
                    transport.Dispose();
                    client.IsConnected = false;
                    await sendTask;
                    break;
            }
        }
        
        private async Task SimulateResourceExhaustion(ClientState client, IRawTransport transport, MetricsCollector metricsCollector)
        {
            // Simulate memory pressure by creating large temporary objects
            var memoryPressure = new List<byte[]>();
            
            try
            {
                // Allocate memory in chunks
                for (int i = 0; i < 100; i++)
                {
                    var chunk = new byte[1024 * 1024]; // 1MB chunks
                    _random.NextBytes(chunk);
                    memoryPressure.Add(chunk);
                    
                    // Send a message during memory pressure
                    if (i % 10 == 0)
                    {
                        var stressData = CreateStressMessage(client, StressEventType.ResourceExhaustion);
                        await SendWithErrorHandling(transport, stressData, $"memory_pressure_{i}", client, metricsCollector);
                    }
                }
                
                // Hold memory for a brief period
                await Task.Delay(1000);
            }
            finally
            {
                // Force garbage collection
                memoryPressure.Clear();
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
        
        private async Task SimulateNetworkPartition(ClientState client, IRawTransport transport, MetricsCollector metricsCollector)
        {
            // Simulate network partition by forcing connection failures
            _logger.LogDebug("Simulating network partition for client {ClientId}", client.ClientId);
            
            var partitionData = CreateStressMessage(client, StressEventType.NetworkPartition);
            
            // Try to send during "partition" - expect failures
            for (int i = 0; i < 5; i++)
            {
                await SendWithErrorHandling(transport, partitionData, $"partition_{i}", client, metricsCollector);
                await Task.Delay(100);
            }
            
            // Simulate recovery
            await Task.Delay(1000);
            transport = await GetOrCreateTransport(client.ClientId);
            client.IsConnected = true;
            
            // Test recovery
            var recoveryData = CreateStressMessage(client, StressEventType.NetworkPartition);
            await SendWithErrorHandling(transport, recoveryData, "recovery_test", client, metricsCollector);
        }
        
        private async Task PerformClientOperation(ClientState client, IRawTransport transport, MetricsCollector metricsCollector, CancellationToken cancellationToken)
        {
            // Regular operation with stress-aware behavior
            if (!client.IsConnected && _random.NextDouble() < 0.1) // 10% chance to reconnect
            {
                try
                {
                    transport = await GetOrCreateTransport(client.ClientId);
                    client.IsConnected = true;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Reconnection failed for client {ClientId}: {Error}", client.ClientId, ex.Message);
                    client.ErrorsEncountered++;
                    return;
                }
            }
            
            if (client.IsConnected)
            {
                // Random chance to inject an error
                if (_random.NextDouble() < _errorInjectionRate)
                {
                    await SimulateErrorCondition(client, transport, metricsCollector);
                }
                else
                {
                    // Normal message
                    var messageData = CreateStressMessage(client, StressEventType.Normal);
                    await SendWithErrorHandling(transport, messageData, $"normal_{client.MessagesSent}", client, metricsCollector);
                }
            }
            
            client.LastActivity = DateTime.UtcNow;
        }
        
        private async Task SendWithErrorHandling(IRawTransport transport, byte[] data, string messageId, ClientState client, MetricsCollector metricsCollector)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                var result = await transport.SendAsync(data, true, CancellationToken.None);
                stopwatch.Stop();
                
                if (result.Success)
                {
                    client.MessagesSent++;
                    metricsCollector.RecordMessage(result.LatencyMicroseconds, data.Length);
                }
                else
                {
                    client.ErrorsEncountered++;
                    metricsCollector.RecordError($"Send failed: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                client.ErrorsEncountered++;
                metricsCollector.RecordError($"Send exception: {ex.Message}");
            }
        }
        
        private async Task<IRawTransport> GetOrCreateTransport(int clientId)
        {
            var transportConfig = new RawTransportConfig
            {
                Host = _configuration.ServerHost,
                Port = _configuration.ServerPort + (clientId % 10), // Distribute across multiple ports
                TransportType = _configuration.TransportType,
                UseReliableTransport = _configuration.UseReliableTransport,
                UseActualTransport = _configuration.UseActualTransport
            };
            
            var networkEmulator = _serviceProvider.GetService<NetworkEmulator>();
            var transport = TransportFactory.CreateTransport(transportConfig, _serviceProvider, _configuration.UseActualTransport, networkEmulator);
            
            await transport.InitializeAsync(transportConfig);
            _transports.Add(transport);
            
            return transport;
        }
        
        private async Task InitializeTransportPool(WorkloadConfiguration configuration)
        {
            // Pre-create some transports for connection storm testing
            var initialTransportCount = Math.Min(10, configuration.ClientCount / 10);
            
            for (int i = 0; i < initialTransportCount; i++)
            {
                try
                {
                    var transport = await GetOrCreateTransport(i);
                    _logger.LogDebug("Pre-created transport {Index}", i);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to pre-create transport {Index}", i);
                }
            }
        }
        
        private StressEventType DetermineStressEventType()
        {
            if (_testType != StressTestType.Mixed)
            {
                return _testType switch
                {
                    StressTestType.ConnectionStorm => StressEventType.ConnectionStorm,
                    StressTestType.BurstTraffic => StressEventType.BurstTraffic,
                    StressTestType.ErrorInjection => StressEventType.ErrorInjection,
                    StressTestType.ResourceExhaustion => StressEventType.ResourceExhaustion,
                    _ => StressEventType.BurstTraffic
                };
            }
            
            // Mixed mode - random selection
            var eventTypes = Enum.GetValues<StressEventType>().Where(e => e != StressEventType.Normal).ToArray();
            return eventTypes[_random.Next(eventTypes.Length)];
        }
        
        private TimeSpan CalculateDelay(ClientState client)
        {
            // Variable delay based on stress conditions
            var baseDelay = 100; // 100ms base
            
            // Increase delay if many errors
            if (client.ErrorsEncountered > 10)
                baseDelay *= 2;
            
            // Decrease delay during bursts
            if (client.CurrentBurstCount > 0)
                baseDelay /= 4;
            
            var jitter = _random.Next(-baseDelay / 4, baseDelay / 4);
            return TimeSpan.FromMilliseconds(Math.Max(10, baseDelay + jitter));
        }
        
        private byte[] CreateStressMessage(ClientState client, StressEventType eventType)
        {
            var size = eventType switch
            {
                StressEventType.BurstTraffic => _random.Next(32, 128),
                StressEventType.ConnectionStorm => 64,
                StressEventType.ErrorInjection => _random.Next(1, 1024),
                StressEventType.ResourceExhaustion => _random.Next(512, 2048),
                StressEventType.NetworkPartition => 128,
                _ => _configuration.MessageSize
            };
            
            var data = new byte[size];
            _random.NextBytes(data);
            
            // Add metadata
            if (data.Length >= 16)
            {
                BitConverter.GetBytes(client.ClientId).CopyTo(data, 0);
                BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).CopyTo(data, 4);
                BitConverter.GetBytes((int)eventType).CopyTo(data, 12);
            }
            
            return data;
        }
        
        private T GetSetting<T>(string key, T defaultValue)
        {
            if (_configuration.CustomSettings.TryGetValue(key, out var value) && value is T typedValue)
            {
                return typedValue;
            }
            return defaultValue;
        }
        
        public override async Task CleanupAsync()
        {
            foreach (var transport in _transports)
            {
                transport.Dispose();
            }
            _transports.Clear();
            _clients.Clear();
            
            // Log stress event summary
            var events = new List<StressEvent>();
            while (_stressEvents.TryDequeue(out var evt))
            {
                events.Add(evt);
            }
            
            if (events.Any())
            {
                var eventSummary = events.GroupBy(e => e.Type)
                    .Select(g => $"{g.Key}: {g.Count()} ({g.Count(e => e.Success)} successful)")
                    .ToList();
                
                _logger.LogInformation("Stress events summary: {EventSummary}", string.Join(", ", eventSummary));
            }
            
            await base.CleanupAsync();
        }
        
        // Data structures
        private class ClientState
        {
            public int ClientId { get; set; }
            public bool IsConnected { get; set; }
            public int MessagesSent { get; set; }
            public int ErrorsEncountered { get; set; }
            public DateTime LastActivity { get; set; }
            public int ConnectionAttempts { get; set; }
            public int CurrentBurstCount { get; set; }
        }
        
        private class StressEvent
        {
            public StressEventType Type { get; set; }
            public int ClientId { get; set; }
            public DateTime Timestamp { get; set; }
            public bool Success { get; set; }
            public string? ErrorMessage { get; set; }
            public Dictionary<string, object> Parameters { get; set; } = new();
        }
        
        private enum StressTestType
        {
            Mixed,
            ConnectionStorm,
            BurstTraffic,
            ErrorInjection,
            ResourceExhaustion
        }
        
        private enum StressEventType
        {
            Normal,
            ConnectionStorm,
            BurstTraffic,
            ErrorInjection,
            ResourceExhaustion,
            NetworkPartition
        }
    }
}