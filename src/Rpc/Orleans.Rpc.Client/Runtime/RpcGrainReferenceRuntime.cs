using System;
using System.Linq;
using System.Reflection;
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
                var methodId = GetMethodId(rpcRef.InterfaceType, request.GetMethodName());
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
                var methodId = GetMethodId(rpcRef.InterfaceType, request.GetMethodName());
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
                var methodId = GetMethodId(rpcRef.InterfaceType, request.GetMethodName());
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

        private int GetMethodId(GrainInterfaceType interfaceType, string methodName)
        {
            // Log the interface type format for debugging
            var interfaceTypeStr = interfaceType.ToString();
            _logger.LogDebug("GetMethodId called for interface: {InterfaceType}, method: {MethodName}", 
                interfaceTypeStr, methodName);
            
            // Find the actual interface type
            // This is a simplified approach - in production, you'd want to cache this
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            Type interfaceClrType = null;
            
            // GrainInterfaceType.Value is an IdSpan, convert to string
            var typeName = interfaceType.Value.ToString();
            _logger.LogDebug("Searching for type with Value: {TypeName}", typeName);
            
            // Also try the full ToString() which includes assembly info
            var fullTypeName = interfaceTypeStr;
            if (fullTypeName.Contains(','))
            {
                // Extract just the type name part before the comma
                fullTypeName = fullTypeName.Split(',')[0].Trim();
            }
            _logger.LogDebug("Also searching for type: {FullTypeName}", fullTypeName);
            
            foreach (var assembly in assemblies)
            {
                try
                {
                    var types = assembly.GetTypes();
                    foreach (var type in types)
                    {
                        if (type.IsInterface && 
                            (type.Name == typeName || 
                             type.FullName == typeName ||
                             type.FullName == fullTypeName ||
                             type.Name == fullTypeName ||
                             type.FullName.EndsWith("." + typeName)))
                        {
                            _logger.LogDebug("Found matching interface type: {TypeFullName} in assembly {AssemblyName}", 
                                type.FullName, assembly.GetName().Name);
                            interfaceClrType = type;
                            break;
                        }
                    }
                    if (interfaceClrType != null) break;
                }
                catch (Exception ex)
                {
                    // Some assemblies might not be loadable
                    _logger.LogTrace(ex, "Could not load types from assembly {Assembly}", assembly.FullName);
                }
            }
            
            if (interfaceClrType == null)
            {
                _logger.LogError("Could not find interface type for {InterfaceType}. Searched {AssemblyCount} assemblies. Type formats tried: Value='{TypeName}', FullName='{FullTypeName}'", 
                    interfaceTypeStr, assemblies.Length, typeName, fullTypeName);
                _logger.LogWarning("Using fallback method ID based on method name hash for {MethodName}", methodName);
                return Math.Abs(methodName.GetHashCode()) % 100; // Fallback
            }
            
            // Get methods sorted alphabetically (same as server)
            var methods = interfaceClrType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => !m.IsSpecialName)
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .ToArray();
            
            
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name == methodName)
                {
                    _logger.LogTrace("Method {MethodName} on {Interface} has ID {MethodId}", methodName, interfaceType, i);
                    return i;
                }
            }
            
            throw new InvalidOperationException($"Method {methodName} not found on interface {interfaceType}");
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