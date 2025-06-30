using Orleans;

namespace Granville.Rpc
{
    /// <summary>
    /// Marker interface for RPC grain interfaces.
    /// Grain interfaces that extend this will use RPC transport instead of standard Orleans messaging.
    /// </summary>
    public interface IRpcGrainInterface : IGrainWithIntegerKey
    {
    }

    /// <summary>
    /// Marker interface for RPC grain interfaces with string keys.
    /// </summary>
    public interface IRpcGrainInterfaceWithStringKey : IGrainWithStringKey
    {
    }

    /// <summary>
    /// Marker interface for RPC grain interfaces with GUID keys.
    /// </summary>
    public interface IRpcGrainInterfaceWithGuidKey : IGrainWithGuidKey
    {
    }

    /// <summary>
    /// Marker interface for RPC grain interfaces with integer compound keys.
    /// </summary>
    public interface IRpcGrainInterfaceWithIntegerCompoundKey : IGrainWithIntegerCompoundKey
    {
    }

    /// <summary>
    /// Marker interface for RPC grain interfaces with GUID compound keys.
    /// </summary>
    public interface IRpcGrainInterfaceWithGuidCompoundKey : IGrainWithGuidCompoundKey
    {
    }
}