using System.Collections.Generic;
using System.Threading.Tasks;

namespace Forkleans.Runtime.MembershipService
{
    internal interface IMembershipGossiper
    {
        Task GossipToRemoteSilos(
            List<SiloAddress> gossipPartners,
            MembershipTableSnapshot snapshot,
            SiloAddress updatedSilo,
            SiloStatus updatedStatus);
    }
}
