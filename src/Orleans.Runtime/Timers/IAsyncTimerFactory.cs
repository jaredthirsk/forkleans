using System;

namespace Forkleans.Runtime
{
    internal interface IAsyncTimerFactory
    {
        IAsyncTimer Create(TimeSpan period, string name);
    }
}
