using Forkleans.Versions.Compatibility;

namespace Forkleans.Runtime.Versions.Compatibility
{
    internal class StrictVersionCompatibilityDirector : ICompatibilityDirector
    {
        public bool IsCompatible(ushort requestedVersion, ushort currentVersion)
        {
            return requestedVersion == currentVersion;
        }
    }
}