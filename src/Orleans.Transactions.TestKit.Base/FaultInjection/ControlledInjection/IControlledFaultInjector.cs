namespace Forkleans.Transactions.TestKit
{
    public interface IControlledTransactionFaultInjector : ITransactionFaultInjector
    {
        bool InjectBeforeStore { get; set; }
        bool InjectAfterStore { get; set; }
    }
}
