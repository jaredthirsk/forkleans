using System;

namespace Forkleans.Transactions
{
    /// <summary>
    /// System clock abstraction
    /// </summary>
    public interface IClock
    {
        /// <summary>
        /// Current time in utc
        /// </summary>
        /// <returns></returns>
        DateTime UtcNow();
    }
}
