using Orleans;
using Orleans.Runtime;
using Orleans.Timers;
using Shooter.Shared.GrainInterfaces;
using Shooter.Shared.Models;
using Shooter.Shared.RpcInterfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Shooter.Silo.Configuration;
using Microsoft.AspNetCore.SignalR;
using Shooter.Silo.Hubs;

namespace Shooter.Silo.Grains;

public class WorldManagerGrain : Orleans.Grain, IWorldManagerGrain
{
    private readonly Orleans.Runtime.IPersistentState<WorldManagerState> _state;
    private readonly ILogger<WorldManagerGrain> _logger;
    private readonly Dictionary<GridSquare, ActionServerInfo> _gridToServer = new();
    private readonly Dictionary<string, ActionServerInfo> _serverIdToInfo = new();
    private readonly IOptions<GameSettings> _gameSettings;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private Orleans.Runtime.IGrainTimer? _timerHandle;
    private readonly Dictionary<GridSquare, ZoneStats> _zoneStats = new();
    private DateTime _lastStatsUpdate = DateTime.UtcNow;
    private IHubContext<GameHub, IGameHubClient>? _hubContext;
    private readonly Dictionary<string, ActionServerStatus> _serverStatuses = new();

    public WorldManagerGrain(
        [Orleans.Runtime.PersistentState("worldManager", "worldStore")] Orleans.Runtime.IPersistentState<WorldManagerState> state,
        ILogger<WorldManagerGrain> logger,
        IOptions<GameSettings> gameSettings,
        IHostApplicationLifetime applicationLifetime)
    {
        _state = state;
        _logger = logger;
        _gameSettings = gameSettings;
        _applicationLifetime = applicationLifetime;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // Get SignalR hub context
        _hubContext = this.ServiceProvider.GetService<IHubContext<GameHub, IGameHubClient>>();
        if (_hubContext == null)
        {
            _logger.LogWarning("SignalR hub context not available, chat messages won't be broadcast to web clients");
        }
        
        // Restore state
        foreach (var server in _state.State.ActionServers)
        {
            _gridToServer[server.AssignedSquare] = server;
            _serverIdToInfo[server.ServerId] = server;
        }
        
        _logger.LogInformation("WorldManagerGrain activated: Restored {ServerCount} ActionServers from persistent state", _state.State.ActionServers.Count);

        // Initialize activation time if not set
        if (!_state.State.FirstActivationTime.HasValue)
        {
            _state.State.FirstActivationTime = DateTime.UtcNow;
            await _state.WriteStateAsync();
            _logger.LogInformation("WorldManagerGrain activated for the first time at {Time}", _state.State.FirstActivationTime);
        }

        // Set up timer if QuitAfterNMinutes is configured
        var quitAfterNMinutes = _gameSettings.Value.QuitAfterNMinutes;
        if (quitAfterNMinutes > 0)
        {
            var elapsedMinutes = (DateTime.UtcNow - _state.State.FirstActivationTime.Value).TotalMinutes;
            var remainingMinutes = quitAfterNMinutes - elapsedMinutes;
            
            if (remainingMinutes > 0)
            {
                _logger.LogInformation("Setting up timer to quit after {RemainingMinutes:F1} minutes", remainingMinutes);
                _timerHandle = this.RegisterGrainTimer(
                    () => CheckTimeLimit(null),
                    TimeSpan.FromMinutes(Math.Max(0.1, remainingMinutes)), // At least 6 seconds
                    TimeSpan.FromMinutes(1)); // Check every minute
            }
            else
            {
                // Time already expired
                _logger.LogWarning("Time limit already expired. Initiating shutdown...");
                await InitiateShutdown("time limit expired");
            }
        }
        
        await base.OnActivateAsync(cancellationToken);
    }

