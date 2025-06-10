using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Forkleans.CodeGeneration;
using Forkleans.Configuration;
using Forkleans.GrainReferences;
using Forkleans.Messaging;
using Forkleans.Runtime;
using Forkleans.Serialization;
using Forkleans.Serialization.Invocation;

namespace Forkleans.Rpc
{
    /// <summary>
    /// RPC-specific implementation of IRuntimeClient for outside (client) use.
    /// </summary>
    internal sealed class OutsideRpcRuntimeClient : IRuntimeClient
    {
        private readonly ILogger<OutsideRpcRuntimeClient> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IGrainReferenceRuntime _grainReferenceRuntime;
        private readonly TimeProvider _timeProvider;
        private readonly ILocalClientDetails _clientDetails;
        private readonly RpcClient _rpcClient;
        private TimeSpan _responseTimeout = TimeSpan.FromSeconds(30);

        public OutsideRpcRuntimeClient(
            IServiceProvider serviceProvider,
            ILogger<OutsideRpcRuntimeClient> logger,
            IGrainReferenceRuntime grainReferenceRuntime,
            TimeProvider timeProvider,
            ILocalClientDetails clientDetails,
            RpcClient rpcClient)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _grainReferenceRuntime = grainReferenceRuntime ?? throw new ArgumentNullException(nameof(grainReferenceRuntime));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _clientDetails = clientDetails ?? throw new ArgumentNullException(nameof(clientDetails));
            _rpcClient = rpcClient ?? throw new ArgumentNullException(nameof(rpcClient));
        }

        public TimeProvider TimeProvider => _timeProvider;

        public IInternalGrainFactory InternalGrainFactory => _serviceProvider.GetRequiredService<IGrainFactory>() as IInternalGrainFactory;

        public string CurrentActivationIdentity => _clientDetails.ClientId;

        public IServiceProvider ServiceProvider => _serviceProvider;

        public IGrainReferenceRuntime GrainReferenceRuntime => _grainReferenceRuntime;

        public TimeSpan GetResponseTimeout() => _responseTimeout;

        public void SetResponseTimeout(TimeSpan timeout) => _responseTimeout = timeout;

        public void SendRequest(GrainReference target, IInvokable request, IResponseCompletionSource context, InvokeMethodOptions options)
        {
            // TODO: Implement RPC-specific message sending
            _logger.LogDebug("Sending RPC request to {Target}", target);
            
            // For now, just fail the request
            context.Complete(Response.FromException(new NotImplementedException("RPC message sending not yet implemented")));
        }

        public void SendResponse(Message request, Response response)
        {
            throw new NotSupportedException("Client does not send responses");
        }

        public void ReceiveResponse(Message message)
        {
            throw new NotImplementedException("RPC response handling not yet implemented");
        }

        public IAddressable CreateObjectReference(IAddressable obj)
        {
            throw new NotSupportedException("Object references are not supported in RPC mode");
        }

        public void DeleteObjectReference(IAddressable obj)
        {
            // Not supported in RPC mode
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