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
using Granville.Benchmarks.Runner.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Granville.Benchmarks.EndToEnd.Workloads
{
    /// <summary>
    /// MMO-style workload that simulates massive multiplayer scenarios with:
    /// - Zone-based player distribution (multiple server zones)
    /// - Cross-zone interactions and transitions
    /// - Variable activity patterns (idle, active, combat)
    /// - Guild/party mechanics simulation
    /// - Scalability testing up to 10,000+ concurrent connections
    /// </summary>
    public class MmoGameWorkload : GameWorkloadBase
    {
        public override string Name => "MMO Game Simulation";
        public override string Description => "Simulates large-scale MMO scenarios with zone distribution, cross-zone interactions, and variable player activity";
        
        private readonly Random _random = new();
        private readonly ConcurrentDictionary<int, PlayerState> _players = new();
        private readonly ConcurrentDictionary<int, ZoneInfo> _zones = new();
        private readonly List<IRawTransport> _transports = new();
        
        // MMO-specific configuration
        private int _zoneCount;
        private int _playersPerZone;
        private int _guildSize;
        private double _crossZoneInteractionRate;
        private double _combatPlayerRatio;
        private TimeSpan _playerActivityCycle;
        
        public MmoGameWorkload(ILogger<MmoGameWorkload> logger, IServiceProvider serviceProvider) 
            : base(logger, serviceProvider)
        {
        }
        
        public override async Task InitializeAsync(WorkloadConfiguration configuration)
        {
            await base.InitializeAsync(configuration);
            
            // Extract MMO-specific settings
            _zoneCount = GetSetting<int>("ZoneCount", 4);
            _playersPerZone = configuration.ClientCount / _zoneCount;
            _guildSize = GetSetting<int>("GuildSize", 20);
            _crossZoneInteractionRate = GetSetting<double>("CrossZoneInteractionRate", 0.1);
            _combatPlayerRatio = GetSetting<double>("CombatPlayerRatio", 0.3);
            _playerActivityCycle = GetSetting<TimeSpan>("PlayerActivityCycle", TimeSpan.FromMinutes(2));
            
            _logger.LogInformation("MMO Configuration: {ZoneCount} zones, {PlayersPerZone} players/zone, {GuildSize} guild size",
                _zoneCount, _playersPerZone, _guildSize);
            
            // Initialize zones
            for (int i = 0; i < _zoneCount; i++)
            {
                var zoneInfo = new ZoneInfo
                {
                    ZoneId = i,
                    Name = $"Zone_{i}",
                    PlayerCount = 0,
                    ServerPort = configuration.ServerPort + i, // Each zone gets its own port
                    Transport = await CreateTransportForZone(i, configuration)
                };
                
                _zones[i] = zoneInfo;
                _transports.Add(zoneInfo.Transport);
                
                _logger.LogInformation("Initialized {ZoneName} on port {Port}", zoneInfo.Name, zoneInfo.ServerPort);
            }
            
            // Initialize players and assign to zones
            var guildId = 0;
            for (int playerId = 0; playerId < configuration.ClientCount; playerId++)
            {
                var assignedZone = playerId % _zoneCount;
                var isInCombat = _random.NextDouble() < _combatPlayerRatio;
                
                var player = new PlayerState
                {
                    PlayerId = playerId,
                    CurrentZone = assignedZone,
                    ActivityLevel = DetermineActivityLevel(),
                    GuildId = guildId,
                    IsInCombat = isInCombat,
                    LastActivity = DateTime.UtcNow,
                    MessagesSent = 0,
                    CrossZoneInteractions = 0
                };
                
                _players[playerId] = player;
                _zones[assignedZone].PlayerCount++;
                
                // Assign guild (every N players get same guild)
                if ((playerId + 1) % _guildSize == 0)
                    guildId++;
            }
            
            _logger.LogInformation("Initialized {PlayerCount} players across {ZoneCount} zones with {GuildCount} guilds",
                configuration.ClientCount, _zoneCount, guildId + 1);
        }
        
        protected override async Task RunClientAsync(int clientId, MetricsCollector metricsCollector, CancellationToken cancellationToken)
        {
            if (!_players.TryGetValue(clientId, out var player))
            {
                _logger.LogError("Player {ClientId} not found", clientId);
                return;
            }
            
            var stopwatch = Stopwatch.StartNew();
            var nextActivityChange = DateTime.UtcNow.Add(_playerActivityCycle);
            var nextZoneTransition = DateTime.UtcNow.Add(TimeSpan.FromMinutes(_random.Next(5, 30)));
            
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var now = DateTime.UtcNow;
                    
                    // Change activity level periodically
                    if (now >= nextActivityChange)
                    {
                        player.ActivityLevel = DetermineActivityLevel();
                        nextActivityChange = now.Add(_playerActivityCycle);
                    }
                    
                    // Zone transitions for some players
                    if (now >= nextZoneTransition && _random.NextDouble() < 0.05) // 5% chance
                    {
                        await HandleZoneTransition(player, metricsCollector);
                        nextZoneTransition = now.Add(TimeSpan.FromMinutes(_random.Next(10, 60)));
                    }
                    
                    // Send messages based on activity level
                    await SendPlayerMessages(player, metricsCollector, cancellationToken);
                    
                    // Update player stats
                    player.LastActivity = now;
                    
                    // Wait based on activity level
                    var delay = GetActivityDelay(player.ActivityLevel);
                    await Task.Delay(delay, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in client {ClientId}", clientId);
                metricsCollector.RecordError("Client error");
            }
            
            _logger.LogDebug("Client {ClientId} completed. Messages sent: {MessagesSent}, Cross-zone: {CrossZone}",
                clientId, player.MessagesSent, player.CrossZoneInteractions);
        }
        
        private async Task SendPlayerMessages(PlayerState player, MetricsCollector metricsCollector, CancellationToken cancellationToken)
        {
            var zone = _zones[player.CurrentZone];
            var messagesThisCycle = GetMessagesForActivity(player.ActivityLevel);
            
            for (int i = 0; i < messagesThisCycle && !cancellationToken.IsCancellationRequested; i++)
            {
                var messageType = DetermineMessageType(player);
                var messageData = CreateMessage(player, messageType);
                
                var stopwatch = Stopwatch.StartNew();
                
                try
                {
                    // Send to appropriate target
                    var result = messageType switch
                    {
                        MessageType.PositionUpdate => await SendToZone(zone, messageData),
                        MessageType.ChatMessage => await SendToGuild(player, messageData),
                        MessageType.CombatAction => await SendToZone(zone, messageData),
                        MessageType.CrossZoneMessage => await SendCrossZone(player, messageData),
                        _ => await SendToZone(zone, messageData)
                    };
                    
                    stopwatch.Stop();
                    
                    if (result.Success)
                    {
                        metricsCollector.RecordMessage(stopwatch.ElapsedMilliseconds, messageData.Length);
                        player.MessagesSent++;
                        
                        if (messageType == MessageType.CrossZoneMessage)
                        {
                            player.CrossZoneInteractions++;
                        }
                    }
                    else
                    {
                        metricsCollector.RecordError($"Send failed: {result.Error}");
                    }
                }
                catch (Exception ex)
                {
                    metricsCollector.RecordError($"Send exception: {ex.Message}");
                }
            }
        }
        
        private async Task HandleZoneTransition(PlayerState player, MetricsCollector metricsCollector)
        {
            var oldZone = player.CurrentZone;
            var newZone = _random.Next(_zoneCount);
            
            if (oldZone == newZone) return;
            
            _logger.LogDebug("Player {PlayerId} transitioning from Zone_{OldZone} to Zone_{NewZone}",
                player.PlayerId, oldZone, newZone);
            
            // Update zone counts
            _zones[oldZone].PlayerCount--;
            _zones[newZone].PlayerCount++;
            player.CurrentZone = newZone;
            
            // Send zone transition message
            var transitionData = CreateZoneTransitionMessage(player, oldZone, newZone);
            var result = await SendToZone(_zones[newZone], transitionData);
            
            if (result.Success)
            {
                metricsCollector.RecordMessage(result.LatencyMs, transitionData.Length);
            }
        }
        
        private async Task<RawTransportResult> SendToZone(ZoneInfo zone, byte[] data)
        {
            return await zone.Transport.SendAsync(data, $"zone_{zone.ZoneId}");
        }
        
        private async Task<RawTransportResult> SendToGuild(PlayerState player, byte[] data)
        {
            // For guild messages, use the zone transport but with guild targeting
            var zone = _zones[player.CurrentZone];
            return await zone.Transport.SendAsync(data, $"guild_{player.GuildId}");
        }
        
        private async Task<RawTransportResult> SendCrossZone(PlayerState player, byte[] data)
        {
            // Pick a random zone different from current
            var targetZone = _random.Next(_zoneCount);
            while (targetZone == player.CurrentZone && _zoneCount > 1)
            {
                targetZone = _random.Next(_zoneCount);
            }
            
            var zone = _zones[targetZone];
            return await zone.Transport.SendAsync(data, $"crosszone_{targetZone}");
        }
        
        private async Task<IRawTransport> CreateTransportForZone(int zoneId, WorkloadConfiguration configuration)
        {
            if (!configuration.UseRawTransport)
            {
                return TransportFactory.CreateSimulationTransport();
            }
            
            var transportConfig = new RawTransportConfig
            {
                Host = configuration.ServerHost,
                Port = configuration.ServerPort + zoneId,
                TransportType = configuration.TransportType,
                UseReliableTransport = configuration.UseReliableTransport,
                UseActualTransport = configuration.UseActualTransport
            };
            
            var networkEmulator = _serviceProvider.GetService<NetworkEmulator>();
            var transport = TransportFactory.CreateTransport(transportConfig, _serviceProvider, configuration.UseActualTransport, networkEmulator);
            
            await transport.ConnectAsync(transportConfig);
            return transport;
        }
        
        private PlayerActivity DetermineActivityLevel()
        {
            var roll = _random.NextDouble();
            return roll switch
            {
                < 0.2 => PlayerActivity.Idle,
                < 0.6 => PlayerActivity.Casual,
                < 0.9 => PlayerActivity.Active,
                _ => PlayerActivity.Intense
            };
        }
        
        private MessageType DetermineMessageType(PlayerState player)
        {
            if (player.IsInCombat && _random.NextDouble() < 0.4)
                return MessageType.CombatAction;
            
            if (_random.NextDouble() < _crossZoneInteractionRate)
                return MessageType.CrossZoneMessage;
            
            var roll = _random.NextDouble();
            return roll switch
            {
                < 0.6 => MessageType.PositionUpdate,
                < 0.8 => MessageType.ChatMessage,
                < 0.95 => MessageType.CombatAction,
                _ => MessageType.CrossZoneMessage
            };
        }
        
        private int GetMessagesForActivity(PlayerActivity activity)
        {
            return activity switch
            {
                PlayerActivity.Idle => _random.Next(0, 2),
                PlayerActivity.Casual => _random.Next(1, 4),
                PlayerActivity.Active => _random.Next(3, 8),
                PlayerActivity.Intense => _random.Next(6, 15),
                _ => 1
            };
        }
        
        private TimeSpan GetActivityDelay(PlayerActivity activity)
        {
            var baseDelay = activity switch
            {
                PlayerActivity.Idle => 5000,    // 5 seconds
                PlayerActivity.Casual => 2000,  // 2 seconds
                PlayerActivity.Active => 500,   // 500ms
                PlayerActivity.Intense => 100,  // 100ms
                _ => 1000
            };
            
            // Add jitter
            var jitter = _random.Next(-baseDelay / 4, baseDelay / 4);
            return TimeSpan.FromMilliseconds(Math.Max(50, baseDelay + jitter));
        }
        
        private byte[] CreateMessage(PlayerState player, MessageType type)
        {
            var size = type switch
            {
                MessageType.PositionUpdate => _random.Next(64, 128),
                MessageType.ChatMessage => _random.Next(50, 200),
                MessageType.CombatAction => _random.Next(32, 96),
                MessageType.CrossZoneMessage => _random.Next(100, 300),
                _ => _configuration.MessageSize
            };
            
            var data = new byte[size];
            _random.NextBytes(data);
            
            // Add some structure (player ID, message type, timestamp)
            var playerId = BitConverter.GetBytes(player.PlayerId);
            var timestamp = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var messageTypeId = (byte)type;
            
            Array.Copy(playerId, 0, data, 0, Math.Min(4, data.Length));
            if (data.Length > 4)
                Array.Copy(timestamp, 0, data, 4, Math.Min(8, data.Length - 4));
            if (data.Length > 12)
                data[12] = messageTypeId;
            
            return data;
        }
        
        private byte[] CreateZoneTransitionMessage(PlayerState player, int oldZone, int newZone)
        {
            var data = new byte[32];
            var playerId = BitConverter.GetBytes(player.PlayerId);
            var oldZoneBytes = BitConverter.GetBytes(oldZone);
            var newZoneBytes = BitConverter.GetBytes(newZone);
            var timestamp = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            
            Array.Copy(playerId, 0, data, 0, 4);
            Array.Copy(oldZoneBytes, 0, data, 4, 4);
            Array.Copy(newZoneBytes, 0, data, 8, 4);
            Array.Copy(timestamp, 0, data, 12, 8);
            data[20] = (byte)MessageType.ZoneTransition;
            
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
            _zones.Clear();
            _players.Clear();
            
            await base.CleanupAsync();
        }
        
        // Data structures
        private class PlayerState
        {
            public int PlayerId { get; set; }
            public int CurrentZone { get; set; }
            public PlayerActivity ActivityLevel { get; set; }
            public int GuildId { get; set; }
            public bool IsInCombat { get; set; }
            public DateTime LastActivity { get; set; }
            public int MessagesSent { get; set; }
            public int CrossZoneInteractions { get; set; }
        }
        
        private class ZoneInfo
        {
            public int ZoneId { get; set; }
            public string Name { get; set; } = string.Empty;
            public int PlayerCount { get; set; }
            public int ServerPort { get; set; }
            public IRawTransport Transport { get; set; } = null!;
        }
        
        private enum PlayerActivity
        {
            Idle,      // Minimal activity (AFK, browsing menus)
            Casual,    // Light activity (walking, chatting)
            Active,    // Regular gameplay (questing, exploring)
            Intense    // High activity (combat, raiding)
        }
        
        private enum MessageType
        {
            PositionUpdate = 1,
            ChatMessage = 2,
            CombatAction = 3,
            CrossZoneMessage = 4,
            ZoneTransition = 5
        }
    }
}