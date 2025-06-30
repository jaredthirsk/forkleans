namespace Granville.Rpc
{
    /// <summary>
    /// Interface for RPC servers that are zone-aware (e.g., game servers managing specific regions).
    /// </summary>
    public interface IZoneAwareRpcServer
    {
        /// <summary>
        /// Gets the zone ID assigned to this server.
        /// </summary>
        /// <returns>The zone ID, or null if the server is not zone-aware.</returns>
        int? GetZoneId();
    }
}