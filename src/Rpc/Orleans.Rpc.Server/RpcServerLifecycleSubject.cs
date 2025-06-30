using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace Granville.Rpc
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