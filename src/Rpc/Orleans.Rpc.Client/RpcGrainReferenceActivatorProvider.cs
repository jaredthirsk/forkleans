using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.CodeGeneration;
using Orleans.GrainReferences;
using Orleans.Metadata;
using Orleans.Runtime;
using Orleans.Runtime.Versions;
using Orleans.Serialization;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Configuration;
using Orleans.Serialization.Serializers;

namespace Granville.Rpc
{
    /// <summary>
    /// Provides RPC grain reference activators.
    /// </summary>
    internal sealed class RpcGrainReferenceActivatorProvider : IGrainReferenceActivatorProvider
    {
        private readonly IServiceProvider _services;
        private readonly RpcProxyProvider _rpcProxyProvider;
        private readonly GrainPropertiesResolver _propertiesResolver;
        private readonly GrainVersionManifest _grainVersionManifest;
        private readonly CodecProvider _codecProvider;
        private readonly CopyContextPool _copyContextPool;
        private readonly Orleans.GrainReferences.RpcProvider _orleansRpcProvider;
        private IGrainReferenceRuntime _grainReferenceRuntime;

        public RpcGrainReferenceActivatorProvider(
            IServiceProvider services,
            RpcProxyProvider rpcProxyProvider,
            [FromKeyedServices("rpc")] GrainPropertiesResolver propertiesResolver,
            GrainVersionManifest grainVersionManifest,
            CodecProvider codecProvider,
            CopyContextPool copyContextPool,
            Orleans.GrainReferences.RpcProvider orleansRpcProvider)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _rpcProxyProvider = rpcProxyProvider ?? throw new ArgumentNullException(nameof(rpcProxyProvider));
            _propertiesResolver = propertiesResolver ?? throw new ArgumentNullException(nameof(propertiesResolver));
            _grainVersionManifest = grainVersionManifest ?? throw new ArgumentNullException(nameof(grainVersionManifest));
            _codecProvider = codecProvider ?? throw new ArgumentNullException(nameof(codecProvider));
            _copyContextPool = copyContextPool ?? throw new ArgumentNullException(nameof(copyContextPool));
            _orleansRpcProvider = orleansRpcProvider ?? throw new ArgumentNullException(nameof(orleansRpcProvider));
        }

        public bool TryGet(GrainType grainType, GrainInterfaceType interfaceType, out IGrainReferenceActivator activator)
        {
            // Check if this is an RPC interface (by convention or configuration)
            var interfaceTypeStr = interfaceType.ToString();
            if (interfaceTypeStr.Contains("Rpc", StringComparison.Ordinal) || 
                interfaceTypeStr.StartsWith("Shooter.", StringComparison.Ordinal))
            {
                // Check if Orleans has a generated proxy for this interface
                if (_orleansRpcProvider.TryGet(interfaceType, out var proxyType))
                {
                    // Use Orleans-generated proxy with RPC grain reference
                    activator = new OrleansProxyWithRpcReferenceActivator(_services, grainType, interfaceType, proxyType);
                    return true;
                }
                else
                {
                    // Fall back to creating RpcGrainReference directly (no proxy)
                    activator = new RpcGrainReferenceActivator(_services, grainType, interfaceType);
                    return true;
                }
            }

            // Not an RPC interface - let Orleans handle it
            activator = null!;
            return false;
        }

        /// <summary>
        /// Activator that creates Orleans proxies backed by RpcGrainReference.
        /// </summary>
        private sealed class OrleansProxyWithRpcReferenceActivator : IGrainReferenceActivator
        {
            private readonly IServiceProvider _services;
            private readonly GrainType _grainType;
            private readonly GrainInterfaceType _interfaceType;
            private readonly Type _proxyType;
            private readonly Func<GrainReferenceShared, IdSpan, GrainReference> _createProxy;

            public OrleansProxyWithRpcReferenceActivator(
                IServiceProvider services,
                GrainType grainType,
                GrainInterfaceType interfaceType,
                Type proxyType)
            {
                _services = services;
                _grainType = grainType;
                _interfaceType = interfaceType;
                _proxyType = proxyType;

                // Prepare the proxy constructor
                var ctor = _proxyType.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    new[] { typeof(GrainReferenceShared), typeof(IdSpan) })
                    ?? throw new InvalidOperationException($"Proxy type {_proxyType} missing expected constructor");

                // Create a dynamic method for fast instantiation
                var method = new DynamicMethod(_proxyType.Name + "_Create", typeof(GrainReference),
                    new[] { typeof(GrainReferenceShared), typeof(IdSpan) });
                var il = method.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Newobj, ctor);
                il.Emit(OpCodes.Ret);
                _createProxy = method.CreateDelegate<Func<GrainReferenceShared, IdSpan, GrainReference>>();
            }

            public GrainReference CreateReference(GrainId grainId)
            {
                // Create the GrainReferenceShared for an RpcGrainReference
                var grainReferenceRuntime = _services.GetRequiredService<IGrainReferenceRuntime>();
                var versionManifest = _services.GetRequiredService<GrainVersionManifest>();
                var codecProvider = _services.GetRequiredService<CodecProvider>();
                var copyContextPool = _services.GetRequiredService<CopyContextPool>();

                var interfaceVersion = versionManifest.GetLocalVersion(_interfaceType);
                var shared = new GrainReferenceShared(
                    _grainType,
                    _interfaceType,
                    interfaceVersion,
                    grainReferenceRuntime,
                    InvokeMethodOptions.None,
                    codecProvider,
                    copyContextPool,
                    _services);

                // Create the Orleans proxy but it will use our RPC runtime
                return _createProxy(shared, grainId.Key);
            }
        }

        /// <summary>
        /// Fallback activator that creates RpcGrainReference instances.
        /// </summary>
        private sealed class RpcGrainReferenceActivator : IGrainReferenceActivator
        {
            private readonly IServiceProvider _services;
            private readonly GrainType _grainType;
            private readonly GrainInterfaceType _interfaceType;

            public RpcGrainReferenceActivator(
                IServiceProvider services,
                GrainType grainType,
                GrainInterfaceType interfaceType)
            {
                _services = services;
                _grainType = grainType;
                _interfaceType = interfaceType;
            }

            public GrainReference CreateReference(GrainId grainId)
            {
                // Lazily get services to avoid circular dependencies
                var grainReferenceRuntime = _services.GetRequiredService<IGrainReferenceRuntime>();
                var versionManifest = _services.GetRequiredService<GrainVersionManifest>();
                var codecProvider = _services.GetRequiredService<CodecProvider>();
                var copyContextPool = _services.GetRequiredService<CopyContextPool>();
                var rpcClient = _services.GetRequiredService<RpcClient>();
                var serializer = _services.GetRequiredService<Serializer>();
                var referenceLogger = _services.GetRequiredService<ILogger<RpcGrainReference>>();
                
                // Get the interface version
                var interfaceVersion = versionManifest.GetLocalVersion(_interfaceType);
                
                // Create the shared state for this grain reference type
                var shared = new GrainReferenceShared(
                    _grainType,
                    _interfaceType,
                    interfaceVersion,
                    grainReferenceRuntime,
                    InvokeMethodOptions.None,
                    codecProvider,
                    copyContextPool,
                    _services);

                return new RpcGrainReference(shared, grainId.Key, referenceLogger, rpcClient, serializer, 
                    rpcClient.AsyncEnumerableManager);
            }
        }
    }
}