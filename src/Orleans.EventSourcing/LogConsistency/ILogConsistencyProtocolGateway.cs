using System.Threading.Tasks;
using Forkleans.EventSourcing;
using Forkleans.Runtime;

namespace Forkleans.SystemTargetInterfaces
{
    /// <summary>
    /// The  protocol gateway is a relay that forwards incoming protocol messages from other clusters
    /// to the appropriate grain in this cluster.
    /// </summary>
    internal interface ILogConsistencyProtocolGateway : ISystemTarget
    {
        Task<ILogConsistencyProtocolMessage> RelayMessage(GrainId id, ILogConsistencyProtocolMessage payload);
    }
}
