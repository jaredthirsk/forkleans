using System.Collections.Generic;
using System.Threading.Tasks;
using Forkleans.Concurrency;
using Forkleans.Transactions.Abstractions;
using Forkleans.Transactions.TestKit.Correctnesss;

namespace Forkleans.Transactions.TestKit
{
    public interface ITransactionCoordinatorGrain : IGrainWithGuidKey
    {
        [Transaction(TransactionOption.Create)]
        Task MultiGrainSet(List<ITransactionTestGrain> grains, int numberToAdd);

        [Transaction(TransactionOption.Create)]
        Task MultiGrainAdd(List<ITransactionTestGrain> grains, int numberToAdd);

        [Transaction(TransactionOption.Create)]
        Task MultiGrainDouble(List<ITransactionTestGrain> grains);

        [Transaction(TransactionOption.Create)]
        Task MultiGrainDoubleByRWRW(List<ITransactionTestGrain> grains, int numberToAdd);

        [Transaction(TransactionOption.Create)]
        Task MultiGrainDoubleByWRWR(List<ITransactionTestGrain> grains, int numberToAdd);

        [Transaction(TransactionOption.Create)]
        Task OrphanCallTransaction(ITransactionTestGrain grain);

        [Transaction(TransactionOption.Create)]
        Task AddAndThrow(ITransactionTestGrain grain, int numberToAdd);

        [Transaction(TransactionOption.Create)]
        Task MultiGrainAddAndThrow(List<ITransactionTestGrain> grain, List<ITransactionTestGrain> grains, int numberToAdd);

        [Transaction(TransactionOption.Create)]
        Task MultiGrainSetBit(List<ITransactionalBitArrayGrain> grains, int bitIndex);

        [Transaction(TransactionOption.Create)]
        Task MultiGrainAdd(ITransactionCommitterTestGrain committer, ITransactionCommitOperation<IRemoteCommitService> operation, List<ITransactionTestGrain> grains, int numberToAdd);

        [Transaction(TransactionOption.Create)]
        [ReadOnly]
        Task UpdateViolated(ITransactionTestGrain grains, int numberToAdd);
    }
}
