using Forkleans;
using System;

namespace Forkleans.Rpc
{
    /// <summary>
    /// The observable RPC server lifecycle.
    /// </summary>
    public interface IRpcServerLifecycle : ILifecycleObservable
    {
    }

    /// <summary>
    /// The RPC server lifecycle subject.
    /// </summary>
    public interface IRpcServerLifecycleSubject : IRpcServerLifecycle, ILifecycleSubject
    {
    }

    /// <summary>
    /// Lifecycle stages for RPC server.
    /// </summary>
    public static class RpcServerLifecycleStage
    {
        // Initialization stages
        public const int First = ServiceLifecycleStage.First;
        public const int RuntimeInitialize = ServiceLifecycleStage.RuntimeInitialize;
        
        // RPC-specific stages
        public const int TransportInit = 1000;
        public const int ActivatorsInit = 1100;
        public const int RuntimeStart = 2000;
        public const int TransportStart = 2100;
        public const int ApplicationStart = 3000;
        
        // Shutdown stages
        public const int ApplicationStop = ServiceLifecycleStage.Last - 3000;
        public const int TransportStop = ServiceLifecycleStage.Last - 2100;
        public const int RuntimeStop = ServiceLifecycleStage.Last - 2000;
        public const int Last = ServiceLifecycleStage.Last;
    }
}