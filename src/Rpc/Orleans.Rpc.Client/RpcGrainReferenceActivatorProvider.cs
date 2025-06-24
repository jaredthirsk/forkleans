using System;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Forkleans.CodeGeneration;
using Forkleans.GrainReferences;
using Forkleans.Metadata;
using Forkleans.Runtime;
using Forkleans.Runtime.Versions;
using Forkleans.Serialization;
using Forkleans.Serialization.Cloning;
using Forkleans.Serialization.Serializers;

namespace Forkleans.Rpc
{
    /// <summary>
    /// Provides RPC grain reference activators.
    /// </summary>
    internal sealed class RpcGrainReferenceActivatorProvider : IGrainReferenceActivatorProvider
    {
        private readonly IServiceProvider _services;
        private readonly RpcProvider _rpcProvider;
        private readonly GrainPropertiesResolver _propertiesResolver;
        private readonly GrainVersionManifest _grainVersionManifest;
        private readonly CodecProvider _codecProvider;
        private readonly CopyContextPool _copyContextPool;
        private IGrainReferenceRuntime _grainReferenceRuntime;

        public RpcGrainReferenceActivatorProvider(
            IServiceProvider services,
            RpcProvider rpcProvider,
            [FromKeyedServices("rpc")] GrainPropertiesResolver propertiesResolver,
            GrainVersionManifest grainVersionManifest,
            CodecProvider codecProvider,
            CopyContextPool copyContextPool)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _rpcProvider = rpcProvider ?? throw new ArgumentNullException(nameof(rpcProvider));
            _propertiesResolver = propertiesResolver ?? throw new ArgumentNullException(nameof(propertiesResolver));
            _grainVersionManifest = grainVersionManifest ?? throw new ArgumentNullException(nameof(grainVersionManifest));
            _codecProvider = codecProvider ?? throw new ArgumentNullException(nameof(codecProvider));
            _copyContextPool = copyContextPool ?? throw new ArgumentNullException(nameof(copyContextPool));
        }

        public bool TryGet(GrainType grainType, GrainInterfaceType interfaceType, out IGrainReferenceActivator activator)
        {
            var logger = _services.GetRequiredService<ILogger<RpcGrainReferenceActivatorProvider>>();
            logger.LogDebug("RpcGrainReferenceActivatorProvider.TryGet called for grainType: {GrainType}, interfaceType: {InterfaceType}", grainType, interfaceType);
            
            // Only handle grains that have RPC proxy types
            // This prevents RPC from intercepting Orleans grain creation
            if (_rpcProvider.TryGet(interfaceType, out var proxyType))
            {
                logger.LogDebug("Found RPC proxy type {ProxyType} for interface {InterfaceType}", proxyType.FullName, interfaceType);
                
                // Use the generated proxy type
                var runtime = _grainReferenceRuntime ??= _services.GetRequiredService<IGrainReferenceRuntime>();
                var interfaceVersion = _grainVersionManifest.GetLocalVersion(interfaceType);
                
                var unordered = false;
                var properties = _propertiesResolver.GetGrainProperties(grainType);
                if (properties.Properties.TryGetValue(WellKnownGrainTypeProperties.Unordered, out var unorderedString)
                    && string.Equals("true", unorderedString, StringComparison.OrdinalIgnoreCase))
                {
                    unordered = true;
                }
                
                var invokeMethodOptions = unordered ? InvokeMethodOptions.Unordered : InvokeMethodOptions.None;
                var shared = new GrainReferenceShared(
                    grainType,
                    interfaceType,
                    interfaceVersion,
                    runtime,
                    invokeMethodOptions,
                    _codecProvider,
                    _copyContextPool,
                    _services);
                activator = new ProxyGrainReferenceActivator(proxyType, shared);
                return true;
            }
            
            // No RPC proxy type found - let other providers handle this grain
            logger.LogTrace("No RPC proxy type found for interface {InterfaceType}, deferring to other providers", interfaceType);
            activator = null;
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
                    rpcClient.StreamingManager);
            }
            
            public GrainReference CreateReference(GrainId grainId)
            {
                // Use the stored interface type for this activator
                return CreateReference(grainId, _interfaceType);
            }
        }
    }
}