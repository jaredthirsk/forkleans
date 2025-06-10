using System.Linq;
using Forkleans.Versions.Compatibility;
using Forkleans.Versions.Selector;

namespace Forkleans.Runtime.Versions.Selector
{
    internal class AllCompatibleVersionsSelector : IVersionSelector
    {
        public ushort[] GetSuitableVersion(ushort requestedVersion, ushort[] availableVersions, ICompatibilityDirector compatibilityDirector)
        {
            return availableVersions.Where(v => compatibilityDirector.IsCompatible(requestedVersion, v)).ToArray();
        }
    }
}
