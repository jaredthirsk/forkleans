using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.CodeGeneration;
using Orleans.GrainReferences;
using Orleans.Metadata;
using Orleans.Runtime;
using Orleans.Runtime.Versions;
using Orleans.Serialization;
using Orleans.Serialization.Cloning;
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
        private IGrainReferenceRuntime _grainReferenceRuntime;

        public RpcGrainReferenceActivatorProvider(
            IServiceProvider services,
            RpcProxyProvider rpcProxyProvider,
            [FromKeyedServices("rpc")] GrainPropertiesResolver propertiesResolver,
            GrainVersionManifest grainVersionManifest,
            CodecProvider codecProvider,
            CopyContextPool copyContextPool)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _rpcProxyProvider = rpcProxyProvider ?? throw new ArgumentNullException(nameof(rpcProxyProvider));
            _propertiesResolver = propertiesResolver ?? throw new ArgumentNullException(nameof(propertiesResolver));
            _grainVersionManifest = grainVersionManifest ?? throw new ArgumentNullException(nameof(grainVersionManifest));
            _codecProvider = codecProvider ?? throw new ArgumentNullException(nameof(codecProvider));
            _copyContextPool = copyContextPool ?? throw new ArgumentNullException(nameof(copyContextPool));
        }

        public bool TryGet(GrainType grainType, GrainInterfaceType interfaceType, out IGrainReferenceActivator activator)
        {
            // For RPC grains, we need to create our own proxy wrapper
            // Check if this is an RPC interface (by convention or configuration)
            // Convert interface type to string for checking
            var interfaceTypeStr = interfaceType.ToString();
            if (interfaceTypeStr.Contains("Rpc", StringComparison.Ordinal) || 
                interfaceTypeStr.StartsWith("Shooter.", StringComparison.Ordinal))
            {
                activator = new RpcInterfaceProxyActivator(_services, grainType, interfaceType);
                return true;
            }

            // Not an RPC interface - let Orleans handle it
            activator = null!;
            return false;
        }

        /// <summary>
        /// Creates grain references using generated proxy objects.
        /// </summary>
        private sealed class ProxyGrainReferenceActivator : IGrainReferenceActivator
        {
            private readonly GrainReferenceShared _shared;
            private readonly Func<GrainReferenceShared, IdSpan, GrainReference> _create;

            public ProxyGrainReferenceActivator(Type referenceType, GrainReferenceShared shared)
            {
                _shared = shared;

                var ctor = referenceType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, new[] { typeof(GrainReferenceShared), typeof(IdSpan) })
                    ?? throw new SerializerException("Invalid proxy type: " + referenceType);

                var method = new DynamicMethod(referenceType.Name, typeof(GrainReference), new[] { typeof(object), typeof(GrainReferenceShared), typeof(IdSpan) });
                var il = method.GetILGenerator();
                // arg0 is unused for better delegate performance (avoids argument shuffling thunk)
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Newobj, ctor);
                il.Emit(OpCodes.Ret);
                _create = method.CreateDelegate<Func<GrainReferenceShared, IdSpan, GrainReference>>();
            }

            public GrainReference CreateReference(GrainId grainId) => _create(_shared, grainId.Key);
        }

        /// <summary>
        /// Activator that creates proxy wrappers around RpcGrainReference for interface implementation.
        /// </summary>
        private sealed class RpcInterfaceProxyActivator : IGrainReferenceActivator
        {
            private readonly IServiceProvider _services;
            private readonly GrainType _grainType;
            private readonly GrainInterfaceType _interfaceType;
            private readonly Type _proxyType;
            private readonly ConstructorInfo _proxyCtor;

            public RpcInterfaceProxyActivator(IServiceProvider services, GrainType grainType, GrainInterfaceType interfaceType)
            {
                _services = services;
                _grainType = grainType;
                _interfaceType = interfaceType;

                // Get the actual interface type by convention
                // GrainInterfaceType typically contains the full type name
                var interfaceTypeName = interfaceType.ToString();
                Type actualInterfaceType = null;
                
                // Try to find the interface type in loaded assemblies
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    actualInterfaceType = assembly.GetTypes()
                        .FirstOrDefault(t => t.IsInterface && (t.FullName == interfaceTypeName || t.Name == interfaceTypeName));
                    if (actualInterfaceType != null) break;
                }
                
                if (actualInterfaceType == null)
                {
                    throw new InvalidOperationException($"Cannot find interface type for {interfaceType}");
                }
                
                // Create proxy type
                _proxyType = RpcInterfaceProxyFactory.GetOrCreateProxyType(actualInterfaceType);
                _proxyCtor = _proxyType.GetConstructor(new[] { typeof(RpcGrainReference) })
                    ?? throw new InvalidOperationException($"Proxy type {_proxyType} missing expected constructor");
            }

            public GrainReference CreateReference(GrainId grainId)
            {
                // Create the underlying RpcGrainReference
                var grainReferenceRuntime = _services.GetRequiredService<IGrainReferenceRuntime>();
                var versionManifest = _services.GetRequiredService<GrainVersionManifest>();
                var codecProvider = _services.GetRequiredService<CodecProvider>();
                var copyContextPool = _services.GetRequiredService<CopyContextPool>();
                var rpcClient = _services.GetRequiredService<RpcClient>();
                var serializer = _services.GetRequiredService<Serializer>();
                var referenceLogger = _services.GetRequiredService<ILogger<RpcGrainReference>>();

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

                var rpcGrainRef = new RpcGrainReference(shared, grainId.Key, referenceLogger, rpcClient, serializer,
                    rpcClient.AsyncEnumerableManager);

                // Wrap it in the proxy
                return (GrainReference)_proxyCtor.Invoke(new object[] { rpcGrainRef });
            }
        }

        /// <summary>
        /// Fallback activator that creates RpcGrainReference instances.
        /// </summary>
        private sealed class RpcGrainReferenceActivator : IGrainReferenceActivator
        {
            private readonly IServiceProvider _services;
            private readonly GrainInterfaceType _interfaceType;

            public RpcGrainReferenceActivator(
                IServiceProvider services,
                GrainInterfaceType interfaceType)
            {
                _services = services;
                _interfaceType = interfaceType;
            }

            public GrainReference CreateReference(GrainId grainId, GrainInterfaceType interfaceType)
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
                var interfaceVersion = versionManifest.GetLocalVersion(interfaceType);
                
                // Create the shared state for this grain reference type
                var shared = new GrainReferenceShared(
                    grainId.Type,
                    interfaceType,
                    interfaceVersion,
                    grainReferenceRuntime,
                    InvokeMethodOptions.None,
                    codecProvider,
                    copyContextPool,
                    _services);

                return new RpcGrainReference(shared, grainId.Key, referenceLogger, rpcClient, serializer, 
                    rpcClient.AsyncEnumerableManager);
            }
            
            public GrainReference CreateReference(GrainId grainId)
            {
                // Use the stored interface type for this activator
                return CreateReference(grainId, _interfaceType);
            }
        }
    }
}