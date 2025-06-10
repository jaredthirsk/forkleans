using System.Linq;
using Forkleans.Versions.Compatibility;
using Forkleans.Versions.Selector;

namespace Forkleans.Runtime.Versions.Selector
{
    internal sealed class MinimumVersionSelector : IVersionSelector
    {
        public ushort[] GetSuitableVersion(ushort requestedVersion, ushort[] availableVersions, ICompatibilityDirector compatibilityDirector)
        {
            return new[]
            {
                availableVersions.Where(v => compatibilityDirector.IsCompatible(requestedVersion, v)).Min()
            };
        }
    }
}