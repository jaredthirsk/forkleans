
using Forkleans.Transactions.TestKit.xUnit;
using Xunit.Abstractions;
using Xunit;

namespace Forkleans.Transactions.Tests
{
    [TestCategory("BVT"), TestCategory("Transactions")]
    public class TocFaultTransactionMemoryTests : TocFaultTransactionTestRunnerxUnit, IClassFixture<MemoryTransactionsFixture>
    {
        public TocFaultTransactionMemoryTests(MemoryTransactionsFixture fixture, ITestOutputHelper output)
            : base(fixture.GrainFactory, output)
        {
        }
    }
}
