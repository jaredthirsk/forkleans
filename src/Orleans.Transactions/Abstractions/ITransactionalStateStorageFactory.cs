
using Forkleans.Runtime;

namespace Forkleans.Transactions.Abstractions
{
    public interface ITransactionalStateStorageFactory
    {
        ITransactionalStateStorage<TState> Create<TState>(string stateName, IGrainContext context) where TState : class, new();
    }
}
