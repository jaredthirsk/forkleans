using Orleans;
using Orleans.Runtime;
using Shooter.Shared.GrainInterfaces;
using Shooter.Shared.Models;

namespace Shooter.Silo.Grains;

public class WorldManagerGrain : Grain, IWorldManagerGrain
{
    private readonly IPersistentState<WorldManagerState> _state;
    private readonly Dictionary<GridSquare, ActionServerInfo> _gridToServer = new();
    private readonly Dictionary<string, ActionServerInfo> _serverIdToInfo = new();
    private int _nextGridX = 0;
    private int _nextGridY = 0;

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

    public async Task<ActionServerInfo> RegisterActionServer(string serverId, string ipAddress, int udpPort)
    {
        // Assign next available grid square
        var assignedSquare = new GridSquare(_nextGridX, _nextGridY);
        
        // Simple grid expansion pattern
        if (_nextGridX == 0 && _nextGridY == 0)
        {
            _nextGridX = 1;
        }
        else if (_nextGridX > 0 && _nextGridY == 0)
        {
            _nextGridY = 1;
        }
        else if (_nextGridX > 0 && _nextGridY > 0)
        {
            _nextGridX = 0;
            _nextGridY = 1;
        }
        else
        {
            _nextGridX++;
            _nextGridY = 0;
        }

        var serverInfo = new ActionServerInfo(serverId, ipAddress, udpPort, assignedSquare, DateTime.UtcNow);
        
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
        
        var playerInfo = new PlayerInfo(playerId, name, startPosition, Vector2.Zero, 100f);
        _state.State.Players[playerId] = playerInfo;
        await _state.WriteStateAsync();
        
        return playerInfo;
    }

    public Task<Vector2> GetPlayerStartPosition(string playerId)
    {
        // Start players in the center of the first grid square
        var startSquare = new GridSquare(0, 0);
        return Task.FromResult(startSquare.GetCenter());
    }
}

[GenerateSerializer]
public class WorldManagerState
{
    public List<ActionServerInfo> ActionServers { get; set; } = new();
    public Dictionary<string, PlayerInfo> Players { get; set; } = new();
}