using System;
using System.Threading;

namespace Forkleans.Transactions.State
{
    internal interface IActivationLifetime
    {
        CancellationToken OnDeactivating { get; }

        IDisposable BlockDeactivation();
    }
}
