
namespace Forkleans.Transactions.Abstractions
{
    public interface ITransactionDataCopier<TData>
    {
        TData DeepCopy(TData original);
    }
}