    public async Task<ActionServerInfo> RegisterActionServer(string serverId, string ipAddress, int udpPort, string httpEndpoint, int rpcPort = 0, string? webUrl = null, bool hasPhaserView = false)
    {
        // Check if this server is already registered
        if (_serverIdToInfo.ContainsKey(serverId))
        {
            _logger.LogWarning("Server {ServerId} is already registered, returning existing assignment", serverId);
            return _serverIdToInfo[serverId];
        }
        
        // Create a square grid pattern that grows as servers are added
        // 1 server: 1x1
        // 2-4 servers: 2x2
        // 5-9 servers: 3x3
        // 10-16 servers: 4x4, etc.
        
        // Find the first available zone in the grid
        var totalServers = _gridToServer.Count + 1; // Use actual count including this new server
        var gridSize = (int)Math.Ceiling(Math.Sqrt(totalServers));
        
        GridSquare? assignedSquare = null;
        
        // Search for first unoccupied zone in row-by-row order
        for (int y = 0; y < gridSize; y++)
        {
            for (int x = 0; x < gridSize; x++)
            {
                var candidate = new GridSquare(x, y);
                if (!_gridToServer.ContainsKey(candidate))
                {
                    assignedSquare = candidate;
                    break;
                }
            }
            if (assignedSquare != null) break;
        }
        
        // If somehow all zones are taken (shouldn't happen), expand the grid
        if (assignedSquare == null)
        {
            assignedSquare = new GridSquare(0, gridSize); // Start a new row
        }
        
        _logger.LogInformation("Assigning server {ServerId} to zone ({X},{Y}) in {GridSize}x{GridSize} grid (found {ExistingCount} existing servers)", 
            serverId, assignedSquare.X, assignedSquare.Y, gridSize, gridSize, _gridToServer.Count);

        var serverInfo = new ActionServerInfo(serverId, ipAddress, udpPort, httpEndpoint, assignedSquare, DateTime.UtcNow, rpcPort, webUrl, hasPhaserView, DateTime.UtcNow);
        
        // Add the server to our mappings
        _gridToServer[assignedSquare] = serverInfo;
        _serverIdToInfo[serverId] = serverInfo;
        
        _state.State.ActionServers.Add(serverInfo);
        await _state.WriteStateAsync();

        return serverInfo;
    }

    public async Task UnregisterActionServer(string serverId)
    {
        if (_serverIdToInfo.TryGetValue(serverId, out var serverInfo))
        {
            _gridToServer.Remove(serverInfo.AssignedSquare);
            _serverIdToInfo.Remove(serverId);
            _state.State.ActionServers.RemoveAll(s => s.ServerId == serverId);
            await _state.WriteStateAsync();
        }
    }

    public Task<ActionServerInfo?> GetActionServerForPosition(Vector2 position)
    {
        var gridSquare = GridSquare.FromPosition(position);
        return Task.FromResult(_gridToServer.TryGetValue(gridSquare, out var server) ? server : null);
    }

    public Task<List<ActionServerInfo>> GetAllActionServers()
    {
        var servers = _serverIdToInfo.Values.ToList();
        _logger.LogInformation("GetAllActionServers called, returning {Count} servers", servers.Count);
        return Task.FromResult(servers);
    }

    public async Task<PlayerInfo> RegisterPlayer(string playerId, string name)
    {
        var playerGrain = GrainFactory.GetGrain<IPlayerGrain>(playerId);
        
        // Check if player already exists and has health info
        var existingInfo = await playerGrain.GetInfo();
        
        if (existingInfo != null && existingInfo.Health > 0 && existingInfo.Health < 1000f)
        {
            // Player exists with damaged health - preserve it
            _logger.LogInformation("Preserving existing player {PlayerId} with health {Health} and team {Team}", 
                playerId, existingInfo.Health, existingInfo.Team);
            var playerInfo = new PlayerInfo(playerId, name, existingInfo.Position, existingInfo.Velocity, existingInfo.Health, existingInfo.Team);
            _state.State.Players[playerId] = playerInfo;
            await _state.WriteStateAsync();
            return playerInfo;
        }
        else
        {
            // New player or respawning - initialize with full health and assign team
            var startPosition = await GetPlayerStartPosition(playerId);
            
            // Determine team assignment
            int team;
            bool isBot = name.Contains("Test") || name.Contains("Bot"); // Simple bot detection
            
            if (isBot)
            {
                // Bots alternate between teams
                // Use hash of bot name to ensure consistent team assignment
                var botNumber = playerId.GetHashCode() & 0x7FFFFFFF; // Ensure positive
                team = (botNumber % 2) + 1; // Team 1 or 2
                _logger.LogInformation("Assigning bot {PlayerId} to team {Team}", playerId, team);
            }
            else
            {
                // Human players
                if (_state.State.HumanPlayerCount == 0)
                {
                    // First human player is always team 1
                    team = 1;
                }
                else
                {
                    // Subsequent human players alternate between teams
                    team = _state.State.NextTeamAssignment;
                    _state.State.NextTeamAssignment = team == 1 ? 2 : 1; // Toggle between 1 and 2
                }
                _state.State.HumanPlayerCount++;
                _logger.LogInformation("Assigning human player {PlayerId} to team {Team} (human #{Count})", 
                    playerId, team, _state.State.HumanPlayerCount);
            }
            
            // Initialize the player grain with name and starting position
            await playerGrain.Initialize(name, startPosition);
            
            var playerInfo = new PlayerInfo(playerId, name, startPosition, Vector2.Zero, 1000f, team);
            _state.State.Players[playerId] = playerInfo;
            await _state.WriteStateAsync();
            
            return playerInfo;
        }
    }

