using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace Granville.Rpc
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