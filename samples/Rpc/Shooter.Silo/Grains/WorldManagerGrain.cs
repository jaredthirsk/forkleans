using Forkleans;
using Forkleans.Runtime;
using Shooter.Shared.GrainInterfaces;
using Shooter.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Shooter.Silo.Grains;

public class WorldManagerGrain : Forkleans.Grain, IWorldManagerGrain
{
    private readonly IPersistentState<WorldManagerState> _state;
    private readonly ILogger<WorldManagerGrain> _logger;
    private readonly Dictionary<GridSquare, ActionServerInfo> _gridToServer = new();
    private readonly Dictionary<string, ActionServerInfo> _serverIdToInfo = new();

    public WorldManagerGrain(
        [PersistentState("worldManager", "worldStore")] IPersistentState<WorldManagerState> state,
        ILogger<WorldManagerGrain> logger)
    {
        _state = state;
        _logger = logger;
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // Restore state
        foreach (var server in _state.State.ActionServers)
        {
            _gridToServer[server.AssignedSquare] = server;
            _serverIdToInfo[server.ServerId] = server;
        }
        return base.OnActivateAsync(cancellationToken);
    }

    public async Task<ActionServerInfo> RegisterActionServer(string serverId, string ipAddress, int udpPort, string httpEndpoint, int rpcPort = 0)
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
            serverId, assignedSquare.X, assignedSquare.Y, gridSize, _gridToServer.Count);

        var serverInfo = new ActionServerInfo(serverId, ipAddress, udpPort, httpEndpoint, assignedSquare, DateTime.UtcNow, rpcPort);
        
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
        return Task.FromResult(_serverIdToInfo.Values.ToList());
    }

    public async Task<PlayerInfo> RegisterPlayer(string playerId, string name)
    {
        var playerGrain = GrainFactory.GetGrain<IPlayerGrain>(playerId);
        var startPosition = await GetPlayerStartPosition(playerId);
        
        // Initialize the player grain with name and starting position
        await playerGrain.Initialize(name, startPosition);
        
        var playerInfo = new PlayerInfo(playerId, name, startPosition, Vector2.Zero, 1000f);
        _state.State.Players[playerId] = playerInfo;
        await _state.WriteStateAsync();
        
        return playerInfo;
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
            
            // Auto-register the player if not found
            _logger.LogInformation("Auto-registering player {PlayerId} with unknown name", playerId);
            playerInfo = await RegisterPlayer(playerId, "Unknown");
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
}

[Forkleans.GenerateSerializer]
public class WorldManagerState
{
    public List<ActionServerInfo> ActionServers { get; set; } = new();
    public Dictionary<string, PlayerInfo> Players { get; set; } = new();
}
