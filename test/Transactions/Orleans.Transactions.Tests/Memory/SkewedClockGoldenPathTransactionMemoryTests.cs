using Forkleans.Transactions.TestKit.xUnit;
using Xunit.Abstractions;
using Xunit;

namespace Forkleans.Transactions.Tests
{
    [TestCategory("BVT"), TestCategory("Transactions")]
    public class SkewedClockGoldenPathTransactionMemoryTests : GoldenPathTransactionTestRunnerxUnit, IClassFixture<SkewedClockMemoryTransactionsFixture>
    {
        public SkewedClockGoldenPathTransactionMemoryTests(SkewedClockMemoryTransactionsFixture fixture, ITestOutputHelper output)
            : base(fixture.GrainFactory, output)
        {
        }
    }
}
