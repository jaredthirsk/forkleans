namespace Granville.Rpc.Multiplexing
{
    /// <summary>
    /// Marker interface for grains that require zone-aware routing.
    /// </summary>
    public interface IZoneAwareGrain
    {
        // This is a marker interface used by ZoneBasedRoutingStrategy
        // to identify grains that should be routed based on zone information
    }
}