
namespace Forkleans.Transactions.Abstractions
{
    public interface ITransactionCommitterConfiguration
    {
        string ServiceName { get; }
        string StorageName { get; }
    }
}
