using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace Granville.Rpc.Zones
{
    /// <summary>
    /// A zone detection strategy that maps grain types to specific zones.
    /// This can be configured to route certain grain types to specific ActionServers.
    /// </summary>
    public class GrainTypeBasedZoneDetectionStrategy : IZoneDetectionStrategy
    {
        private readonly ILogger<GrainTypeBasedZoneDetectionStrategy> _logger;
        private readonly Dictionary<string, int> _grainTypeToZoneMapping;
        
        public GrainTypeBasedZoneDetectionStrategy(
            ILogger<GrainTypeBasedZoneDetectionStrategy> logger,
            Dictionary<string, int> grainTypeToZoneMapping = null)
        {
            _logger = logger;
            _grainTypeToZoneMapping = grainTypeToZoneMapping ?? new Dictionary<string, int>();
        }
        
        public int? GetZoneId(GrainId grainId)
        {
            var grainType = grainId.Type;
            var grainKey = grainId.Key.ToString();
            
            return GetZoneId(grainType, grainKey);
        }
        
        public int? GetZoneId(GrainType grainType, string grainKey)
        {
            var grainTypeName = grainType.ToString();
            
            // Check if we have a specific mapping for this grain type
            foreach (var mapping in _grainTypeToZoneMapping)
            {
                if (grainTypeName.Contains(mapping.Key))
                {
                    _logger.LogDebug("Grain type {GrainType} mapped to zone {ZoneId}", grainTypeName, mapping.Value);
                    return mapping.Value;
                }
            }
            
            // No specific zone mapping found
            return null;
        }
        
        /// <summary>
        /// Adds a mapping from a grain type pattern to a zone ID.
        /// </summary>
        /// <param name="grainTypePattern">The grain type pattern (substring match)</param>
        /// <param name="zoneId">The zone ID to map to</param>
        public void AddMapping(string grainTypePattern, int zoneId)
        {
            _grainTypeToZoneMapping[grainTypePattern] = zoneId;
            _logger.LogInformation("Added grain type mapping: {Pattern} -> Zone {ZoneId}", grainTypePattern, zoneId);
        }
        
        /// <summary>
        /// Removes a grain type mapping.
        /// </summary>
        /// <param name="grainTypePattern">The grain type pattern to remove</param>
        public void RemoveMapping(string grainTypePattern)
        {
            if (_grainTypeToZoneMapping.Remove(grainTypePattern))
            {
                _logger.LogInformation("Removed grain type mapping for pattern: {Pattern}", grainTypePattern);
            }
        }
    }
}