    public async Task ResetAllServerAssignments()
    {
        _logger.LogWarning("Resetting all server assignments - clearing persisted state");
        
        // Clear all in-memory state
        _gridToServer.Clear();
        _serverIdToInfo.Clear();
        
        // Clear persisted state
        _state.State.ActionServers.Clear();
        await _state.WriteStateAsync();
        
        _logger.LogInformation("All server assignments have been reset");
    }
    
    public Task<Vector2> GetPlayerStartPosition(string playerId)
    {
        // For now, start all players in a zone that has an ActionServer
        // Look for any available zone with a server
        var random = new Random(playerId.GetHashCode());
        
        if (_gridToServer.Any())
        {
            // Pick a random zone that has a server
            var availableZones = _gridToServer.Keys.ToList();
            var selectedZone = availableZones[random.Next(availableZones.Count)];
            
            // Random position within the selected zone
            var (min, max) = selectedZone.GetBounds();
            var x = min.X + (float)random.NextDouble() * (max.X - min.X);
            var y = min.Y + (float)random.NextDouble() * (max.Y - min.Y);
            
            return Task.FromResult(new Vector2(x, y));
        }
        else
        {
            // No servers available, default to zone (0,0)
            // This should rarely happen as players shouldn't be able to join without servers
            var startSquare = new GridSquare(0, 0);
            var (min, max) = startSquare.GetBounds();
            var x = min.X + (float)random.NextDouble() * (max.X - min.X);
            var y = min.Y + (float)random.NextDouble() * (max.Y - min.Y);
            
            return Task.FromResult(new Vector2(x, y));
        }
    }
    
    public async Task<PlayerTransferInfo?> InitiatePlayerTransfer(string playerId, Vector2 currentPosition)
    {
        // Get the new grid square for the player's current position
        var newGridSquare = GridSquare.FromPosition(currentPosition);
        _logger.LogDebug("InitiatePlayerTransfer: Player {PlayerId} at position {Position} is in zone {Zone}", 
            playerId, currentPosition, newGridSquare);
        
        // Get the new server for this position
        var newServer = _gridToServer.TryGetValue(newGridSquare, out var server) ? server : null;
        if (newServer == null)
        {
            _logger.LogWarning("No server available for zone {Zone}. Available zones: {Zones}", 
                newGridSquare, string.Join(", ", _gridToServer.Keys.Select(z => $"({z.X},{z.Y})")));
            // No server available for this zone
            return null;
        }
        _logger.LogInformation("Found server {ServerId} for zone {Zone}", newServer.ServerId, newGridSquare);
        
        // Get player info
        if (!_state.State.Players.TryGetValue(playerId, out var playerInfo))
        {
            _logger.LogWarning("Player {PlayerId} not found in WorldManager state. Total registered players: {Count}", 
                playerId, _state.State.Players.Count);
            _logger.LogWarning("Registered players: {Players}", string.Join(", ", _state.State.Players.Keys));
            
            // Get player's current health before auto-registering
            var playerGrain = GrainFactory.GetGrain<IPlayerGrain>(playerId);
            var existingInfo = await playerGrain.GetInfo();
            
            // Auto-register the player if not found, but preserve their health
            _logger.LogInformation("Auto-registering player {PlayerId} with unknown name, preserving health: {Health}", 
                playerId, existingInfo?.Health ?? 1000f);
            
            if (existingInfo != null && existingInfo.Name != "")
            {
                // Player exists, use their actual name
                playerInfo = await RegisterPlayer(playerId, existingInfo.Name);
            }
            else
            {
                // New player
                playerInfo = await RegisterPlayer(playerId, "Unknown");
            }
        }
        _logger.LogInformation("Player {PlayerId} current stored position: {Position}", playerId, playerInfo.Position);
        
        // Get the player's current server based on their previous position
        var oldGridSquare = GridSquare.FromPosition(playerInfo.Position);
        var oldServer = _gridToServer.TryGetValue(oldGridSquare, out var old) ? old : null;
        
        // Update player position in state
        playerInfo = playerInfo with { Position = currentPosition };
        _state.State.Players[playerId] = playerInfo;
        await _state.WriteStateAsync();

        // If the player is already on the correct server, no transfer needed
        if (oldServer?.ServerId == newServer.ServerId)
        {
            _logger.LogDebug("Player {PlayerId} is already on server {ServerId}, no transfer needed", playerId, newServer.ServerId);
            return null;
        }

        _logger.LogInformation("Player {PlayerId} needs transfer from server {OldServer} to {NewServer}", 
            playerId, oldServer?.ServerId ?? "none", newServer.ServerId);
        return new PlayerTransferInfo(playerId, newServer, oldServer, playerInfo);
    }
    
