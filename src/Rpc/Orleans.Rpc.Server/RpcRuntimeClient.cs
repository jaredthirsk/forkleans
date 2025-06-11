using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Forkleans;
using Forkleans.CodeGeneration;
using Forkleans.Runtime;
using Forkleans.Serialization.Invocation;

namespace Forkleans.Rpc
{
    /// <summary>
    /// Minimal IRuntimeClient implementation for RPC servers.
    /// </summary>
    internal sealed class RpcRuntimeClient : IRuntimeClient
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IGrainReferenceRuntime _grainReferenceRuntime;
        private readonly TimeProvider _timeProvider;
        private readonly string _serverId;
        private readonly ILogger<RpcRuntimeClient> _logger;
        
        public RpcRuntimeClient(
            IServiceProvider serviceProvider,
            IGrainReferenceRuntime grainReferenceRuntime,
            TimeProvider timeProvider,
            ILocalRpcServerDetails serverDetails,
            ILogger<RpcRuntimeClient> logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _grainReferenceRuntime = grainReferenceRuntime ?? throw new ArgumentNullException(nameof(grainReferenceRuntime));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _serverId = serverDetails?.ServerId ?? throw new ArgumentNullException(nameof(serverDetails));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public TimeProvider TimeProvider => _timeProvider;

        public IGrainReferenceRuntime GrainReferenceRuntime => _grainReferenceRuntime;

        public IServiceProvider ServiceProvider => _serviceProvider;

        public IInternalGrainFactory InternalGrainFactory => _serviceProvider.GetRequiredService<RpcGrainFactory>();

        public string CurrentActivationIdentity => _serverId;

        public TimeSpan GetResponseTimeout() => TimeSpan.FromSeconds(30);

        public void SetResponseTimeout(TimeSpan timeout)
        {
            // Not implemented for RPC
        }

        public void SendRequest(GrainReference target, IInvokable request, IResponseCompletionSource context, InvokeMethodOptions options)
        {
            throw new NotImplementedException("Use RPC transport directly");
        }

        public void SendResponse(Message request, Response response)
        {
            // This is called when a grain method completes
            // We need to find the RPC connection and send the response back
            _logger.LogDebug("SendResponse called for message {MessageId}", request.Id);
            
            // For now, log a warning as we need to implement the connection lookup
            _logger.LogWarning("SendResponse not fully implemented - response will be lost");
        }

        public void ReceiveResponse(Message message)
        {
            throw new NotImplementedException("Use RPC transport directly");
        }

        public IAddressable CreateObjectReference(IAddressable obj)
        {
            throw new NotSupportedException("Object references are not supported in RPC mode");
        }

        public void DeleteObjectReference(IAddressable obj)
        {
            // Not implemented for RPC
        }

        public void BreakOutstandingMessagesToSilo(SiloAddress deadSilo)
        {
            // Not applicable for RPC
        }

        public int GetRunningRequestsCount(GrainInterfaceType grainInterfaceType)
        {
            // For testing purposes - always return 0
            return 0;
        }
    }
}