using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Forkleans.GrainReferences;
using Forkleans.Metadata;
using Forkleans.Runtime;
using Forkleans.Serialization;

namespace Forkleans.Rpc
{
    /// <summary>
    /// RPC-specific grain factory implementation.
    /// </summary>
    internal sealed class RpcGrainFactory : GrainFactory
    {
        private readonly ILocalRpcServerDetails _serverDetails;

        public RpcGrainFactory(
            IRuntimeClient runtimeClient,
            GrainReferenceActivator referenceActivator,
            GrainInterfaceTypeResolver interfaceTypeResolver,
            GrainInterfaceTypeToGrainTypeResolver interfaceToTypeResolver,
            ILocalRpcServerDetails serverDetails)
            : base(runtimeClient, referenceActivator, interfaceTypeResolver, interfaceToTypeResolver)
        {
            _serverDetails = serverDetails ?? throw new ArgumentNullException(nameof(serverDetails));
        }

        // RPC-specific grain reference creation can be added here if needed
    }
}