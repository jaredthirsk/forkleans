using Microsoft.AspNetCore.Mvc;
using Orleans;
using Shooter.Shared.GrainInterfaces;
using Shooter.Shared.Models;
using System.Net.Http;

namespace Shooter.Silo.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WorldController : ControllerBase
{
    private readonly Orleans.IGrainFactory _grainFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WorldController> _logger;

    public WorldController(Orleans.IGrainFactory grainFactory, IHttpClientFactory httpClientFactory, ILogger<WorldController> logger)
    {
        _grainFactory = grainFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpPost("action-servers/register")]
    public async Task<ActionResult<ActionServerInfo>> RegisterActionServer(RegisterActionServerRequest request)
    {
        var worldManager = _grainFactory.GetGrain<IWorldManagerGrain>(0);
        var serverInfo = await worldManager.RegisterActionServer(
            request.ServerId, 
            request.IpAddress, 
            request.UdpPort,
            request.HttpEndpoint,
            request.RpcPort);
        return Ok(serverInfo);
    }

    [HttpDelete("action-servers/{serverId}")]
    public async Task<IActionResult> UnregisterActionServer(string serverId)
    {
        var worldManager = _grainFactory.GetGrain<IWorldManagerGrain>(0);
        await worldManager.UnregisterActionServer(serverId);
        return Ok();
    }

    [HttpGet("action-servers")]
    public async Task<ActionResult<List<ActionServerInfo>>> GetActionServers()
    {
        var worldManager = _grainFactory.GetGrain<IWorldManagerGrain>(0);
        var servers = await worldManager.GetAllActionServers();
        return Ok(servers);
    }

    [HttpGet("action-servers/for-position")]
    public async Task<ActionResult<ActionServerInfo>> GetActionServerForPosition(
        [FromQuery] float x, 
        [FromQuery] float y)
    {
        var worldManager = _grainFactory.GetGrain<IWorldManagerGrain>(0);
        var server = await worldManager.GetActionServerForPosition(new Vector2(x, y));
        if (server == null)
            return NotFound();
        return Ok(server);
    }

    [HttpPost("players/register")]
    public async Task<ActionResult<PlayerRegistrationResponse>> RegisterPlayer(RegisterPlayerRequest request)
    {
        var worldManager = _grainFactory.GetGrain<IWorldManagerGrain>(0);
        var playerInfo = await worldManager.RegisterPlayer(request.PlayerId, request.Name);
        
        // Get the action server for this player's starting position
        var actionServer = await worldManager.GetActionServerForPosition(playerInfo.Position);
        
        return Ok(new PlayerRegistrationResponse
        {
            PlayerInfo = playerInfo,
            ActionServer = actionServer
        });
    }

    [HttpGet("players/{playerId}")]
    public async Task<ActionResult<PlayerInfo>> GetPlayer(string playerId)
    {
        var playerGrain = _grainFactory.GetGrain<IPlayerGrain>(playerId);
        var info = await playerGrain.GetInfo();
        return Ok(info);
    }
    
    [HttpGet("players/{playerId}/server")]
    public async Task<ActionResult<ActionServerInfo>> GetPlayerServer(string playerId)
    {
        var playerGrain = _grainFactory.GetGrain<IPlayerGrain>(playerId);
        var info = await playerGrain.GetInfo();
        
        var worldManager = _grainFactory.GetGrain<IWorldManagerGrain>(0);
        var server = await worldManager.GetActionServerForPosition(info.Position);
        
        if (server == null)
            return NotFound();
            
        return Ok(server);
    }
    
    [HttpGet("zone-stats")]
    public async Task<ActionResult<List<WorldZoneStats>>> GetZoneStats()
    {
        var worldManager = _grainFactory.GetGrain<IWorldManagerGrain>(0);
        var servers = await worldManager.GetAllActionServers();
        
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(5);
        
        var tasks = servers.Select(async server =>
        {
            try
            {
                // Query each action server for its zone stats
                var url = $"{server.HttpEndpoint}/api/game/zone-stats";
                var response = await httpClient.GetFromJsonAsync<ZoneStats>(url);
                
                return new WorldZoneStats
                {
                    Zone = server.AssignedSquare,
                    FactoryCount = response?.FactoryCount ?? 0,
                    EnemyCount = response?.EnemyCount ?? 0
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get zone stats from server {ServerId} at {Endpoint}", 
                    server.ServerId, server.HttpEndpoint);
                
                // Return empty stats on failure
                return new WorldZoneStats
                {
                    Zone = server.AssignedSquare,
                    FactoryCount = 0,
                    EnemyCount = 0
                };
            }
        });
        
        var stats = await Task.WhenAll(tasks);
        
        return Ok(stats.ToList());
    }
    
    [HttpPost("reset-server-assignments")]
    public async Task<IActionResult> ResetServerAssignments()
    {
        _logger.LogWarning("Resetting all server assignments - this will force all ActionServers to re-register");
        
        var worldManager = _grainFactory.GetGrain<IWorldManagerGrain>(0);
        await worldManager.ResetAllServerAssignments();
        
        return Ok(new { message = "All server assignments have been reset. ActionServers will need to restart to re-register." });
    }
}

public record RegisterActionServerRequest(string ServerId, string IpAddress, int UdpPort, string HttpEndpoint, int RpcPort = 0);
public record RegisterPlayerRequest(string PlayerId, string Name);
public record WorldZoneStats
{
    public GridSquare Zone { get; init; } = null!;
    public int FactoryCount { get; init; }
    public int EnemyCount { get; init; }
}
public record PlayerRegistrationResponse
{
    public required PlayerInfo PlayerInfo { get; init; }
    public ActionServerInfo? ActionServer { get; init; }
}