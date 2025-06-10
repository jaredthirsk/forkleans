using Forkleans.Versions.Compatibility;

namespace Forkleans.Runtime.Versions.Compatibility
{
    internal class BackwardCompatilityDirector : ICompatibilityDirector
    {
        public bool IsCompatible(ushort requestedVersion, ushort currentVersion)
        {
            return requestedVersion <= currentVersion;
        }
    }
}
