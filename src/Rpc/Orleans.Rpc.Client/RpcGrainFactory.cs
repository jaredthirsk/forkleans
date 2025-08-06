using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.GrainReferences;
using Orleans.Metadata;
using Orleans.Runtime;
using Orleans.Serialization;

namespace Granville.Rpc
{
    /// <summary>
    /// RPC-specific grain factory implementation for clients.
    /// </summary>
    internal sealed class RpcGrainFactory : GrainFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<RpcGrainReference> _referenceLogger;
        private readonly Serializer _serializer;
        private OutsideRpcClient? _rpcClient;

        public RpcGrainFactory(
            IRuntimeClient runtimeClient,
            GrainReferenceActivator referenceActivator,
            GrainInterfaceTypeResolver interfaceTypeResolver,
            GrainInterfaceTypeToGrainTypeResolver interfaceToTypeResolver,
            IServiceProvider serviceProvider,
            ILogger<RpcGrainReference> referenceLogger,
            Serializer serializer)
            : base(runtimeClient, referenceActivator, interfaceTypeResolver, interfaceToTypeResolver)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _referenceLogger = referenceLogger ?? throw new ArgumentNullException(nameof(referenceLogger));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        private OutsideRpcClient GetRpcClient()
        {
            return _rpcClient ??= _serviceProvider.GetRequiredService<OutsideRpcClient>();
        }

        // We can override CreateGrainReference if needed to create RpcGrainReference instances
    }
}