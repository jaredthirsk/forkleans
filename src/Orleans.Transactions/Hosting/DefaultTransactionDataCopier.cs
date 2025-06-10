using Forkleans.Serialization;
using Forkleans.Transactions.Abstractions;

namespace Forkleans.Transactions
{
    public class DefaultTransactionDataCopier<TData> : ITransactionDataCopier<TData>
    {
        private readonly DeepCopier<TData> deepCopier;

        public DefaultTransactionDataCopier(DeepCopier<TData> deepCopier)
        {
            this.deepCopier = deepCopier;
        }

        public TData DeepCopy(TData original)
        {
            return (TData)this.deepCopier.Copy(original);
        }
    }
}
