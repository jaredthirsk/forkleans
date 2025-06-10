
using Forkleans.Transactions.TestKit.xUnit;
using Xunit;
using Xunit.Abstractions;

namespace Forkleans.Transactions.AzureStorage.Tests
{
    [TestCategory("AzureStorage"), TestCategory("Transactions"), TestCategory("Functional")]
    public class TocFaultTransactionTests : TocFaultTransactionTestRunnerxUnit, IClassFixture<TestFixture>
    {
        public TocFaultTransactionTests(TestFixture fixture, ITestOutputHelper output)
            : base(fixture.GrainFactory, output)
        {
            fixture.EnsurePreconditionsMet();
        }
    }
}
