using Forkleans.Transactions.TestKit.xUnit;
using Xunit.Abstractions;
using Xunit;

namespace Forkleans.Transactions.Tests
{
    [TestCategory("BVT"), TestCategory("Transactions")]
    public class GoldenPathTransactionMemoryTests : GoldenPathTransactionTestRunnerxUnit, IClassFixture<MemoryTransactionsFixture>
    {
        public GoldenPathTransactionMemoryTests(MemoryTransactionsFixture fixture, ITestOutputHelper output)
            : base(fixture.GrainFactory, output)
        {
        }
    }
}
