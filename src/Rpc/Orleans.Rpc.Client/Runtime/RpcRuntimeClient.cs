using System;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.CodeGeneration;
using Orleans.Runtime;
using Orleans.Serialization.Invocation;

namespace Granville.Rpc.Runtime
{
    /// <summary>
    /// RPC-specific implementation of IRuntimeClient that integrates with RPC transport.
    /// This is a lightweight wrapper that delegates most functionality to the Orleans runtime client
    /// while providing RPC-specific overrides where needed.
    /// </summary>
    internal class RpcRuntimeClient : IRuntimeClient
    {
        private readonly IRuntimeClient _orleansRuntimeClient;
        private readonly IGrainReferenceRuntime _rpcGrainReferenceRuntime;

        public RpcRuntimeClient(
            IRuntimeClient orleansRuntimeClient,
            IGrainReferenceRuntime rpcGrainReferenceRuntime)
        {
            _orleansRuntimeClient = orleansRuntimeClient ?? throw new ArgumentNullException(nameof(orleansRuntimeClient));
            _rpcGrainReferenceRuntime = rpcGrainReferenceRuntime ?? throw new ArgumentNullException(nameof(rpcGrainReferenceRuntime));
        }

        // Delegate most properties to Orleans runtime client
        public TimeProvider TimeProvider => _orleansRuntimeClient.TimeProvider;
        public IInternalGrainFactory InternalGrainFactory => _orleansRuntimeClient.InternalGrainFactory;
        public string CurrentActivationIdentity => _orleansRuntimeClient.CurrentActivationIdentity;
        public IServiceProvider ServiceProvider => _orleansRuntimeClient.ServiceProvider;

        // Use our custom RPC grain reference runtime
        public IGrainReferenceRuntime GrainReferenceRuntime => _rpcGrainReferenceRuntime;

        // Delegate methods to Orleans runtime client
        public TimeSpan GetResponseTimeout() => _orleansRuntimeClient.GetResponseTimeout();
        public void SetResponseTimeout(TimeSpan timeout) => _orleansRuntimeClient.SetResponseTimeout(timeout);

        public void SendRequest(GrainReference target, IInvokable request, IResponseCompletionSource context, InvokeMethodOptions options)
        {
            // For RPC grains, this should be handled by RpcGrainReferenceRuntime
            // For Orleans grains, delegate to Orleans runtime
            if (target is RpcGrainReference)
            {
                throw new InvalidOperationException(
                    "RPC grain requests should be handled by RpcGrainReferenceRuntime.InvokeMethodAsync");
            }

            _orleansRuntimeClient.SendRequest(target, request, context, options);
        }

        public void SendResponse(Message request, Response response)
        {
            _orleansRuntimeClient.SendResponse(request, response);
        }

        public void ReceiveResponse(Message message)
        {
            _orleansRuntimeClient.ReceiveResponse(message);
        }

        public IAddressable CreateObjectReference(IAddressable obj)
        {
            return _orleansRuntimeClient.CreateObjectReference(obj);
        }

        public void DeleteObjectReference(IAddressable obj)
        {
            _orleansRuntimeClient.DeleteObjectReference(obj);
        }

        public void BreakOutstandingMessagesToSilo(SiloAddress deadSilo)
        {
            _orleansRuntimeClient.BreakOutstandingMessagesToSilo(deadSilo);
        }

        public int GetRunningRequestsCount(GrainInterfaceType grainInterfaceType)
        {
            return _orleansRuntimeClient.GetRunningRequestsCount(grainInterfaceType);
        }
    }
}