using Forkleans.Transactions.TestKit.xUnit;
using Xunit;
using Xunit.Abstractions;
using TestExtensions;

namespace Forkleans.Transactions.Tests
{
    [TestCategory("BVT"), TestCategory("Transactions")]
    public class DisabledTransactionsTests : DisabledTransactionsTestRunnerxUnit, IClassFixture<DefaultClusterFixture>
    {
        public DisabledTransactionsTests(DefaultClusterFixture fixture, ITestOutputHelper output)
            : base(fixture.GrainFactory, output)
        {
        }
    }
}
