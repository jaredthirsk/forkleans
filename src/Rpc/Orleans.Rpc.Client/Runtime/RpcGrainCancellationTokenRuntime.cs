using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Forkleans.Runtime;

namespace Forkleans.Rpc
{
    /// <summary>
    /// Simplified implementation of grain cancellation token runtime for RPC mode.
    /// Since RPC mode doesn't support distributed cancellation, this is a stub implementation.
    /// </summary>
    internal class RpcGrainCancellationTokenRuntime
    {
        public Task Cancel(Guid id, CancellationTokenSource tokenSource, ConcurrentDictionary<GrainId, GrainReference> grainReferences)
        {
            // In RPC mode, we just cancel the local token source
            // We don't propagate cancellation to remote grains
            if (!tokenSource.IsCancellationRequested)
            {
                tokenSource.Cancel();
            }
            
            return Task.CompletedTask;
        }
    }
}