    public async Task UpdatePlayerPosition(string playerId, Vector2 position)
    {
        if (_state.State.Players.TryGetValue(playerId, out var playerInfo))
        {
            _state.State.Players[playerId] = playerInfo with { Position = position };
            await _state.WriteStateAsync();
            
            // Also update the player grain - preserve existing velocity
            var playerGrain = GrainFactory.GetGrain<IPlayerGrain>(playerId);
            await playerGrain.UpdatePosition(position, playerInfo.Velocity);
        }
    }
    
    public async Task UpdatePlayerPositionAndVelocity(string playerId, Vector2 position, Vector2 velocity)
    {
        if (_state.State.Players.TryGetValue(playerId, out var playerInfo))
        {
            _state.State.Players[playerId] = playerInfo with { Position = position, Velocity = velocity };
            await _state.WriteStateAsync();
            
            // Also update the player grain
            var playerGrain = GrainFactory.GetGrain<IPlayerGrain>(playerId);
            await playerGrain.UpdatePosition(position, velocity);
        }
    }
    
    public async Task BroadcastChatMessage(ChatMessage message)
    {
        _logger.LogInformation("[CHAT_BROADCAST] Broadcasting chat message from {Sender}: {Message}", message.SenderName, message.Message);

        // Broadcast to all SignalR web clients
        if (_hubContext != null)
        {
            _logger.LogInformation("[CHAT_BROADCAST] HubContext available, sending to all SignalR clients");
            await _hubContext.Clients.All.ReceiveChatMessage(message);
            _logger.LogInformation("[CHAT_BROADCAST] Successfully broadcast chat message to all SignalR clients");
        }
        else
        {
            _logger.LogWarning("[CHAT_BROADCAST] HubContext is null - cannot broadcast to SignalR clients!");
        }

        // Note: Cross-zone chat forwarding is handled by GameGranule.ForwardChatMessageToAllZones()
        // WorldManagerGrain only needs to broadcast to SignalR web clients
    }
    
    public async Task NotifyGameOver()
    {
        // Increment the round counter
        _state.State.RoundsCompleted++;
        await _state.WriteStateAsync();
        
        _logger.LogInformation("Game over notification received. Rounds completed: {RoundsCompleted}", _state.State.RoundsCompleted);
        
        // Notify SignalR clients about game over
        if (_hubContext != null)
        {
            // Create a simple game over message with round info
            var gameOverMessage = new GameOverMessage(
                new List<PlayerScore>(), // Scores will be sent via chat
                DateTime.UtcNow,
                15 // Restart delay
            );
            await _hubContext.Clients.All.GameOver(gameOverMessage);
            _logger.LogDebug("Notified SignalR clients about game over");
        }
        
        // Check if we should quit after N rounds
        var quitAfterNRounds = _gameSettings.Value.QuitAfterNRounds;
        if (quitAfterNRounds > 0 && _state.State.RoundsCompleted >= quitAfterNRounds)
        {
            _logger.LogInformation("Reached configured round limit ({QuitAfterNRounds}). Initiating graceful shutdown...", quitAfterNRounds);
            
            // Broadcast a final message to all players
            var shutdownMessage = new ChatMessage(
                "System",
                "System",
                $"Server shutting down after {_state.State.RoundsCompleted} rounds. Thank you for playing!",
                DateTime.UtcNow
            );
            await BroadcastChatMessage(shutdownMessage);
            
            // Give a small delay to ensure the message is sent
            await Task.Delay(2000);
            
            // Trigger application shutdown
            _applicationLifetime.StopApplication();
        }
        else
        {
            _logger.LogInformation("Game over. Waiting for next round... (QuitAfterNRounds: {QuitAfterNRounds}, current: {RoundsCompleted})", 
                quitAfterNRounds, _state.State.RoundsCompleted);
        }
    }

