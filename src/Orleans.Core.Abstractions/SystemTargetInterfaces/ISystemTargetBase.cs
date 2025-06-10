using Forkleans.Runtime;

namespace Forkleans
{
    /// <summary>
    /// Internal interface implemented by the SystemTarget base class that enables generation of grain references for system targets.
    /// </summary>
    internal interface ISystemTargetBase : IGrainContext
    {
        /// <summary>
        /// Gets the address of the server which this system target is activated on.
        /// </summary>
        SiloAddress Silo { get; }
    }
}