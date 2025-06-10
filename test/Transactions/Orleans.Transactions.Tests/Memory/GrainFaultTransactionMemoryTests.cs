using Forkleans.Transactions.TestKit.xUnit;
using Xunit.Abstractions;
using Xunit;

namespace Forkleans.Transactions.Tests
{
    [TestCategory("BVT"), TestCategory("Transactions")]
    public class GrainFaultTransactionMemoryTests : GrainFaultTransactionTestRunnerxUnit, IClassFixture<MemoryTransactionsFixture>
    {
        public GrainFaultTransactionMemoryTests(MemoryTransactionsFixture fixture, ITestOutputHelper output)
            : base(fixture.GrainFactory, output)
        {
        }
    }
}
