using Microsoft.Extensions.Logging;
using Forkleans.Runtime;

namespace Forkleans.Rpc
{
    /// <summary>
    /// Default implementation of IRpcServerLifecycleSubject.
    /// </summary>
    internal class RpcServerLifecycleSubject : LifecycleSubject, IRpcServerLifecycleSubject
    {
        public RpcServerLifecycleSubject(ILogger<RpcServerLifecycleSubject> logger) : base(logger)
        {
        }
    }
}