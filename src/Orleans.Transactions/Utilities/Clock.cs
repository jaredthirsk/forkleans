using System;

namespace Forkleans.Transactions
{
    public class Clock : IClock
    {
        public DateTime UtcNow()
        {
            return DateTime.UtcNow;
        }
    }
}
