namespace Shooter.Client.Common.Configuration;

/// <summary>
/// Configuration settings for the Shooter client
/// </summary>
public static class ClientConfiguration
{
    /// <summary>
    /// Zone transition warning time - if a server transition takes longer than this, show warning colors
    /// </summary>
    public const double ZoneTransitionWarningTimeSeconds = 1.0;
    
    /// <summary>
    /// Time to wait before clearing previous zone memory after being stable
    /// </summary>
    public const double StableZoneClearTimeSeconds = 3.0;
    
    /// <summary>
    /// Timeout for one-way zone restrictions
    /// </summary>
    public const double OneWayTimeoutSeconds = 5.0;
    
    /// <summary>
    /// Distance threshold for zone re-entry (must move this far into zone to allow return)
    /// </summary>
    public const float ZoneReentryThresholdUnits = 100f;
    
    /// <summary>
    /// Distance within which zone changes are considered fluctuations and ignored
    /// </summary>
    public const float ZoneBoundaryFluctuationThresholdUnits = 10f;
    
    /// <summary>
    /// Zone grid size in world units (should match ActionServer configuration)
    /// </summary>
    public const float ZoneGridSizeUnits = 500f;
    
    /// <summary>
    /// Distance from zone edge to trigger proactive connection establishment
    /// </summary>
    public const float ProactiveConnectionDistanceUnits = 50f;
    
    /// <summary>
    /// Interval for zone boundary checks when near edges
    /// </summary>
    public const double ZoneBoundaryCheckIntervalSeconds = 1.0;
}