    private async Task CheckTimeLimit(object? state)
    {
        var quitAfterNMinutes = _gameSettings.Value.QuitAfterNMinutes;
        if (quitAfterNMinutes > 0 && _state.State.FirstActivationTime.HasValue)
        {
            var elapsedMinutes = (DateTime.UtcNow - _state.State.FirstActivationTime.Value).TotalMinutes;
            if (elapsedMinutes >= quitAfterNMinutes)
            {
                _logger.LogInformation("Time limit reached ({QuitAfterNMinutes} minutes). Initiating shutdown...", quitAfterNMinutes);
                await InitiateShutdown($"{quitAfterNMinutes} minute time limit reached");
                
                // Dispose the timer to prevent further checks
                _timerHandle?.Dispose();
                _timerHandle = null;
            }
            else
            {
                var remainingMinutes = quitAfterNMinutes - elapsedMinutes;
                // Only log time checks at significant intervals
                if (Math.Abs(remainingMinutes % 1.0) < 0.1) // Log approximately every minute
                {
                    _logger.LogInformation("Time check: {ElapsedMinutes:F1} minutes elapsed, {RemainingMinutes:F1} minutes remaining", 
                        elapsedMinutes, remainingMinutes);
                }
            }
        }
    }

    private async Task InitiateShutdown(string reason)
    {
        _logger.LogWarning("Initiating graceful shutdown. Reason: {Reason}", reason);
        
        // Broadcast a final message to all players
        var shutdownMessage = new ChatMessage(
            "System",
            "System",
            $"Server shutting down ({reason}). Thank you for playing!",
            DateTime.UtcNow
        );
        await BroadcastChatMessage(shutdownMessage);
        
        // Give a small delay to ensure the message is sent
        await Task.Delay(2000);
        
        // Trigger application shutdown
        _applicationLifetime.StopApplication();
    }

