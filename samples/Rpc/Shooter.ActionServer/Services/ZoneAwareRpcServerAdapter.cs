using Forkleans.Rpc;
using Shooter.ActionServer.Simulation;
using Shooter.Shared.Models;

namespace Shooter.ActionServer.Services;

/// <summary>
/// Adapter to make the ActionServer zone-aware for RPC.
/// </summary>
public class ZoneAwareRpcServerAdapter : IZoneAwareRpcServer
{
    private readonly IWorldSimulation _worldSimulation;
    private readonly ILogger<ZoneAwareRpcServerAdapter> _logger;

    public ZoneAwareRpcServerAdapter(IWorldSimulation worldSimulation, ILogger<ZoneAwareRpcServerAdapter> logger)
    {
        _worldSimulation = worldSimulation;
        _logger = logger;
    }

    public int? GetZoneId()
    {
        var assignedSquare = _worldSimulation.GetAssignedSquare();
        if (assignedSquare == null)
        {
            _logger.LogWarning("No zone assigned to this server");
            return null;
        }

        // Convert GridSquare (X,Y) to a single zone ID
        // Using a simple formula: zoneId = y * 1000 + x
        // This assumes zones are in a reasonable range (e.g., -500 to 500)
        var zoneId = assignedSquare.Y * 1000 + assignedSquare.X;
        
        _logger.LogDebug("Zone ({X},{Y}) mapped to ZoneId: {ZoneId}", 
            assignedSquare.X, assignedSquare.Y, zoneId);
        
        return zoneId;
    }
}