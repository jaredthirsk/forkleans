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
            // Don't register as IClusterClient to avoid conflicts with Orleans client
            // services.TryAddFromExisting<IClusterClient, RpcClient>();
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
            // RpcGrainFactory is RPC-specific, no conflict with Orleans
            services.TryAddSingleton<RpcGrainFactory>(sp => new RpcGrainFactory(
                sp.GetRequiredService<IRuntimeClient>(),
                sp.GetRequiredService<GrainReferenceActivator>(),
                sp.GetRequiredService<GrainInterfaceTypeResolver>(),
                sp.GetRequiredKeyedService<GrainInterfaceTypeToGrainTypeResolver>("rpc"),
                sp.GetRequiredService<RpcClient>(),
                sp.GetRequiredService<ILogger<RpcGrainReference>>(),
                sp.GetRequiredService<Serializer>()));
            
            // Note: The keyed GrainFactory registration is done later with proper RPC resolver
            
            // For standalone RPC client (without Orleans), register as unkeyed to support existing code
            // TryAddSingleton ensures Orleans client's GrainFactory takes precedence when both are present
            services.TryAddSingleton<GrainFactory>(sp => sp.GetRequiredService<RpcGrainFactory>());
            
            services.TryAddSingleton<InterfaceToImplementationMappingCache>();
            // Use keyed singleton for RPC to avoid conflicts with Orleans client
            services.TryAddKeyedSingleton<GrainInterfaceTypeToGrainTypeResolver>("rpc", (sp, key) => new GrainInterfaceTypeToGrainTypeResolver(
                sp.GetRequiredKeyedService<IClusterManifestProvider>("rpc")));
            services.TryAddSingleton<GrainReferenceActivator>();
            // Add RPC grain reference provider first so it takes precedence
            services.AddSingleton<IGrainReferenceActivatorProvider, RpcGrainReferenceActivatorProvider>();
            services.AddSingleton<IGrainReferenceActivatorProvider, GrainReferenceActivatorProvider>();
            services.AddSingleton<IGrainReferenceActivatorProvider, UntypedGrainReferenceActivatorProvider>();
            services.TryAddSingleton<RpcProvider>();
            services.TryAddSingleton<IGrainReferenceRuntime, GrainReferenceRuntime>();
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
            // Register GrainFactory as keyed service with RPC's resolver
            services.AddKeyedSingleton<GrainFactory>("rpc", (sp, key) => 
            {
                var runtimeClient = sp.GetRequiredService<IRuntimeClient>();
                var referenceActivator = sp.GetRequiredService<GrainReferenceActivator>();
                var interfaceTypeResolver = sp.GetRequiredService<GrainInterfaceTypeResolver>();
                // Use the keyed GrainInterfaceTypeToGrainTypeResolver for RPC
                var interfaceToTypeResolver = sp.GetRequiredKeyedService<GrainInterfaceTypeToGrainTypeResolver>("rpc");
                return new GrainFactory(runtimeClient, referenceActivator, interfaceTypeResolver, interfaceToTypeResolver);
            });
            
            // Register interface mappings as keyed services for RPC
            services.AddKeyedSingleton<IGrainFactory>("rpc", (sp, key) => sp.GetRequiredKeyedService<GrainFactory>("rpc"));
            services.AddKeyedSingleton<IInternalGrainFactory>("rpc", (sp, key) => sp.GetRequiredKeyedService<GrainFactory>("rpc"));
            
            // For standalone RPC client, register as unkeyed (Orleans client takes precedence when both are present)
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
            services.AddSingleton<IPostConfigureOptions<ForkleansJsonSerializerOptions>, ConfigureOrleansJsonSerializerOptions>();
            services.AddSingleton<ForkleansJsonSerializer>();

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
                if (assemblyName is { Length: > 0 } && assemblyName.Contains("Forkleans"))
                {
                    return true;
                }

                return null;
            }
        }

        private class ServicesAdded { }
    }
}
