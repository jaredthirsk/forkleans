using Forkleans.Placement;
using Forkleans.Runtime;
using UnitTests.GrainInterfaces;

namespace UnitTests.Grains
{
    [HashBasedPlacement]
    public class HashBasedBasedPlacementGrain : Grain, IHashBasedPlacementGrain
    {

        public Task<SiloAddress> GetSiloAddress()
        {
            return Task.FromResult(this.Runtime.SiloAddress);
        }
    }
}