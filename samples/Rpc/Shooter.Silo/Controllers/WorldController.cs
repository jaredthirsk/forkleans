using Microsoft.AspNetCore.Mvc;
using Orleans;
using Shooter.Shared.GrainInterfaces;
using Shooter.Shared.Models;

namespace Shooter.Silo.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WorldController : ControllerBase
{
    private readonly IGrainFactory _grainFactory;

    public WorldController(IGrainFactory grainFactory)
    {
        _grainFactory = grainFactory;
    }

    [HttpPost("action-servers/register")]
    public async Task<ActionResult<ActionServerInfo>> RegisterActionServer(RegisterActionServerRequest request)
    {
        var worldManager = _grainFactory.GetGrain<IWorldManagerGrain>(0);
        var serverInfo = await worldManager.RegisterActionServer(
            request.ServerId, 
            request.IpAddress, 
            request.UdpPort);
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
}

public record RegisterActionServerRequest(string ServerId, string IpAddress, int UdpPort);
public record RegisterPlayerRequest(string PlayerId, string Name);
public record PlayerRegistrationResponse
{
    public required PlayerInfo PlayerInfo { get; init; }
    public ActionServerInfo? ActionServer { get; init; }
}