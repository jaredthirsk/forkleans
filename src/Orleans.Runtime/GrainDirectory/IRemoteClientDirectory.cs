using System.Collections.Immutable;
using System.Threading.Tasks;

namespace Forkleans.Runtime.GrainDirectory
{
    internal interface IRemoteClientDirectory : ISystemTarget
    {
        Task OnUpdateClientRoutes(ImmutableDictionary<SiloAddress, (ImmutableHashSet<GrainId> ConnectedClients, long Version)> update);
        Task<ImmutableDictionary<SiloAddress, (ImmutableHashSet<GrainId> ConnectedClients, long Version)>> GetClientRoutes(ImmutableDictionary<SiloAddress, long> knownRoutes);
    }
}
