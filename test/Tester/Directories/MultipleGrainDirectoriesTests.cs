using Forkleans.Internal;
using Forkleans.Runtime.Placement;
using Forkleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces.Directories;
using Xunit;

namespace Tester.Directories
{
    public abstract class MultipleGrainDirectoriesTests : TestClusterPerTest
    {
        protected override void ConfigureTestCluster(TestClusterBuilder builder)
        {
            builder.Options.InitialSilosCount = 2;
        }

        [SkippableFact, TestCategory("Directory"), TestCategory("Functional")]
        public async Task PingGrain()
        {
            var grainOnPrimary = await GetGrainOnPrimary().WaitAsync(TimeSpan.FromSeconds(5));
            var grainOnSecondary = await GetGrainOnSecondary().WaitAsync(TimeSpan.FromSeconds(5));

            // Setup
            var primaryCounter = await grainOnPrimary.Ping();
            var secondaryCounter = await grainOnSecondary.Ping();

            // Each silo see the activation on the other silo
            Assert.Equal(++primaryCounter, await grainOnSecondary.ProxyPing(grainOnPrimary));
            Assert.Equal(++secondaryCounter, await grainOnPrimary.ProxyPing(grainOnSecondary));

            await Task.Delay(5000);

            // Shutdown the secondary silo
            await this.HostedCluster.StopSecondarySilosAsync();

            // Activation on the primary silo should still be there, another activation should be
            // created for the other one
            Assert.Equal(++primaryCounter, await grainOnPrimary.Ping());
            Assert.Equal(1, await grainOnSecondary.Ping());
        }

        private async Task<ICustomDirectoryGrain> GetGrainOnPrimary()
        {
            while (true)
            {
                RequestContext.Set(IPlacementDirector.PlacementHintKey, HostedCluster.Primary.SiloAddress);
                var grain = this.GrainFactory.GetGrain<ICustomDirectoryGrain>(Guid.NewGuid());
                var instanceId = await grain.GetRuntimeInstanceId();
                if (instanceId.Contains(HostedCluster.Primary.SiloAddress.Endpoint.ToString()))
                    return grain;
            }
        }

        private async Task<ICustomDirectoryGrain> GetGrainOnSecondary()
        {
            while (true)
            {
                RequestContext.Set(IPlacementDirector.PlacementHintKey, HostedCluster.SecondarySilos[0].SiloAddress);
                var grain = this.GrainFactory.GetGrain<ICustomDirectoryGrain>(Guid.NewGuid());
                var instanceId = await grain.GetRuntimeInstanceId();
                if (instanceId.Contains(HostedCluster.SecondarySilos[0].SiloAddress.Endpoint.ToString()))
                    return grain;
            }
        }
    }
}
