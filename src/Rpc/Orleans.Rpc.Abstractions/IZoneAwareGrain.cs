namespace Granville.Rpc
{
    /// <summary>
    /// Marker interface for grains that can be addressed by zone ID.
    /// Grains implementing this interface can be invoked on specific zones.
    /// Grains without this interface are local-server only.
    /// </summary>
    public interface IZoneAwareGrain
    {
    }
}