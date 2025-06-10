using Xunit;
using Xunit.Abstractions;
using Forkleans.Runtime.Development;
using TestExtensions;
using TestExtensions.Runners;

namespace DefaultCluster.Tests
{
    [TestCategory("BVT"), TestCategory("Lease")]
    public class GoldenPathInMemoryLeaseProviderTests : GoldenPathLeaseProviderTestRunner, IClassFixture<GoldenPathInMemoryLeaseProviderTests.Fixture>
    {
        public GoldenPathInMemoryLeaseProviderTests(Fixture fixture, ITestOutputHelper output)
            : base(new InMemoryLeaseProvider(fixture.GrainFactory), output)
        {
        }

        public class Fixture : BaseTestClusterFixture
        {
        }
    }
}
