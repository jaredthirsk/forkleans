using Forkleans.Transactions.TestKit.xUnit;
using Xunit.Abstractions;
using Xunit;

namespace Forkleans.Transactions.Tests
{
    [TestCategory("Transactions-dev")]
    public class ConsistencyTests : ConsistencyTransactionTestRunnerxUnit, IClassFixture<MemoryTransactionsFixture>
    {
        public ConsistencyTests(MemoryTransactionsFixture fixture, ITestOutputHelper output)
            : base(fixture.GrainFactory, output)
        {
        }

        protected override bool StorageErrorInjectionActive => false;
        protected override bool StorageAdaptorHasLimitedCommitSpace => false;

    }

}
