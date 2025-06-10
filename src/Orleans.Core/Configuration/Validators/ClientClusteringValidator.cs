using System;

using Microsoft.Extensions.DependencyInjection;
using Forkleans.Messaging;
using Forkleans.Runtime;

namespace Forkleans.Configuration.Validators
{
    /// <summary>
    /// Validator for client-side clustering.
    /// </summary>
    internal class ClientClusteringValidator : IConfigurationValidator
    {
        /// <summary>
        /// The error message displayed when clustering is misconfigured.
        /// </summary>
        internal const string ClusteringNotConfigured =
            "Clustering has not been configured. Configure clustering using one of the clustering packages, such as:"
            + "\n  * Microsoft.Forkleans.Clustering.AzureStorage"
            + "\n  * Microsoft.Forkleans.Clustering.AdoNet for ADO.NET systems such as SQL Server, MySQL, PostgreSQL, and Oracle"
            + "\n  * Microsoft.Forkleans.Clustering.DynamoDB"
            + "\n  * Microsoft.Forkleans.Clustering.Consul"
            + "\n  * Microsoft.Forkleans.Clustering.ZooKeeper"
            + "\n  * Others, see: https://www.nuget.org/packages?q=Microsoft.Forkleans.Clustering.";

        /// <summary>
        /// The service provider.
        /// </summary>
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClientClusteringValidator"/> class.
        /// </summary>
        /// <param name="serviceProvider">
        /// The service provider.
        /// </param>
        public ClientClusteringValidator(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <inheritdoc />
        public void ValidateConfiguration()
        {
            var gatewayProvider = _serviceProvider.GetService<IGatewayListProvider>();
            if (gatewayProvider == null)
            {
                throw new OrleansConfigurationException(ClusteringNotConfigured);
            }
        }
    }
}
