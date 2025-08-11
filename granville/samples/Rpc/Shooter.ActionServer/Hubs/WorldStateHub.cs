using Microsoft.AspNetCore.SignalR;
using Shooter.ActionServer.Services;
using Shooter.ActionServer.Simulation;
using Shooter.Shared.Models;
using System.Collections.Concurrent;

namespace Shooter.ActionServer.Hubs;

public class WorldStateHub : Hub
{
    private readonly IWorldSimulation _worldSimulation;
    private readonly CrossZoneRpcService _crossZoneRpc;
    private readonly ILogger<WorldStateHub> _logger;
    private readonly Orleans.IClusterClient _orleansClient;
    private readonly ConcurrentDictionary<string, ViewSettings> _clientSettings = new();

    public class ViewSettings
    {
        public bool ShowLocalEntities { get; set; } = true;
        public bool ShowAdjacentEntities { get; set; } = false;
        public List<GridSquare> SelectedAdjacentZones { get; set; } = new();
    }

    public WorldStateHub(
        IWorldSimulation worldSimulation,
        CrossZoneRpcService crossZoneRpc,
        ILogger<WorldStateHub> logger,
        Orleans.IClusterClient orleansClient)
    {
        _worldSimulation = worldSimulation;
        _crossZoneRpc = crossZoneRpc;
        _logger = logger;
        _orleansClient = orleansClient;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Phaser view client connected: {ConnectionId}", Context.ConnectionId);
        
        // Initialize default settings for this client
        _clientSettings[Context.ConnectionId] = new ViewSettings();
        
        // Send initial configuration
        var assignedSquare = _worldSimulation.GetAssignedSquare();
        await Clients.Caller.SendAsync("serverConfig", new
        {
            AssignedZone = new { assignedSquare.X, assignedSquare.Y },
            ConnectionId = Context.ConnectionId
        });
        
        // Start sending updates to this client
        _ = Task.Run(async () => await SendPeriodicUpdates(Context.ConnectionId));
        
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Phaser view client disconnected: {ConnectionId}", Context.ConnectionId);
        _clientSettings.TryRemove(Context.ConnectionId, out _);
        return base.OnDisconnectedAsync(exception);
    }

    public Task UpdateViewSettings(bool showLocal, bool showAdjacent, List<GridSquare>? selectedZones)
    {
        if (_clientSettings.TryGetValue(Context.ConnectionId, out var settings))
        {
            settings.ShowLocalEntities = showLocal;
            settings.ShowAdjacentEntities = showAdjacent;
            settings.SelectedAdjacentZones = selectedZones ?? new List<GridSquare>();
            
            _logger.LogInformation("Updated view settings for {ConnectionId}: Local={ShowLocal}, Adjacent={ShowAdjacent}, Zones={ZoneCount}",
                Context.ConnectionId, showLocal, showAdjacent, settings.SelectedAdjacentZones.Count);
        }
        
        return Task.CompletedTask;
    }

    private async Task SendPeriodicUpdates(string connectionId)
    {
        var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100)); // 10 FPS update rate
        
        try
        {
            while (await timer.WaitForNextTickAsync())
            {
                // Check if client is still connected
                if (!_clientSettings.ContainsKey(connectionId))
                {
                    break;
                }
                
                var settings = _clientSettings[connectionId];
                var combinedState = new CombinedWorldState();
                
                // Get local entities
                if (settings.ShowLocalEntities)
                {
                    var localState = _worldSimulation.GetCurrentState();
                    combinedState.LocalEntities = localState.Entities.ToList();
                    combinedState.LocalZone = _worldSimulation.GetAssignedSquare();
                    combinedState.Timestamp = localState.Timestamp;
                }
                
                // Get adjacent zone entities
                if (settings.ShowAdjacentEntities && settings.SelectedAdjacentZones.Any())
                {
                    combinedState.AdjacentZoneEntities = new Dictionary<string, List<EntityState>>();
                    
                    try
                    {
                        // Get all action servers
                        var worldManager = _orleansClient.GetGrain<Shooter.Shared.GrainInterfaces.IWorldManagerGrain>(0);
                        var allServers = await worldManager.GetAllActionServers();
                        
                        // Fetch state from selected adjacent zones
                        foreach (var zone in settings.SelectedAdjacentZones)
                        {
                            var server = allServers.FirstOrDefault(s => 
                                s.AssignedSquare.X == zone.X && s.AssignedSquare.Y == zone.Y);
                                
                            if (server != null)
                            {
                                try
                                {
                                    var gameGrain = await _crossZoneRpc.GetGameGrainForZone(server, zone, bypassZoneCheck: true);
                                    var adjacentState = await gameGrain.GetWorldState();
                                    
                                    if (adjacentState != null)
                                    {
                                        var zoneKey = $"{zone.X},{zone.Y}";
                                        combinedState.AdjacentZoneEntities[zoneKey] = adjacentState.Entities.ToList();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Failed to get state from adjacent zone {ZoneX},{ZoneY}", zone.X, zone.Y);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to fetch adjacent zone states");
                    }
                }
                
                // Send update to client
                await Clients.Client(connectionId).SendAsync("worldStateUpdate", combinedState);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in periodic update loop for {ConnectionId}", connectionId);
        }
        finally
        {
            timer.Dispose();
        }
    }
    
    public class CombinedWorldState
    {
        public List<EntityState> LocalEntities { get; set; } = new();
        public Dictionary<string, List<EntityState>> AdjacentZoneEntities { get; set; } = new();
        public GridSquare? LocalZone { get; set; }
        public DateTime Timestamp { get; set; }
    }
}