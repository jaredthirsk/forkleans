using Forkleans.Runtime;

namespace Forkleans
{
    /// <summary>
    /// A marker interface for grain observers.
    /// Observers are used to receive notifications from grains; that is, they represent the subscriber side of a 
    /// publisher/subscriber interface.
    /// </summary>
    public interface IGrainObserver : IAddressable
    {
    }
}
