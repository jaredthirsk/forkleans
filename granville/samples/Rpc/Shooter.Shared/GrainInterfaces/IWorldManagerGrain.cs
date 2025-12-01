using Granville.Rpc.Security;
using Orleans;
using Shooter.Shared.Models;

namespace Shooter.Shared.GrainInterfaces;

/// <summary>
/// World manager grain for coordinating game state.
/// Most operations are server-only, with some read-only client access.
/// </summary>
[Authorize]
public interface IWorldManagerGrain : Orleans.IGrainWithIntegerKey
{
    // Server-only operations
    [ServerOnly]
    Task<ActionServerInfo> RegisterActionServer(string serverId, string ipAddress, int udpPort, string httpEndpoint, int rpcPort = 0, string? webUrl = null, bool hasPhaserView = false);

    [ServerOnly]
    Task UnregisterActionServer(string serverId);

    // Read-only operations accessible to clients
    [ClientAccessible]
    Task<ActionServerInfo?> GetActionServerForPosition(Vector2 position);

    [ClientAccessible]
    Task<List<ActionServerInfo>> GetAllActionServers();

    [ServerOnly]
    Task<PlayerInfo> RegisterPlayer(string playerId, string name);

    [ServerOnly]
    Task<Vector2> GetPlayerStartPosition(string playerId);

    [ServerOnly]
    Task ResetAllServerAssignments();

    // Zone transition support - server operations
    [ServerOnly]
    Task<PlayerTransferInfo?> InitiatePlayerTransfer(string playerId, Vector2 currentPosition);

    [ServerOnly]
    Task UpdatePlayerPosition(string playerId, Vector2 position);

    [ServerOnly]
    Task UpdatePlayerPositionAndVelocity(string playerId, Vector2 position, Vector2 velocity);

    // Chat message broadcasting - server only
    [ServerOnly]
    Task BroadcastChatMessage(ChatMessage message);

    // Game round tracking - server only
    [ServerOnly]
    Task NotifyGameOver();

    // Zone statistics - server only
    [ServerOnly]
    Task ReportZoneStats(GridSquare zone, ZoneStats stats);

    [ServerOnly]
    IAsyncEnumerable<GlobalZoneStats> StreamZoneStatistics(TimeSpan updateInterval);

    // ActionServer monitoring - server only
    [ServerOnly]
    Task UpdateActionServerStatus(ActionServerStatus status);

    [ClientAccessible]
    Task<List<ActionServerStatus>> GetActionServerStatuses();

    [ServerOnly]
    Task UpdateActionServerHeartbeat(string serverId);

    // Player information - client accessible for scoreboard
    [ClientAccessible]
    Task<List<PlayerInfo>> GetAllPlayers();

    // Zone management - server only
    [ServerOnly]
    Task<GridSquare> RequestNewZone();

    [ServerOnly]
    Task<bool> RemoveLastZone();
}