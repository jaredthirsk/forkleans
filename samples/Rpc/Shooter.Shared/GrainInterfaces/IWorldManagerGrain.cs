using Orleans;
using Shooter.Shared.Models;

namespace Shooter.Shared.GrainInterfaces;

public interface IWorldManagerGrain : Orleans.IGrainWithIntegerKey
{
    Task<ActionServerInfo> RegisterActionServer(string serverId, string ipAddress, int udpPort);
    Task UnregisterActionServer(string serverId);
    Task<ActionServerInfo?> GetActionServerForPosition(Vector2 position);
    Task<List<ActionServerInfo>> GetAllActionServers();
    Task<PlayerInfo> RegisterPlayer(string playerId, string name);
    Task<Vector2> GetPlayerStartPosition(string playerId);
}