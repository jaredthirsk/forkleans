using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Forkleans.Runtime;
using Forkleans.Runtime.MembershipService;

namespace Forkleans.Configuration
{
    internal class DevelopmentClusterMembershipOptionsValidator : IConfigurationValidator
    {
        private readonly DevelopmentClusterMembershipOptions options;
        private readonly IMembershipTable membershipTable;

        public DevelopmentClusterMembershipOptionsValidator(IOptions<DevelopmentClusterMembershipOptions> options, IServiceProvider serviceProvider)
        {
            this.options = options.Value;
            this.membershipTable = serviceProvider.GetService<IMembershipTable>();
        }

        public void ValidateConfiguration()
        {
            if (this.membershipTable is SystemTargetBasedMembershipTable && this.options.PrimarySiloEndpoint is null)
            {
                throw new ForkleansConfigurationException("Development clustering is enabled but no value is specified ");
            }
        }
    }
}