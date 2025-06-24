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
}