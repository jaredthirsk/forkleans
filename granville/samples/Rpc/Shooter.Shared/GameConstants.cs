namespace Shooter.Shared;

/// <summary>
/// Central location for game constants and configuration values.
/// </summary>
public static class GameConstants
{
    /// <summary>
    /// Size of each grid square in world units.
    /// </summary>
    public const int GridSize = 1000;
    
    /// <summary>
    /// Distance from zone edge where adjacent zone content becomes visible.
    /// This controls how much of an adjacent zone is visible when near a border.
    /// </summary>
    public const float ZonePeekDistance = 200f;
    
    /// <summary>
    /// Opacity for the grayed-out areas of adjacent zones (0-1).
    /// </summary>
    public const float AdjacentZoneDimOpacity = 0.3f;
    
    /// <summary>
    /// Interval in seconds between zone boundary checks on the client.
    /// Reduced from 1.0 to 0.1 seconds for more responsive zone transitions.
    /// </summary>
    public const double ZoneBoundaryCheckInterval = 0.1;
    
    /// <summary>
    /// Interval in milliseconds between zone transfer checks on the server.
    /// Reduced from 500ms to 100ms for faster server-side zone transition detection.
    /// </summary>
    public const int ZoneTransferCheckInterval = 100;
}