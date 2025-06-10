using Microsoft.Extensions.Options;
using Forkleans.Runtime;
using Forkleans.Runtime.MembershipService;

namespace Forkleans.Configuration
{
    /// <summary>
    /// Validates <see cref="AdoNetClusteringSiloOptions"/> configuration.
    /// </summary>
    public class AdoNetClusteringSiloOptionsValidator : IConfigurationValidator
    {
        private readonly AdoNetClusteringSiloOptions options;

        public AdoNetClusteringSiloOptionsValidator(IOptions<AdoNetClusteringSiloOptions> options)
        {
            this.options = options.Value;
        }

        /// <inheritdoc />
        public void ValidateConfiguration()
        {
            if (string.IsNullOrWhiteSpace(this.options.Invariant))
            {
                throw new OrleansConfigurationException($"Invalid {nameof(AdoNetClusteringSiloOptions)} values for {nameof(AdoNetClusteringTable)}. {nameof(options.Invariant)} is required.");
            }

            if (string.IsNullOrWhiteSpace(this.options.ConnectionString))
            {
                throw new OrleansConfigurationException($"Invalid {nameof(AdoNetClusteringSiloOptions)} values for {nameof(AdoNetClusteringTable)}. {nameof(options.ConnectionString)} is required.");
            }
        }
    }
}