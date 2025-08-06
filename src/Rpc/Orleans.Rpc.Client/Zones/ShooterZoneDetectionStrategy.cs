using System;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace Granville.Rpc.Zones
{
    /// <summary>
    /// Zone detection strategy for the Shooter game.
    /// Maps grains to zones based on their spatial position in the game world.
    /// </summary>
    public class ShooterZoneDetectionStrategy : IZoneDetectionStrategy
    {
        private readonly ILogger<ShooterZoneDetectionStrategy> _logger;
        private const int WORLD_SIZE = 1000; // 1000x1000 world
        private const int GRID_SIZE = 100;   // 100x100 grid cells
        private const int ZONES_PER_DIMENSION = WORLD_SIZE / GRID_SIZE; // 10x10 = 100 zones
        
        public ShooterZoneDetectionStrategy(ILogger<ShooterZoneDetectionStrategy> logger)
        {
            _logger = logger;
        }
        
        public int? GetZoneId(GrainId grainId)
        {
            // Extract grain type from the GrainId
            var grainType = grainId.Type;
            var grainKey = grainId.Key.ToString();
            
            return GetZoneId(grainType, grainKey);
        }
        
        public int? GetZoneId(GrainType grainType, string grainKey)
        {
            var grainTypeName = grainType.ToString();
            
            // Check if this is a zone-aware grain
            if (grainTypeName.Contains("IGameRpcGrain") || 
                grainTypeName.Contains("IEnemyRpcGrain") ||
                grainTypeName.Contains("IProjectileRpcGrain"))
            {
                // For game-related grains, the key might contain position information
                // or we might need to query the grain's current position
                // For now, return null to use default routing
                _logger.LogDebug("Zone-aware grain type {GrainType} detected, but position-based routing not yet implemented", grainTypeName);
                return null;
            }
            
            // For player grains, we might extract position from the key or grain state
            if (grainTypeName.Contains("IPlayerGrain"))
            {
                // Players move around, so we'd need to track their current position
                // This would require querying grain state or maintaining a position cache
                _logger.LogDebug("Player grain {GrainKey} detected, dynamic zone assignment needed", grainKey);
                return null;
            }
            
            // Non-zone-aware grains don't need specific zone assignment
            return null;
        }
        
        /// <summary>
        /// Calculates the zone ID based on world coordinates.
        /// </summary>
        /// <param name="x">X coordinate in world space (0-999)</param>
        /// <param name="y">Y coordinate in world space (0-999)</param>
        /// <returns>Zone ID (0-99)</returns>
        public static int CalculateZoneFromPosition(float x, float y)
        {
            // Clamp coordinates to world bounds
            x = Math.Max(0, Math.Min(x, WORLD_SIZE - 1));
            y = Math.Max(0, Math.Min(y, WORLD_SIZE - 1));
            
            // Calculate grid coordinates
            int gridX = (int)(x / GRID_SIZE);
            int gridY = (int)(y / GRID_SIZE);
            
            // Calculate zone ID (row-major order)
            int zoneId = gridY * ZONES_PER_DIMENSION + gridX;
            
            // Add 1000 to match the Shooter's zone numbering (zones start at 1000)
            return 1000 + zoneId;
        }
        
        /// <summary>
        /// Gets the world bounds for a given zone.
        /// </summary>
        /// <param name="zoneId">The zone ID (1000-1099)</param>
        /// <returns>Tuple of (minX, minY, maxX, maxY)</returns>
        public static (float minX, float minY, float maxX, float maxY) GetZoneBounds(int zoneId)
        {
            // Remove the 1000 offset
            int localZoneId = zoneId - 1000;
            
            // Calculate grid coordinates
            int gridX = localZoneId % ZONES_PER_DIMENSION;
            int gridY = localZoneId / ZONES_PER_DIMENSION;
            
            // Calculate world bounds
            float minX = gridX * GRID_SIZE;
            float minY = gridY * GRID_SIZE;
            float maxX = minX + GRID_SIZE;
            float maxY = minY + GRID_SIZE;
            
            return (minX, minY, maxX, maxY);
        }
    }
}