using Forkleans.Versions.Compatibility;

namespace Forkleans.Runtime.Versions.Compatibility
{
    internal class AllVersionsCompatibilityDirector : ICompatibilityDirector
    {
        public bool IsCompatible(ushort requestedVersion, ushort currentVersion)
        {
            return true;
        }
    }
}