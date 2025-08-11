namespace Shooter.ActionServer.Configuration;

/// <summary>
/// Configuration settings for the Shooter ActionServer
/// </summary>
public static class ActionServerConfiguration
{
    /// <summary>
    /// Zone grid size in world units - defines the size of each zone square
    /// </summary>
    public const float ZoneGridSizeUnits = 500f;
    
    /// <summary>
    /// World update rate in milliseconds
    /// </summary>
    public const int WorldUpdateIntervalMs = 50; // 20 FPS
    
    /// <summary>
    /// Player inactivity timeout before removal
    /// </summary>
    public const double PlayerInactivityTimeoutSeconds = 30.0;
    
    /// <summary>
    /// Maximum entities per zone before spawning is throttled
    /// </summary>
    public const int MaxEntitiesPerZone = 200;
    
    /// <summary>
    /// Distance at which entities are considered "near" for interaction
    /// </summary>
    public const float EntityInteractionDistanceUnits = 100f;
    
    /// <summary>
    /// Scout alert radius - how far scout alerts reach to neighboring zones
    /// </summary>
    public const float ScoutAlertRadiusUnits = 1000f;
}