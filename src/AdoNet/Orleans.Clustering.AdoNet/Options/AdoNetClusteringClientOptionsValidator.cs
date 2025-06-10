using Microsoft.Extensions.Options;
using Forkleans.Runtime;
using Forkleans.Runtime.MembershipService;

namespace Forkleans.Configuration
{
    /// <summary>
    /// Validates <see cref="AdoNetClusteringClientOptions"/> configuration.
    /// </summary>
    public class AdoNetClusteringClientOptionsValidator : IConfigurationValidator
    {
        private readonly AdoNetClusteringClientOptions options;

        public AdoNetClusteringClientOptionsValidator(IOptions<AdoNetClusteringClientOptions> options)
        {
            this.options = options.Value;
        }

        /// <inheritdoc />
        public void ValidateConfiguration()
        {
            if (string.IsNullOrWhiteSpace(this.options.Invariant))
            {
                throw new OrleansConfigurationException($"Invalid {nameof(AdoNetClusteringClientOptions)} values for {nameof(AdoNetClusteringTable)}. {nameof(options.Invariant)} is required.");
            }

            if (string.IsNullOrWhiteSpace(this.options.ConnectionString))
            {
                throw new OrleansConfigurationException($"Invalid {nameof(AdoNetClusteringClientOptions)} values for {nameof(AdoNetClusteringTable)}. {nameof(options.ConnectionString)} is required.");
            }
        }
    }
}