namespace Forkleans.Transactions.Abstractions
{
    public interface ITransactionCommitterFactory
    {
        ITransactionCommitter<TService> Create<TService>(ITransactionCommitterConfiguration config) where TService : class;
    }
}
