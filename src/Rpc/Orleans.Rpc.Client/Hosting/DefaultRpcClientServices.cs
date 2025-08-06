#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Configuration;
using Orleans.Configuration.Internal;
using Orleans.GrainReferences;
using Orleans.Metadata;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Runtime.Versions;
using Granville.Rpc.Configuration;
using Granville.Rpc.Transport;
using Orleans.Serialization;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Configuration;
using Orleans.Serialization.Internal;
using Orleans.Serialization.Serializers;
using Orleans.Statistics;
using System;
using System.Linq;

namespace Granville.Rpc.Hosting
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

            // RPC Client - following Orleans pattern of separation
            services.TryAddSingleton<OutsideRpcClient>();
            services.TryAddSingleton<RpcClusterClient>();
            services.TryAddFromExisting<IRpcClient, RpcClusterClient>();
            services.TryAddFromExisting<IClusterClient, RpcClusterClient>();
            services.TryAddFromExisting<IInternalClusterClient, RpcClusterClient>();
            services.AddFromExisting<IHostedService, RpcClusterClient>();
            
            // RPC client lifecycle
            services.TryAddSingleton<RpcClientLifecycleSubject>();
            services.TryAddFromExisting<IClusterClientLifecycle, RpcClientLifecycleSubject>();
            
            // Local client details
            services.TryAddSingleton<ILocalClientDetails, LocalRpcClientDetails>();
            
            // Zone detection strategy (default is null, can be overridden by user)
            services.TryAddSingleton<Zones.IZoneDetectionStrategy>(sp => null);
            
            // Grain context
            services.TryAddSingleton<ClientGrainContext>();
            services.AddFromExisting<IGrainContextAccessor, ClientGrainContext>();
            
            // RPC runtime client 
            services.TryAddSingleton<OutsideRpcRuntimeClient>();
            
            // Register IRuntimeClient with lazy initialization to break circular dependency
            services.TryAddSingleton<IRuntimeClient>(sp =>
            {
                // First get the OutsideRpcRuntimeClient
                var orleansRuntimeClient = sp.GetRequiredService<OutsideRpcRuntimeClient>();
                
                // Return it directly for now - the RpcGrainReferenceRuntime will be wired up
                // through the IGrainReferenceRuntime registration
                return orleansRuntimeClient;
            });
            
            // Grain factory and references
            // Follow Orleans pattern: register GrainFactory as singleton first
            services.TryAddSingleton<GrainFactory>(sp => 
            {
                var runtimeClient = sp.GetRequiredService<IRuntimeClient>();
                var referenceActivator = sp.GetRequiredService<GrainReferenceActivator>();
                var interfaceTypeResolver = sp.GetRequiredService<GrainInterfaceTypeResolver>();
                var interfaceToTypeResolver = sp.GetRequiredKeyedService<GrainInterfaceTypeToGrainTypeResolver>("rpc");
                return new GrainFactory(runtimeClient, referenceActivator, interfaceTypeResolver, interfaceToTypeResolver);
            });
            
            // RpcGrainFactory wraps GrainFactory with RPC-specific functionality
            services.TryAddSingleton<RpcGrainFactory>(sp => new RpcGrainFactory(
                sp.GetRequiredService<IRuntimeClient>(),
                sp.GetRequiredService<GrainReferenceActivator>(),
                sp.GetRequiredService<GrainInterfaceTypeResolver>(),
                sp.GetRequiredKeyedService<GrainInterfaceTypeToGrainTypeResolver>("rpc"),
                sp,
                sp.GetRequiredService<ILogger<RpcGrainReference>>(),
                sp.GetRequiredService<Serializer>()));
            
            services.TryAddSingleton<InterfaceToImplementationMappingCache>();
            // Use keyed singleton for RPC to avoid conflicts with Orleans client
            services.TryAddKeyedSingleton<GrainInterfaceTypeToGrainTypeResolver>("rpc", (sp, key) => new GrainInterfaceTypeToGrainTypeResolver(
                sp.GetRequiredKeyedService<IClusterManifestProvider>("rpc")));
            services.TryAddSingleton<GrainReferenceActivator>();
            // Add RPC grain reference provider first so it takes precedence
            services.AddSingleton<IGrainReferenceActivatorProvider, RpcGrainReferenceActivatorProvider>();
            services.AddSingleton<IGrainReferenceActivatorProvider, GrainReferenceActivatorProvider>();
            services.AddSingleton<IGrainReferenceActivatorProvider, UntypedGrainReferenceActivatorProvider>();
            // Register GranvilleRpcProvider
            services.TryAddSingleton<GranvilleRpcProvider>();
            // Register Orleans RpcProvider first
            services.TryAddSingleton<Orleans.GrainReferences.RpcProvider, OrleansRpcProviderAdapter>();
            // Then register RpcProxyProvider that delegates to it
            services.TryAddSingleton<RpcProxyProvider>(sp => new RpcProxyProvider(
                sp.GetRequiredService<Orleans.GrainReferences.RpcProvider>()));
            
            // Register Orleans' GrainReferenceRuntime first (for fallback)
            services.TryAddSingleton<Orleans.Runtime.GrainReferenceRuntime>();
            
            // Register RPC runtime components
            services.TryAddSingleton<Granville.Rpc.Runtime.RpcGrainReferenceRuntime>();
            services.TryAddSingleton<Granville.Rpc.Runtime.RpcRuntimeClient>();
            
            // Register the RPC grain reference runtime as the primary IGrainReferenceRuntime
            services.TryAddSingleton<IGrainReferenceRuntime>(sp => 
                sp.GetRequiredService<Granville.Rpc.Runtime.RpcGrainReferenceRuntime>());
            // Register GrainPropertiesResolver as keyed service for RPC
            services.AddKeyedSingleton<GrainPropertiesResolver>("rpc", (sp, key) => 
                new GrainPropertiesResolver(
                    sp.GetRequiredKeyedService<IClusterManifestProvider>("rpc")
                ));
            // For standalone mode, also register as unkeyed
            services.TryAddSingleton<GrainPropertiesResolver>(sp => 
                sp.GetRequiredKeyedService<GrainPropertiesResolver>("rpc"));
            // Grain cancellation token runtime
            services.TryAddSingleton<IGrainCancellationTokenRuntime, GrainCancellationTokenRuntime>();
            
            // Register keyed GrainFactory for RPC to coexist with Orleans
            services.AddKeyedSingleton<GrainFactory>("rpc", (sp, key) => 
            {
                var runtimeClient = sp.GetRequiredService<IRuntimeClient>();
                var referenceActivator = sp.GetRequiredService<GrainReferenceActivator>();
                var interfaceTypeResolver = sp.GetRequiredService<GrainInterfaceTypeResolver>();
                var interfaceToTypeResolver = sp.GetRequiredKeyedService<GrainInterfaceTypeToGrainTypeResolver>("rpc");
                return new GrainFactory(runtimeClient, referenceActivator, interfaceTypeResolver, interfaceToTypeResolver);
            });
            
            // Register keyed interfaces pointing to the keyed GrainFactory
            services.AddKeyedSingleton<IGrainFactory>("rpc", (sp, key) => sp.GetRequiredKeyedService<GrainFactory>("rpc"));
            services.AddKeyedSingleton<IInternalGrainFactory>("rpc", (sp, key) => sp.GetRequiredKeyedService<GrainFactory>("rpc"));
            
            // For standalone RPC client (without Orleans), also register as unkeyed
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
            
            // Cluster manifest provider for RPC with multi-server support
            // Use keyed service to avoid overriding Orleans client's manifest provider
            services.AddKeyedSingleton<MultiServerManifestProvider>("rpc");
            services.AddKeyedSingleton<IClusterManifestProvider>("rpc", (sp, key) => sp.GetRequiredKeyedService<MultiServerManifestProvider>("rpc"));
            
            // For standalone RPC client (without Orleans), register as unkeyed to support GrainPropertiesResolver
            // TryAddSingleton ensures Orleans client's manifest provider takes precedence when both are present
            services.TryAddSingleton<IClusterManifestProvider>(sp => sp.GetRequiredKeyedService<MultiServerManifestProvider>("rpc"));
            
            // Grain version manifest
            services.TryAddSingleton<GrainVersionManifest>();
            
            // Add grain cancellation token runtime
            services.TryAddSingleton<IGrainCancellationTokenRuntime, GrainCancellationTokenRuntime>();

            // Serialization
            services.AddSerializer(serializer =>
            {
                // Add the RPC abstractions assembly for protocol messages
                serializer.AddAssembly(typeof(Protocol.RpcMessage).Assembly);
                
                // Force load the metadata provider
                var metadataProviderType = typeof(Protocol.RpcMessage).Assembly.GetType("OrleansCodeGen.GranvilleRpcAbstractions.Metadata_GranvilleRpcAbstractions");
                if (metadataProviderType != null)
                {
                    serializer.Services.AddSingleton(typeof(IConfigureOptions<TypeManifestOptions>), metadataProviderType);
                }
                
                // Register VoidTaskResult codec as a safety net
                serializer.Services.AddSingleton<IFieldCodec<object>, Granville.Rpc.Serialization.VoidTaskResultCodec>();
            });
            services.AddSingleton<ITypeNameFilter, AllowGranvilleTypes>();
            // Note: GrainReferenceCodecProvider and GrainReferenceCopierProvider are internal
            // services.AddSingleton<ISpecializableCodec, GrainReferenceCodecProvider>();
            // services.AddSingleton<ISpecializableCopier, GrainReferenceCopierProvider>();
            services.AddSingleton<OnDeserializedCallbacks>();
            services.AddSingleton<MigrationContext.SerializationHooks>();
            services.AddSingleton<IPostConfigureOptions<OrleansJsonSerializerOptions>, ConfigureOrleansJsonSerializerOptions>();
            services.AddSingleton<OrleansJsonSerializer>();

            // RPC Session Factory for isolated serialization
            services.TryAddSingleton<RpcSerializationSessionFactory>();

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

        private class AllowGranvilleTypes : ITypeNameFilter
        {
            public bool? IsTypeNameAllowed(string typeName, string assemblyName)
            {
                if (assemblyName is { Length: > 0 } && assemblyName.Contains("Granville"))
                {
                    return true;
                }

                return null;
            }
        }

        private class ServicesAdded { }
    }
}
