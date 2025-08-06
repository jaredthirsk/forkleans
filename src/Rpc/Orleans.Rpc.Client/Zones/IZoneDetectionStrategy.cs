using Orleans.Runtime;

namespace Granville.Rpc.Zones
{
    /// <summary>
    /// Strategy for determining which zone a grain belongs to based on its GrainId.
    /// This is used to route RPC calls to the appropriate ActionServer (RPC Server).
    /// </summary>
    public interface IZoneDetectionStrategy
    {
        /// <summary>
        /// Determines the zone ID for a given grain.
        /// </summary>
        /// <param name="grainId">The grain identifier.</param>
        /// <returns>The zone ID that should handle this grain, or null if no specific zone is required.</returns>
        int? GetZoneId(GrainId grainId);
        
        /// <summary>
        /// Gets the zone ID for a specific grain type and key.
        /// </summary>
        /// <param name="grainType">The grain type.</param>
        /// <param name="grainKey">The grain's primary key as a string.</param>
        /// <returns>The zone ID that should handle this grain, or null if no specific zone is required.</returns>
        int? GetZoneId(GrainType grainType, string grainKey);
    }
}