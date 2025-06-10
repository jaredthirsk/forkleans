using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Forkleans.Configuration;
using Forkleans.Timers.Internal;
using Forkleans.Transactions.Abstractions;
using Forkleans.Transactions.State;

namespace Forkleans.Transactions.TOC
{
    internal class TocTransactionQueue<TService> : TransactionQueue<TransactionCommitter<TService>.OperationState>
                where TService : class
    {
        private readonly TService service;

        public TocTransactionQueue(
            TService service,
            IOptions<TransactionalStateOptions> options,
            ParticipantId resource,
            Action deactivate,
            ITransactionalStateStorage<TransactionCommitter<TService>.OperationState> storage,
            IClock clock,
            ILogger logger,
            ITimerManager timerManager,
            IActivationLifetime activationLifetime)
            : base(options, resource, deactivate, storage, clock, logger, timerManager, activationLifetime)
        {
            this.service = service;
        }

        protected override void OnLocalCommit(TransactionRecord<TransactionCommitter<TService>.OperationState> entry)
        {
            base.storageBatch.AddStorePreCondition(() => entry.State.Operation.Commit(entry.TransactionId, this.service));
            base.OnLocalCommit(entry);
        }
    }
}
