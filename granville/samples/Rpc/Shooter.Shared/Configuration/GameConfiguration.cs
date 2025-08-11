namespace Shooter.Shared.Configuration;

/// <summary>
/// Shared configuration settings used across multiple components
/// </summary>
public static class GameConfiguration
{
    /// <summary>
    /// Zone grid size in world units - must match between client and server
    /// </summary>
    public const float ZoneGridSizeUnits = 500f;
    
    /// <summary>
    /// Maximum world coordinate (both positive and negative)
    /// </summary>
    public const float MaxWorldCoordinate = 10000f;
    
    /// <summary>
    /// Player movement speed in units per second
    /// </summary>
    public const float PlayerSpeedUnitsPerSecond = 150f;
    
    /// <summary>
    /// Bullet speed in units per second
    /// </summary>
    public const float BulletSpeedUnitsPerSecond = 500f;
    
    /// <summary>
    /// Bullet lifetime in seconds
    /// </summary>
    public const double BulletLifetimeSeconds = 3.0;
    
    /// <summary>
    /// Explosion duration in seconds
    /// </summary>
    public const double ExplosionDurationSeconds = 0.5;
}