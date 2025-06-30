namespace Shooter.Shared.Models;

/// <summary>
/// Utilities for zone management and ID calculation.
/// </summary>
public static class ZoneUtils
{
    /// <summary>
    /// Maximum grid coordinate value (used for zone ID calculation).
    /// </summary>
    public const int MaxGridCoordinate = 1000;

    /// <summary>
    /// Converts a grid square to a unique zone ID.
    /// Uses formula: zoneId = x + (y * MaxGridCoordinate)
    /// </summary>
    public static int GetZoneId(GridSquare square)
    {
        return GetZoneId(square.X, square.Y);
    }

    /// <summary>
    /// Converts grid coordinates to a unique zone ID.
    /// </summary>
    public static int GetZoneId(int x, int y)
    {
        // Ensure positive zone IDs even for negative coordinates
        var adjustedX = x + MaxGridCoordinate;
        var adjustedY = y + MaxGridCoordinate;
        return adjustedX + (adjustedY * (MaxGridCoordinate * 2));
    }

    /// <summary>
    /// Converts a zone ID back to grid coordinates.
    /// </summary>
    public static GridSquare GetGridSquare(int zoneId)
    {
        var width = MaxGridCoordinate * 2;
        var adjustedY = zoneId / width;
        var adjustedX = zoneId % width;
        
        var x = adjustedX - MaxGridCoordinate;
        var y = adjustedY - MaxGridCoordinate;
        
        return new GridSquare(x, y);
    }

    /// <summary>
    /// Gets the distance threshold for pre-connecting to neighboring zones.
    /// </summary>
    public const float ZonePreconnectDistance = 200f;

    /// <summary>
    /// Checks if a position is close enough to a zone border to warrant pre-connection.
    /// </summary>
    public static bool IsNearZoneBorder(Vector2 position, out GridSquare neighborZone)
    {
        var currentSquare = GridSquare.FromPosition(position);
        var (min, max) = currentSquare.GetBounds();
        
        // Check distance to each border
        var distToLeft = position.X - min.X;
        var distToRight = max.X - position.X;
        var distToBottom = position.Y - min.Y;
        var distToTop = max.Y - position.Y;
        
        // Find the closest border
        var minDist = Math.Min(Math.Min(distToLeft, distToRight), Math.Min(distToBottom, distToTop));
        
        if (minDist <= ZonePreconnectDistance)
        {
            // Determine which neighbor zone we're approaching
            if (minDist == distToLeft)
                neighborZone = new GridSquare(currentSquare.X - 1, currentSquare.Y);
            else if (minDist == distToRight)
                neighborZone = new GridSquare(currentSquare.X + 1, currentSquare.Y);
            else if (minDist == distToBottom)
                neighborZone = new GridSquare(currentSquare.X, currentSquare.Y - 1);
            else
                neighborZone = new GridSquare(currentSquare.X, currentSquare.Y + 1);
                
            return true;
        }
        
        neighborZone = new GridSquare(0, 0);
        return false;
    }

    /// <summary>
    /// Gets all neighboring zones that are within pre-connect distance.
    /// </summary>
    public static List<GridSquare> GetNearbyZones(Vector2 position)
    {
        var nearbyZones = new List<GridSquare>();
        var currentSquare = GridSquare.FromPosition(position);
        var (min, max) = currentSquare.GetBounds();
        
        // Check all four borders
        if (position.X - min.X <= ZonePreconnectDistance)
            nearbyZones.Add(new GridSquare(currentSquare.X - 1, currentSquare.Y));
            
        if (max.X - position.X <= ZonePreconnectDistance)
            nearbyZones.Add(new GridSquare(currentSquare.X + 1, currentSquare.Y));
            
        if (position.Y - min.Y <= ZonePreconnectDistance)
            nearbyZones.Add(new GridSquare(currentSquare.X, currentSquare.Y - 1));
            
        if (max.Y - position.Y <= ZonePreconnectDistance)
            nearbyZones.Add(new GridSquare(currentSquare.X, currentSquare.Y + 1));
            
        // Check corners if close to both borders
        if (position.X - min.X <= ZonePreconnectDistance && position.Y - min.Y <= ZonePreconnectDistance)
            nearbyZones.Add(new GridSquare(currentSquare.X - 1, currentSquare.Y - 1));
            
        if (max.X - position.X <= ZonePreconnectDistance && position.Y - min.Y <= ZonePreconnectDistance)
            nearbyZones.Add(new GridSquare(currentSquare.X + 1, currentSquare.Y - 1));
            
        if (position.X - min.X <= ZonePreconnectDistance && max.Y - position.Y <= ZonePreconnectDistance)
            nearbyZones.Add(new GridSquare(currentSquare.X - 1, currentSquare.Y + 1));
            
        if (max.X - position.X <= ZonePreconnectDistance && max.Y - position.Y <= ZonePreconnectDistance)
            nearbyZones.Add(new GridSquare(currentSquare.X + 1, currentSquare.Y + 1));
            
        return nearbyZones;
    }
}