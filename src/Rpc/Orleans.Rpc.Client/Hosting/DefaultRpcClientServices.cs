#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Forkleans.Configuration;
using Forkleans.Configuration.Internal;
using Forkleans.GrainReferences;
using Forkleans.Metadata;
using Forkleans.Providers;
using Forkleans.Runtime;
using Forkleans.Runtime.Versions;
using Forkleans.Rpc.Configuration;
using Forkleans.Rpc.Transport;
using Forkleans.Serialization;
using Forkleans.Serialization.Cloning;
using Forkleans.Serialization.Internal;
using Forkleans.Serialization.Serializers;
using Forkleans.Statistics;
using System;
using System.Linq;

namespace Forkleans.Rpc.Hosting
{
    internal static class DefaultRpcClientServices
    {
        private static readonly ServiceDescriptor ServiceDescriptor = new(typeof(ServicesAdded), new ServicesAdded());

        internal static void AddDefaultServices(IRpcClientBuilder builder)
        {
            var services = builder.Services;
            if (services.Contains(ServiceDescriptor))
            {
                return;
            }

            services.Add(ServiceDescriptor);

            // Common services
            services.AddLogging();
            services.AddOptions();
            services.TryAddSingleton<TimeProvider>(TimeProvider.System);

            // Options logging
            services.TryAddSingleton(typeof(IOptionFormatter<>), typeof(DefaultOptionsFormatter<>));
            services.TryAddSingleton(typeof(IOptionFormatterResolver<>), typeof(DefaultOptionsFormatterResolver<>));

            // Statistics
            services.AddSingleton<IEnvironmentStatisticsProvider, EnvironmentStatisticsProvider>();
#pragma warning disable 618
            services.AddSingleton<OldEnvironmentStatistics>();
            services.AddFromExisting<IAppEnvironmentStatistics, OldEnvironmentStatistics>();
            services.AddFromExisting<IHostEnvironmentStatistics, OldEnvironmentStatistics>();
#pragma warning restore 618

            // RPC Client
            services.TryAddSingleton<RpcClient>();
            services.TryAddFromExisting<IRpcClient, RpcClient>();
            services.TryAddFromExisting<IClusterClient, RpcClient>();
            services.AddFromExisting<IHostedService, RpcClient>();
            
            // RPC client lifecycle
            services.TryAddSingleton<RpcClientLifecycleSubject>();
            services.TryAddFromExisting<IClusterClientLifecycle, RpcClientLifecycleSubject>();
            
            // Local client details
            services.TryAddSingleton<ILocalClientDetails, LocalRpcClientDetails>();
            
            // Grain context
            services.TryAddSingleton<ClientGrainContext>();
            services.AddFromExisting<IGrainContextAccessor, ClientGrainContext>();
            
            // RPC runtime client
            services.TryAddSingleton<OutsideRpcRuntimeClient>();
            services.TryAddFromExisting<IRuntimeClient, OutsideRpcRuntimeClient>();
            
            // Local client details
            services.TryAddSingleton<ILocalClientDetails, LocalRpcClientDetails>();
            
            // Grain factory and references
            services.TryAddSingleton<RpcGrainFactory>();
            services.TryAddSingleton<GrainFactory>(sp => sp.GetRequiredService<RpcGrainFactory>());
            services.TryAddSingleton<InterfaceToImplementationMappingCache>();
            services.TryAddSingleton<GrainInterfaceTypeToGrainTypeResolver>();
            services.TryAddSingleton<GrainReferenceActivator>();
            // Add RPC grain reference provider first so it takes precedence
            services.AddSingleton<IGrainReferenceActivatorProvider, RpcGrainReferenceActivatorProvider>();
            services.AddSingleton<IGrainReferenceActivatorProvider, GrainReferenceActivatorProvider>();
            services.AddSingleton<IGrainReferenceActivatorProvider, UntypedGrainReferenceActivatorProvider>();
            services.TryAddSingleton<RpcProvider>();
            services.TryAddSingleton<IGrainReferenceRuntime, GrainReferenceRuntime>();
            services.TryAddSingleton<GrainPropertiesResolver>();
            // Grain cancellation token runtime
            services.TryAddSingleton<IGrainCancellationTokenRuntime, GrainCancellationTokenRuntime>();
            services.TryAddFromExisting<IGrainFactory, GrainFactory>();
            services.TryAddFromExisting<IInternalGrainFactory, GrainFactory>();
            
            // Provider runtime
            services.TryAddSingleton<ClientProviderRuntime>();
            services.TryAddFromExisting<IProviderRuntime, ClientProviderRuntime>();
            
            // Message factory
            services.TryAddSingleton<MessageFactory>();
            
            // Type metadata
            services.TryAddSingleton<GrainBindingsResolver>();
            services.AddSingleton<GrainInterfaceTypeResolver>();
            services.AddSingleton<IGrainInterfaceTypeProvider, AttributeGrainInterfaceTypeProvider>();
            // Note: AttributeGrainInterfacePropertiesProvider is internal
            // services.AddSingleton<IGrainInterfacePropertiesProvider, AttributeGrainInterfacePropertiesProvider>();
            services.AddSingleton<IGrainInterfacePropertiesProvider, TypeNameGrainPropertiesProvider>();
            // Note: ImplementedInterfaceProvider is internal
            // services.AddSingleton<IGrainInterfacePropertiesProvider, ImplementedInterfaceProvider>();
            services.AddSingleton<IGrainPropertiesProvider, AttributeGrainPropertiesProvider>();
            services.AddSingleton<IGrainPropertiesProvider, TypeNameGrainPropertiesProvider>();
            // Note: ImplementedInterfaceProvider is internal 
            // services.AddSingleton<IGrainPropertiesProvider, ImplementedInterfaceProvider>();
            services.AddSingleton<IGrainTypeProvider, AttributeGrainTypeProvider>();
            services.AddSingleton<GrainTypeResolver>();
            // Note: GrainVersionManifest might be internal
            // services.AddSingleton<GrainVersionManifest>();
            
            // Cluster manifest provider for RPC (simplified version)
            services.AddSingleton<RpcClientManifestProvider>();
            services.AddSingleton<IClusterManifestProvider>(sp => sp.GetRequiredService<RpcClientManifestProvider>());
            
            // Grain version manifest
            services.TryAddSingleton<GrainVersionManifest>();
            
            // Add grain cancellation token runtime
            services.TryAddSingleton<IGrainCancellationTokenRuntime, GrainCancellationTokenRuntime>();
            
            // Add the standard Orleans grain reference runtime
            services.TryAddSingleton<IGrainReferenceRuntime, GrainReferenceRuntime>();

            // Serialization
            services.AddSerializer(serializer =>
            {
                // Add the RPC abstractions assembly for protocol messages
                serializer.AddAssembly(typeof(Protocol.RpcMessage).Assembly);
            });
            services.AddSingleton<ITypeNameFilter, AllowForkleanTypes>();
            // Note: GrainReferenceCodecProvider and GrainReferenceCopierProvider are internal
            // services.AddSingleton<ISpecializableCodec, GrainReferenceCodecProvider>();
            // services.AddSingleton<ISpecializableCopier, GrainReferenceCopierProvider>();
            services.AddSingleton<OnDeserializedCallbacks>();
            services.AddSingleton<MigrationContext.SerializationHooks>();
            services.AddSingleton<IPostConfigureOptions<OrleansJsonSerializerOptions>, ConfigureOrleansJsonSerializerOptions>();
            services.AddSingleton<OrleansJsonSerializer>();

            // RPC Transport
            //services.TryAddSingleton<IRpcTransportFactory, DefaultRpcTransportFactory>();  // We don't have a default factory, so the user needs to add this in their application.

            // RPC Protocol
            services.TryAddSingleton<Rpc.Protocol.RpcMessageSerializer>();
            
            // Configure formatters for required options
            services.ConfigureFormatter<RpcClientOptions>();
            services.ConfigureFormatter<RpcTransportOptions>();
            services.ConfigureFormatter<ClusterOptions>();
            services.ConfigureFormatter<GrainTypeOptions>();
            
            // Configuration options
            services.AddSingleton<IConfigureOptions<GrainTypeOptions>, DefaultGrainTypeOptionsProvider>();
            services.Configure<TypeManagementOptions>(options => { });
        }

        private class AllowForkleanTypes : ITypeNameFilter
        {
            public bool? IsTypeNameAllowed(string typeName, string assemblyName)
            {
                if (assemblyName is { Length: > 0 } && assemblyName.Contains("Orleans"))
                {
                    return true;
                }

                return null;
            }
        }

        private class ServicesAdded { }
    }
}
