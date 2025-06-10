using System.Threading.Tasks;
using Forkleans.Concurrency;

namespace Forkleans.Runtime
{
    internal interface IDeploymentLoadPublisher : ISystemTarget
    {
        [OneWay]
        Task UpdateRuntimeStatistics(SiloAddress siloAddress, SiloRuntimeStatistics siloStats);
    }
}
