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
        private readonly RpcClient _rpcClient;
        private readonly ILogger<RpcGrainReference> _referenceLogger;
        private readonly Serializer _serializer;

        public RpcGrainFactory(
            IRuntimeClient runtimeClient,
            GrainReferenceActivator referenceActivator,
            GrainInterfaceTypeResolver interfaceTypeResolver,
            GrainInterfaceTypeToGrainTypeResolver interfaceToTypeResolver,
            RpcClient rpcClient,
            ILogger<RpcGrainReference> referenceLogger,
            Serializer serializer)
            : base(runtimeClient, referenceActivator, interfaceTypeResolver, interfaceToTypeResolver)
        {
            _rpcClient = rpcClient ?? throw new ArgumentNullException(nameof(rpcClient));
            _referenceLogger = referenceLogger ?? throw new ArgumentNullException(nameof(referenceLogger));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        // We can override CreateGrainReference if needed to create RpcGrainReference instances
    }
}