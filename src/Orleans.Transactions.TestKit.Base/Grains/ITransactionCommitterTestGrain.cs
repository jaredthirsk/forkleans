
using Forkleans.Transactions.Abstractions;
using System.Threading.Tasks;

namespace Forkleans.Transactions.TestKit
{
    public interface ITransactionCommitterTestGrain : IGrainWithGuidKey
    {
        [Transaction(TransactionOption.Join)]
        Task Commit(ITransactionCommitOperation<IRemoteCommitService> operation);
    }
}
