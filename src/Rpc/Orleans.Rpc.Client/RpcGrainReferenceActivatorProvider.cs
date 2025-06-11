using System;
using System.Reflection;
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

        public RpcGrainReferenceActivatorProvider(IServiceProvider services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
        }

        public bool TryGet(GrainType grainType, GrainInterfaceType interfaceType, out IGrainReferenceActivator activator)
        {
            // Try to find the interface type from the current app domain
            Type interfaceClrType = null;
            var interfaceTypeName = interfaceType.Value.ToString();
            
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var types = assembly.GetTypes();
                    foreach (var type in types)
                    {
                        if (type.IsInterface)
                        {
                            // Check if this type has been assigned this GrainInterfaceType
                            var attr = type.GetCustomAttribute<GrainInterfaceTypeAttribute>();
                            if (attr != null && attr.GetGrainInterfaceType(_services, type) == interfaceType)
                            {
                                interfaceClrType = type;
                                break;
                            }
                        }
                    }
                    if (interfaceClrType != null) break;
                }
                catch
                {
                    // Skip assemblies that can't be loaded
                }
            }
            
            if (interfaceClrType != null)
            {
                // Check if the interface inherits from any RPC grain interface by checking parent interfaces
                var baseInterfaces = interfaceClrType.GetInterfaces();
                foreach (var baseInterface in baseInterfaces)
                {
                    var name = baseInterface.Name;
                    if (name == "IRpcGrainInterface" || 
                        name == "IRpcGrainInterfaceWithStringKey" ||
                        name == "IRpcGrainInterfaceWithGuidKey" ||
                        name == "IRpcGrainInterfaceWithIntegerCompoundKey" ||
                        name == "IRpcGrainInterfaceWithGuidCompoundKey")
                    {
                        activator = new RpcGrainReferenceActivator(_services, interfaceType);
                        return true;
                    }
                }
            }
            
            // Not an RPC grain interface
            activator = null;
            return false;
        }

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

                return new RpcGrainReference(shared, grainId.Key, referenceLogger, rpcClient, serializer);
            }
            
            public GrainReference CreateReference(GrainId grainId)
            {
                // Use the stored interface type for this activator
                return CreateReference(grainId, _interfaceType);
            }
        }
    }
}