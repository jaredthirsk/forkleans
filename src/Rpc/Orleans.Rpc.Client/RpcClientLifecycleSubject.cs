using Microsoft.Extensions.Logging;
using Forkleans.Runtime;

namespace Forkleans.Rpc
{
    /// <summary>
    /// Lifecycle subject for RPC client.
    /// </summary>
    internal sealed class RpcClientLifecycleSubject : LifecycleSubject, IClusterClientLifecycle
    {
        public RpcClientLifecycleSubject(ILogger<RpcClientLifecycleSubject> logger) : base(logger)
        {
        }
    }
}