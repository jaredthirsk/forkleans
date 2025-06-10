
using Forkleans.Versions.Compatibility;
using Forkleans.Versions.Selector;

namespace Forkleans.Configuration
{
    /// <summary>
    /// Versioning options govern grain implementation selection in heterogeneous deployments.
    /// </summary>
    public class GrainVersioningOptions
    {
        /// <summary>
        /// Gets or sets the name of the default strategy used to determine grain compatibility in heterogeneous deployments.
        /// </summary>
        /// <value>The <see cref="BackwardCompatible"/> strategy is used by default.</value>
        public string DefaultCompatibilityStrategy { get; set; } = nameof(BackwardCompatible);

        /// <summary>
        /// Gets or sets the name of the default strategy for selecting grain versions in heterogeneous deployments.
        /// </summary>
        /// <value>The <see cref="AllCompatibleVersions"/> strategy is used by default.</value>
        public string DefaultVersionSelectorStrategy { get; set; } = nameof(AllCompatibleVersions);
    }
}
