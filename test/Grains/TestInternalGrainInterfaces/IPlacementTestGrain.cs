namespace UnitTests.GrainInterfaces
{
    using System.Threading.Tasks;

    using Forkleans;
    using Forkleans.Runtime;

    internal interface IDefaultPlacementGrain : IGrainWithIntegerKey
    {
        Task<PlacementStrategy> GetDefaultPlacement();
    }
}
