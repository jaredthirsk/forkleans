using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.CodeGeneration;
using Orleans.GrainReferences;
using Orleans.Metadata;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Serialization.Invocation;
using Orleans.Serialization.Session;

namespace Granville.Rpc.Runtime
{
    /// <summary>
    /// RPC-specific implementation of IGrainReferenceRuntime that routes calls through RPC transport.
    /// </summary>
    internal class RpcGrainReferenceRuntime : IGrainReferenceRuntime
    {
        private readonly ILogger<RpcGrainReferenceRuntime> _logger;
        private readonly IRuntimeClient _runtimeClient;
        private readonly GrainReferenceActivator _referenceActivator;
        private readonly GrainInterfaceTypeResolver _interfaceTypeResolver;
        private readonly RpcClient _rpcClient;
        private readonly Serializer _serializer;
        private readonly SerializerSessionPool _sessionPool;

        public RpcGrainReferenceRuntime(
            ILogger<RpcGrainReferenceRuntime> logger,
            IRuntimeClient runtimeClient,
            GrainReferenceActivator referenceActivator,
            GrainInterfaceTypeResolver interfaceTypeResolver,
            RpcClient rpcClient,
            Serializer serializer,
            SerializerSessionPool sessionPool)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _runtimeClient = runtimeClient ?? throw new ArgumentNullException(nameof(runtimeClient));
            _referenceActivator = referenceActivator ?? throw new ArgumentNullException(nameof(referenceActivator));
            _interfaceTypeResolver = interfaceTypeResolver ?? throw new ArgumentNullException(nameof(interfaceTypeResolver));
            _rpcClient = rpcClient ?? throw new ArgumentNullException(nameof(rpcClient));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _sessionPool = sessionPool ?? throw new ArgumentNullException(nameof(sessionPool));
        }

        public async ValueTask<T> InvokeMethodAsync<T>(GrainReference reference, IInvokable request, InvokeMethodOptions options)
        {
            if (reference is not RpcGrainReference rpcRef)
            {
                // Fall back to Orleans runtime for non-RPC grains
                return await _runtimeClient.GrainReferenceRuntime.InvokeMethodAsync<T>(reference, request, options);
            }

            try
            {
                // Extract method info from the invokable
                var methodId = GetMethodId(request);
                var arguments = GetMethodArguments(request);

                // Route through RPC transport
                _logger.LogDebug("Routing method {MethodId} through RPC transport for grain {GrainId}", 
                    methodId, rpcRef.GrainId);

                var result = await rpcRef.InvokeRpcMethodAsync<T>(methodId, arguments);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error invoking RPC method on grain {GrainId}", rpcRef.GrainId);
                throw;
            }
        }

        public async ValueTask InvokeMethodAsync(GrainReference reference, IInvokable request, InvokeMethodOptions options)
        {
            if (reference is not RpcGrainReference rpcRef)
            {
                // Fall back to Orleans runtime for non-RPC grains
                await _runtimeClient.GrainReferenceRuntime.InvokeMethodAsync(reference, request, options);
                return;
            }

            try
            {
                // Extract method info from the invokable
                var methodId = GetMethodId(request);
                var arguments = GetMethodArguments(request);

                // Route through RPC transport (void return)
                _logger.LogDebug("Routing void method {MethodId} through RPC transport for grain {GrainId}", 
                    methodId, rpcRef.GrainId);

                await rpcRef.InvokeRpcMethodAsync<object>(methodId, arguments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error invoking RPC void method on grain {GrainId}", rpcRef.GrainId);
                throw;
            }
        }

        public void InvokeMethod(GrainReference reference, IInvokable request, InvokeMethodOptions options)
        {
            if (reference is not RpcGrainReference rpcRef)
            {
                // Fall back to Orleans runtime for non-RPC grains
                _runtimeClient.GrainReferenceRuntime.InvokeMethod(reference, request, options);
                return;
            }

            try
            {
                // Extract method info from the invokable
                var methodId = GetMethodId(request);
                var arguments = GetMethodArguments(request);

                // For one-way calls, fire and forget
                _logger.LogDebug("Sending one-way method {MethodId} through RPC transport for grain {GrainId}", 
                    methodId, rpcRef.GrainId);

                // Fire and forget - don't await
                _ = rpcRef.InvokeRpcMethodAsync<object>(methodId, arguments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error invoking RPC one-way method on grain {GrainId}", rpcRef.GrainId);
                throw;
            }
        }

        public object Cast(IAddressable grain, Type interfaceType)
        {
            var grainId = grain.GetGrainId();
            
            // If it's already the correct type, return as-is
            if (grain is GrainReference grainRef && interfaceType.IsAssignableFrom(grain.GetType()))
            {
                return grain;
            }

            // For RPC grains, ensure we create RpcGrainReference but let Orleans proxies handle the interface
            var interfaceTypeId = _interfaceTypeResolver.GetGrainInterfaceType(interfaceType);
            
            // Check if this is an RPC interface
            var interfaceTypeStr = interfaceTypeId.ToString();
            if (interfaceTypeStr.Contains("Rpc", StringComparison.Ordinal) || 
                interfaceTypeStr.StartsWith("Shooter.", StringComparison.Ordinal))
            {
                // Let the reference activator create the appropriate proxy
                return _referenceActivator.CreateReference(grainId, interfaceTypeId);
            }

            // Fall back to Orleans casting for non-RPC interfaces
            return _runtimeClient.GrainReferenceRuntime.Cast(grain, interfaceType);
        }

        private int GetMethodId(IInvokable invokable)
        {
            // Get method name from the invokable
            var methodName = invokable.GetMethodName();
            
            // For now, use a simple hash of the method name as the method ID
            // In a production system, this should match how the server identifies methods
            return methodName.GetHashCode();
        }

        private object[] GetMethodArguments(IInvokable invokable)
        {
            // Extract arguments from the invokable
            var argumentCount = invokable.GetArgumentCount();
            var arguments = new object[argumentCount];
            
            for (int i = 0; i < argumentCount; i++)
            {
                arguments[i] = invokable.GetArgument(i);
            }

            return arguments;
        }
    }
}