using Orleans;
using Orleans.Runtime;
using Shooter.Shared.GrainInterfaces;
using Shooter.Shared.Models;

namespace Shooter.Silo.Grains;

public class WorldManagerGrain : Orleans.Grain, IWorldManagerGrain
{
    private readonly IPersistentState<WorldManagerState> _state;
    private readonly Dictionary<GridSquare, ActionServerInfo> _gridToServer = new();
    private readonly Dictionary<string, ActionServerInfo> _serverIdToInfo = new();
    private int _nextServerIndex = 0;

    public WorldManagerGrain(
        [PersistentState("worldManager", "worldStore")] IPersistentState<WorldManagerState> state)
    {
        _state = state;
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

    public async Task<ActionServerInfo> RegisterActionServer(string serverId, string ipAddress, int udpPort, string httpEndpoint)
    {
        // Create a 3x3 grid pattern
        // Server 0: (0,0), Server 1: (1,0), Server 2: (2,0)
        // Server 3: (0,1), Server 4: (1,1), Server 5: (2,1)
        // Server 6: (0,2), Server 7: (1,2), Server 8: (2,2)
        
        var gridX = _nextServerIndex % 3;
        var gridY = (_nextServerIndex / 3) % 3;
        var assignedSquare = new GridSquare(gridX, gridY);
        
        _nextServerIndex++;

        var serverInfo = new ActionServerInfo(serverId, ipAddress, udpPort, httpEndpoint, assignedSquare, DateTime.UtcNow);
        
        // If a server already manages this square, remove it first
        if (_gridToServer.ContainsKey(assignedSquare))
        {
            var oldServer = _gridToServer[assignedSquare];
            _serverIdToInfo.Remove(oldServer.ServerId);
            _state.State.ActionServers.RemoveAll(s => s.ServerId == oldServer.ServerId);
        }
        
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

    public Task<Vector2> GetPlayerStartPosition(string playerId)
    {
        // Start players randomly in one of the 3x3 zones
        var random = new Random(playerId.GetHashCode());
        var gridX = random.Next(0, 3);
        var gridY = random.Next(0, 3);
        var startSquare = new GridSquare(gridX, gridY);
        
        // Random position within the grid square
        var (min, max) = startSquare.GetBounds();
        var x = min.X + (float)random.NextDouble() * (max.X - min.X);
        var y = min.Y + (float)random.NextDouble() * (max.Y - min.Y);
        
        return Task.FromResult(new Vector2(x, y));
    }
    
    public async Task<PlayerTransferInfo?> InitiatePlayerTransfer(string playerId, Vector2 currentPosition)
    {
        // Get the new grid square for the player's current position
        var newGridSquare = GridSquare.FromPosition(currentPosition);
        
        // Get the new server for this position
        var newServer = _gridToServer.TryGetValue(newGridSquare, out var server) ? server : null;
        if (newServer == null)
        {
            // No server available for this zone
            return null;
        }
        
        // Get player info
        if (!_state.State.Players.TryGetValue(playerId, out var playerInfo))
        {
            return null;
        }
        
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
            return null;
        }
        
        return new PlayerTransferInfo(playerId, newServer, oldServer, playerInfo);
    }
    
    public async Task UpdatePlayerPosition(string playerId, Vector2 position)
    {
        if (_state.State.Players.TryGetValue(playerId, out var playerInfo))
        {
            _state.State.Players[playerId] = playerInfo with { Position = position };
            await _state.WriteStateAsync();
            
            // Also update the player grain
            var playerGrain = GrainFactory.GetGrain<IPlayerGrain>(playerId);
            await playerGrain.UpdatePosition(position, Vector2.Zero);
        }
    }
}

[Orleans.GenerateSerializer]
public class WorldManagerState
{
    public List<ActionServerInfo> ActionServers { get; set; } = new();
    public Dictionary<string, PlayerInfo> Players { get; set; } = new();
}