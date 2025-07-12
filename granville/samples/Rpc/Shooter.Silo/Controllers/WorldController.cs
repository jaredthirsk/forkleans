using Microsoft.AspNetCore.Mvc;
using Orleans;
using Shooter.Shared.GrainInterfaces;
using Shooter.Shared.Models;
using System.Net.Http;
using Shooter.Silo.Services;

namespace Shooter.Silo.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WorldController : ControllerBase
{
    private readonly Orleans.IGrainFactory _grainFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WorldController> _logger;
    private readonly ActionServerManager _actionServerManager;
    private static readonly SemaphoreSlim _addServerSemaphore = new(1, 1);

    public WorldController(
        Orleans.IGrainFactory grainFactory, 
        IHttpClientFactory httpClientFactory, 
        ILogger<WorldController> logger,
        ActionServerManager actionServerManager)
    {
        _grainFactory = grainFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _actionServerManager = actionServerManager;
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
                    PlayerCount = response?.PlayerCount ?? 0,
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
                    PlayerCount = 0,
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
    
    [HttpPost("action-servers/add")]
    public async Task<ActionResult<ActionServerInfo>> AddActionServer()
    {
        // Prevent concurrent server additions
        if (!await _addServerSemaphore.WaitAsync(0))
        {
            _logger.LogWarning("Another ActionServer addition is already in progress");
            return StatusCode(429, new { error = "Another server addition is in progress. Please try again later." });
        }
        
        try
        {
            _logger.LogInformation("Request to add new ActionServer");
            
            // Start a new ActionServer process
            var serverId = await _actionServerManager.StartNewActionServerAsync();
            
            // Wait for the server to register, with retries
            var worldManager = _grainFactory.GetGrain<IWorldManagerGrain>(0);
            ActionServerInfo? newServer = null;
            
            // Wait up to 30 seconds for the server to register
            for (int i = 0; i < 6; i++)
            {
                await Task.Delay(5000);
                
                var servers = await worldManager.GetAllActionServers();
                newServer = servers.FirstOrDefault(s => s.ServerId == serverId);
                
                if (newServer != null)
                {
                    break;
                }
                
                _logger.LogDebug("Waiting for ActionServer {ServerId} to register... (attempt {Attempt}/6)", serverId, i + 1);
            }
            
            if (newServer == null)
            {
                _logger.LogWarning("New ActionServer {ServerId} did not register within timeout", serverId);
                return StatusCode(500, new { error = "ActionServer started but did not register within 30 seconds" });
            }
            
            _logger.LogInformation("Successfully added ActionServer {ServerId} with zone ({X},{Y})", 
                serverId, newServer.AssignedSquare.X, newServer.AssignedSquare.Y);
            
            return Ok(newServer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add new ActionServer");
            return StatusCode(500, new { error = ex.Message });
        }
        finally
        {
            _addServerSemaphore.Release();
        }
    }
    
    [HttpPost("action-servers/{serverId}/remove")]
    public async Task<IActionResult> RemoveActionServer(string serverId)
    {
        try
        {
            _logger.LogInformation("Request to remove ActionServer {ServerId}", serverId);
            
            // First, check if this server exists
            var worldManager = _grainFactory.GetGrain<IWorldManagerGrain>(0);
            var servers = await worldManager.GetAllActionServers();
            var serverToRemove = servers.FirstOrDefault(s => s.ServerId == serverId);
            
            if (serverToRemove == null)
            {
                return NotFound(new { error = $"ActionServer {serverId} not found" });
            }
            
            // Try to gracefully shut down the server via HTTP
            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5);
                
                var shutdownUrl = $"{serverToRemove.HttpEndpoint}/api/admin/shutdown";
                _logger.LogInformation("Sending shutdown request to {Url}", shutdownUrl);
                
                var response = await httpClient.PostAsync(shutdownUrl, null);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("ActionServer {ServerId} acknowledged shutdown request", serverId);
                    await Task.Delay(2000); // Give it time to shut down gracefully
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send graceful shutdown to ActionServer {ServerId}, will force terminate", serverId);
            }
            
            // Unregister from Orleans
            await worldManager.UnregisterActionServer(serverId);
            
            // Stop the process if it's managed by us
            var stopped = await _actionServerManager.StopActionServerAsync(serverId);
            if (!stopped)
            {
                _logger.LogWarning("ActionServer {ServerId} was not managed by this Silo", serverId);
            }
            
            _logger.LogInformation("Successfully removed ActionServer {ServerId}", serverId);
            
            return Ok(new { message = $"ActionServer {serverId} removed successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove ActionServer {ServerId}", serverId);
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    [HttpGet("damage-stats")]
    public async Task<ActionResult<Dictionary<string, object>>> GetDamageStats()
    {
        var statsCollector = _grainFactory.GetGrain<IStatsCollectorGrain>(0);
        
        var topByDamageDealt = await statsCollector.GetTopPlayersByDamageDealt(10);
        var topByDamageReceived = await statsCollector.GetTopPlayersByDamageReceived(10);
        var allZoneReports = await statsCollector.GetAllZoneReports();
        
        return Ok(new
        {
            TopPlayersByDamageDealt = topByDamageDealt,
            TopPlayersByDamageReceived = topByDamageReceived,
            ZoneReports = allZoneReports.Select(kvp => new
            {
                ServerId = kvp.Key,
                Zone = kvp.Value.Zone,
                PlayerCount = kvp.Value.PlayerStats.Count,
                DamageEventCount = kvp.Value.DamageEvents.Count,
                StartTime = kvp.Value.StartTime,
                EndTime = kvp.Value.EndTime
            })
        });
    }
    
    [HttpGet("damage-stats/player/{playerId}")]
    public async Task<ActionResult<PlayerDamageStats>> GetPlayerDamageStats(string playerId)
    {
        var statsCollector = _grainFactory.GetGrain<IStatsCollectorGrain>(0);
        var stats = await statsCollector.GetPlayerStats(playerId);
        return Ok(stats);
    }
    
    [HttpPost("damage-stats/clear")]
    public async Task<IActionResult> ClearDamageStats()
    {
        var statsCollector = _grainFactory.GetGrain<IStatsCollectorGrain>(0);
        await statsCollector.ClearStats();
        return Ok(new { message = "Damage statistics cleared" });
    }
}

public record RegisterActionServerRequest(string ServerId, string IpAddress, int UdpPort, string HttpEndpoint, int RpcPort = 0);
public record RegisterPlayerRequest(string PlayerId, string Name);
public record WorldZoneStats
{
    public GridSquare Zone { get; init; } = null!;
    public int PlayerCount { get; init; }
    public int FactoryCount { get; init; }
    public int EnemyCount { get; init; }
}
public record PlayerRegistrationResponse
{
    public required PlayerInfo PlayerInfo { get; init; }
    public ActionServerInfo? ActionServer { get; init; }
}