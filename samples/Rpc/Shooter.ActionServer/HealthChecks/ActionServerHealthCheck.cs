using Microsoft.Extensions.Diagnostics.HealthChecks;
using Orleans;
using Granville.Rpc;
using Shooter.ActionServer.Services;
using Shooter.Shared.GrainInterfaces;

namespace Shooter.ActionServer.HealthChecks;

public class ActionServerHealthCheck : IHealthCheck
{
    private readonly IClusterClient _clusterClient;
    private readonly ILocalRpcServerDetails _rpcServerDetails;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<ActionServerHealthCheck> _logger;
    private readonly GameService _gameService;

    public ActionServerHealthCheck(
        IClusterClient clusterClient,
        ILocalRpcServerDetails rpcServerDetails,
        IHostApplicationLifetime lifetime,
        ILogger<ActionServerHealthCheck> logger,
        GameService gameService)
    {
        _clusterClient = clusterClient;
        _rpcServerDetails = rpcServerDetails;
        _lifetime = lifetime;
        _logger = logger;
        _gameService = gameService;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var data = new Dictionary<string, object>();

            // Check if the application has started
            if (!_lifetime.ApplicationStarted.IsCancellationRequested)
            {
                return HealthCheckResult.Degraded("Application is still starting");
            }

            // Check RPC server status
            var rpcEndpoint = _rpcServerDetails.ServerEndpoint;
            if (rpcEndpoint == null)
            {
                return HealthCheckResult.Unhealthy("RPC server is not running");
            }
            data["RpcPort"] = rpcEndpoint.Port;
            data["RpcServerId"] = _rpcServerDetails.ServerId;

            // Check Orleans client connection by trying to use it
            try
            {
                // Simple test to verify client is connected
                var testGrain = _clusterClient.GetGrain<IWorldManagerGrain>(0);
                // If we can get a grain reference, the client is connected
            }
            catch
            {
                return HealthCheckResult.Unhealthy("Orleans client is not connected");
            }

            // Check zone assignment
            var zoneInfo = _gameService.GetZoneInfo();
            if (zoneInfo == null || zoneInfo.ZoneId <= 0)
            {
                return HealthCheckResult.Degraded("Zone not yet assigned");
            }
            
            data["ZoneId"] = zoneInfo.ZoneId;
            data["ZoneX"] = zoneInfo.X;
            data["ZoneY"] = zoneInfo.Y;

            // Try to communicate with WorldManagerGrain
            var worldManager = _clusterClient.GetGrain<IWorldManagerGrain>(0);
            var actionServers = await worldManager.GetAllActionServers();
            
            data["TotalActionServers"] = actionServers?.Count ?? 0;
            data["Status"] = "Ready";

            return HealthCheckResult.Healthy("ActionServer is healthy and zone is assigned", data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return HealthCheckResult.Unhealthy("Health check failed", ex);
        }
    }
}