    public Task ReportZoneStats(GridSquare zone, ZoneStats stats)
    {
        _zoneStats[zone] = stats;
        _lastStatsUpdate = DateTime.UtcNow;
        _logger.LogDebug("Received zone stats for ({X},{Y}): {Players} players, {Enemies} enemies, {Factories} factories",
            zone.X, zone.Y, stats.PlayerCount, stats.EnemyCount, stats.FactoryCount);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<GlobalZoneStats> StreamZoneStatistics(TimeSpan updateInterval)
    {
        _logger.LogInformation("Starting zone statistics stream with {Interval}ms interval", updateInterval.TotalMilliseconds);
        
        // For Orleans grains, we can't use CancellationToken in IAsyncEnumerable
        // The consumer will stop enumerating when they want to cancel
        while (true)
        {
            var globalStats = new GlobalZoneStats
            {
                Timestamp = DateTime.UtcNow
            };
            
            // Aggregate stats from all zones
            foreach (var (zone, stats) in _zoneStats)
            {
                // Only include stats that are recent (within last minute)
                if ((DateTime.UtcNow - stats.LastUpdate).TotalMinutes < 1)
                {
                    var zoneKey = $"{zone.X},{zone.Y}";
                    var serverInfo = _gridToServer.GetValueOrDefault(zone);
                    
                    globalStats.ZoneStats[zoneKey] = new ZoneStatsEntry
                    {
                        Zone = zone,
                        PlayerCount = stats.PlayerCount,
                        EnemyCount = stats.EnemyCount,
                        FactoryCount = stats.FactoryCount,
                        ServerId = serverInfo?.ServerId ?? "unknown",
                        LastUpdate = stats.LastUpdate
                    };
                    
                    globalStats.TotalPlayers += stats.PlayerCount;
                    globalStats.TotalEnemies += stats.EnemyCount;
                    globalStats.TotalFactories += stats.FactoryCount;
                }
            }
            
            globalStats.ActiveZoneCount = globalStats.ZoneStats.Count;
            
            // Determine global game phase based on round tracking
            if (_state.State.RoundsCompleted > 0 && globalStats.TotalEnemies == 0)
            {
                globalStats.GlobalGamePhase = GamePhase.GameOver;
            }
            
            yield return globalStats;
            
            // Wait for next update interval
            await Task.Delay(updateInterval);
        }
    }
    
    public override Task OnDeactivateAsync(Orleans.DeactivationReason reason, CancellationToken cancellationToken)
    {
        _timerHandle?.Dispose();
        return base.OnDeactivateAsync(reason, cancellationToken);
    }

    // ActionServer monitoring methods
    public async Task UpdateActionServerStatus(ActionServerStatus status)
    {
        _serverStatuses[status.ServerId] = status;
        
        // Update heartbeat if the server exists
        if (_serverIdToInfo.TryGetValue(status.ServerId, out var serverInfo))
        {
            var updatedInfo = serverInfo with { LastHeartbeat = DateTime.UtcNow };
            _serverIdToInfo[status.ServerId] = updatedInfo;
            _gridToServer[serverInfo.AssignedSquare] = updatedInfo;
        }
        
        await Task.CompletedTask;
    }

    public async Task<List<ActionServerStatus>> GetActionServerStatuses()
    {
        await Task.CompletedTask;
        return _serverStatuses.Values.ToList();
    }
    
    public Task UpdateActionServerHeartbeat(string serverId)
    {
        if (_serverIdToInfo.TryGetValue(serverId, out var serverInfo))
        {
            // Update the heartbeat timestamp
            var updatedServerInfo = serverInfo with { LastHeartbeat = DateTime.UtcNow };
            _serverIdToInfo[serverId] = updatedServerInfo;
            _gridToServer[serverInfo.AssignedSquare] = updatedServerInfo;
            
            // Update in persistent state
            var existingIndex = _state.State.ActionServers.FindIndex(s => s.ServerId == serverId);
            if (existingIndex >= 0)
            {
                _state.State.ActionServers[existingIndex] = updatedServerInfo;
            }
            
            _logger.LogDebug("Updated heartbeat for ActionServer {ServerId}", serverId);
        }
        else
        {
            _logger.LogWarning("Received heartbeat from unknown ActionServer {ServerId}", serverId);
        }
        
        return Task.CompletedTask;
    }
    
    public Task<List<PlayerInfo>> GetAllPlayers()
    {
        var players = _state.State.Players.Values.ToList();
        _logger.LogDebug("GetAllPlayers returning {Count} players", players.Count);
        return Task.FromResult(players);
    }
    
    public Task<GridSquare> RequestNewZone()
    {
        // Calculate the next zone position using the same logic as RegisterActionServer
        var totalServers = _gridToServer.Count + 1;
        var gridSize = (int)Math.Ceiling(Math.Sqrt(totalServers));
        
        GridSquare newZone;
        
        // Find the first available zone in row-by-row order
        for (int y = 0; y < gridSize; y++)
        {
            for (int x = 0; x < gridSize; x++)
            {
                var candidate = new GridSquare(x, y);
                if (!_gridToServer.ContainsKey(candidate))
                {
                    newZone = candidate;
                    _logger.LogInformation("Requested new zone at ({X},{Y}) - deployment needed", newZone.X, newZone.Y);
                    return Task.FromResult(newZone);
                }
            }
        }
        
        // If no available zones in current grid, expand it
        newZone = new GridSquare(0, gridSize);
        _logger.LogInformation("Requested new zone at ({X},{Y}) - expanding grid to accommodate", newZone.X, newZone.Y);
        return Task.FromResult(newZone);
    }
    
    public async Task<bool> RemoveLastZone()
    {
        if (!_gridToServer.Any())
        {
            _logger.LogWarning("Cannot remove zone - no zones exist");
            return false;
        }
        
        // Find the "last" server (highest Y, then highest X)
        var lastServer = _gridToServer.Values
            .OrderByDescending(s => s.AssignedSquare.Y)
            .ThenByDescending(s => s.AssignedSquare.X)
            .First();
            
        _logger.LogInformation("Removing zone ({X},{Y}) with server {ServerId}", 
            lastServer.AssignedSquare.X, lastServer.AssignedSquare.Y, lastServer.ServerId);
        
        // Remove from mappings
        _gridToServer.Remove(lastServer.AssignedSquare);
        _serverIdToInfo.Remove(lastServer.ServerId);
        _state.State.ActionServers.RemoveAll(s => s.ServerId == lastServer.ServerId);
        await _state.WriteStateAsync();
        
        return true;
    }
}

[Orleans.GenerateSerializer]
public class WorldManagerState
{
    public List<ActionServerInfo> ActionServers { get; set; } = new();
    public Dictionary<string, PlayerInfo> Players { get; set; } = new();
    public int RoundsCompleted { get; set; } = 0;
    public DateTime? FirstActivationTime { get; set; }
    public int NextTeamAssignment { get; set; } = 1; // Track which team to assign next human player
    public int HumanPlayerCount { get; set; } = 0; // Track number of human players (non-bots)
}
