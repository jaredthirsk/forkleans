#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Forkleans.Configuration;
using Forkleans.Configuration.Internal;
using Forkleans.GrainReferences;
using Forkleans.Metadata;
using Forkleans.Providers;
using Forkleans.Runtime;
using Forkleans.Runtime.Messaging;
using Forkleans.Runtime.Placement;
using Forkleans.Runtime.Scheduler;
using Forkleans.Runtime.Versions;
using Forkleans.Rpc.Configuration;
using Forkleans.Rpc.Transport;
using Forkleans.Serialization;
using Forkleans.Serialization.Cloning;
using Forkleans.Serialization.Internal;
using Forkleans.Serialization.Serializers;
using Forkleans.Serialization.TypeSystem;
using Forkleans.Statistics;
using Forkleans.Timers;
using Forkleans.Timers.Internal;
using System;
using System.Linq;
using System.Net;

namespace Forkleans.Rpc.Hosting
{
    internal static class DefaultRpcServerServices
    {
        private static readonly ServiceDescriptor ServiceDescriptor = new(typeof(ServicesAdded), new ServicesAdded());

        internal static void AddDefaultServices(IRpcServerBuilder builder)
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

            // Options formatting
            services.TryAddSingleton(typeof(IOptionFormatter<>), typeof(DefaultOptionsFormatter<>));
            services.TryAddSingleton(typeof(IOptionFormatterResolver<>), typeof(DefaultOptionsFormatterResolver<>));

            // Core RPC server components
            services.AddHostedService<RpcServerHostedService>();
            
            // RPC-specific lifecycle
            services.TryAddSingleton<RpcServerLifecycleSubject>();
            services.TryAddFromExisting<IRpcServerLifecycleSubject, RpcServerLifecycleSubject>();
            services.TryAddFromExisting<IRpcServerLifecycle, RpcServerLifecycleSubject>();
            services.TryAddFromExisting<ILifecycleSubject, RpcServerLifecycleSubject>();
            
            // Runtime client for grain factory
            services.TryAddSingleton<RpcRuntimeClient>();
            services.TryAddFromExisting<IRuntimeClient, RpcRuntimeClient>();

            // Minimal local server details
            services.PostConfigure<RpcServerOptions>(options => options.ServerName ??= $"RpcServer_{Guid.NewGuid().ToString("N")[..5]}");
            services.TryAddSingleton<ILocalRpcServerDetails, LocalRpcServerDetails>();

            // Statistics (minimal set)
            services.AddSingleton<IEnvironmentStatisticsProvider, EnvironmentStatisticsProvider>();
#pragma warning disable 618
            services.AddSingleton<OldEnvironmentStatistics>();
            services.AddFromExisting<IAppEnvironmentStatistics, OldEnvironmentStatistics>();
            services.AddFromExisting<IHostEnvironmentStatistics, OldEnvironmentStatistics>();
#pragma warning restore 618

            // Timer support
            services.TryAddSingleton<ITimerRegistry, TimerRegistry>();
            services.TryAddSingleton<IAsyncTimerFactory, AsyncTimerFactory>();

            // Grain runtime (simplified for RPC)
            services.TryAddSingleton<GrainRuntime>();
            services.TryAddSingleton<IGrainRuntime, GrainRuntime>();
            // Note: IGrainCancellationTokenRuntime and ICancellationSourcesExtension are internal types
            // We'll need to handle cancellation differently in RPC mode
            services.AddTransient<CancellationSourcesExtension>();
            
            // Grain factory and references
            // RpcGrainFactory is RPC-specific, no conflict with Orleans
            services.TryAddSingleton<RpcGrainFactory>(sp => new RpcGrainFactory(
                sp.GetRequiredService<IRuntimeClient>(),
                sp.GetRequiredService<GrainReferenceActivator>(),
                sp.GetRequiredService<GrainInterfaceTypeResolver>(),
                sp.GetRequiredKeyedService<GrainInterfaceTypeToGrainTypeResolver>("rpc"),
                sp.GetRequiredService<ILocalRpcServerDetails>()));
            
            // Register GrainFactory as keyed service for RPC to avoid overriding Orleans client's GrainFactory
            services.AddKeyedSingleton<GrainFactory>("rpc", (sp, key) => sp.GetRequiredService<RpcGrainFactory>());
            
            // For standalone RPC server (without Orleans), register as unkeyed to support existing code
            // TryAddSingleton ensures Orleans client's GrainFactory takes precedence when both are present
            services.TryAddSingleton<GrainFactory>(sp => sp.GetRequiredService<RpcGrainFactory>());
            
            services.TryAddSingleton<InterfaceToImplementationMappingCache>();
            // Use keyed singleton for RPC to avoid conflicts with Orleans client
            services.TryAddKeyedSingleton<GrainInterfaceTypeToGrainTypeResolver>("rpc", (sp, key) => new GrainInterfaceTypeToGrainTypeResolver(
                sp.GetRequiredKeyedService<IClusterManifestProvider>("rpc")));
            // Register interface mappings as keyed services for RPC
            services.AddKeyedSingleton<IGrainFactory>("rpc", (sp, key) => sp.GetRequiredKeyedService<GrainFactory>("rpc"));
            services.AddKeyedSingleton<IInternalGrainFactory>("rpc", (sp, key) => sp.GetRequiredKeyedService<GrainFactory>("rpc"));
            
            // For standalone RPC server, register as unkeyed (Orleans client takes precedence when both are present)
            services.TryAddFromExisting<IGrainFactory, GrainFactory>();
            services.TryAddFromExisting<IInternalGrainFactory, GrainFactory>();
            services.TryAddSingleton<IGrainReferenceRuntime, GrainReferenceRuntime>();
            services.TryAddSingleton<GrainReferenceActivator>();
            services.AddSingleton<IGrainReferenceActivatorProvider, GrainReferenceActivatorProvider>();
            services.AddSingleton<IGrainReferenceActivatorProvider, UntypedGrainReferenceActivatorProvider>();
            
            // RPC provider
            services.TryAddSingleton<RpcProvider>();
            services.TryAddSingleton<GrainBindingsResolver>();
            
            // Simplified activation directory (no distribution needed)
            services.TryAddSingleton<RpcActivationDirectory>();
            services.TryAddSingleton<GrainCountStatistics>();
            
            // Grain activation - simplified for RPC mode
            services.TryAddSingleton<RpcCatalog>();
            services.AddFromExisting<ILifecycleParticipant<IRpcServerLifecycle>, RpcCatalog>();
            
            // Scoped to a grain activation
            // Note: RuntimeContext is internal, using a factory that will be provided at activation time
            services.AddScoped<IGrainContext>(sp => throw new InvalidOperationException("Grain context must be set during activation"));

            // Type metadata
            // Use keyed service to avoid overriding Orleans client's manifest provider
            services.AddKeyedSingleton<RpcManifestProvider>("rpc");
            services.AddKeyedSingleton<IClusterManifestProvider>("rpc", (sp, key) => sp.GetRequiredKeyedService<RpcManifestProvider>("rpc"));
            // Register GrainClassMap as keyed to avoid conflicts with Orleans
            services.AddKeyedSingleton<GrainClassMap>("rpc", (sp, key) => sp.GetRequiredKeyedService<RpcManifestProvider>("rpc").GrainTypeMap);
            // For RPC internal use, resolve the keyed version
            services.TryAddSingleton<GrainClassMap>(sp => sp.GetRequiredKeyedService<GrainClassMap>("rpc"));
            services.AddSingleton<GrainTypeResolver>();
            services.AddSingleton<IGrainTypeProvider, AttributeGrainTypeProvider>();
            // Register GrainPropertiesResolver as keyed service for RPC
            services.AddKeyedSingleton<GrainPropertiesResolver>("rpc", (sp, key) => 
                new GrainPropertiesResolver(
                    sp.GetRequiredKeyedService<IClusterManifestProvider>("rpc")
                ));
            // For standalone mode, also register as unkeyed
            services.TryAddSingleton<GrainPropertiesResolver>(sp => 
                sp.GetRequiredKeyedService<GrainPropertiesResolver>("rpc"));
            services.AddSingleton<GrainInterfaceTypeResolver>();
            services.AddSingleton<IGrainInterfaceTypeProvider, AttributeGrainInterfaceTypeProvider>();
            // Note: AttributeGrainInterfacePropertiesProvider is internal, removed for now
            services.AddSingleton<IGrainPropertiesProvider, AttributeGrainPropertiesProvider>();
            services.AddSingleton<IGrainPropertiesProvider, AttributeGrainBindingsProvider>();
            services.AddSingleton<IGrainInterfacePropertiesProvider, TypeNameGrainPropertiesProvider>();
            services.AddSingleton<IGrainPropertiesProvider, TypeNameGrainPropertiesProvider>();
            services.AddSingleton<IGrainPropertiesProvider, ImplementedInterfaceProvider>();
            
            // Configure GrainTypeOptions from TypeManifestOptions
            services.TryAddSingleton<IConfigureOptions<GrainTypeOptions>, DefaultGrainTypeOptionsProvider>();
            
            // Grain versioning support (simplified for RPC)
            services.AddSingleton<GrainVersionManifest>();

            // Message factory
            services.TryAddSingleton<MessageFactory>();
            services.TryAddSingleton<MessagingTrace>();

            // Serialization
            services.AddSerializer(serializer =>
            {
                // Add the RPC abstractions assembly for protocol messages
                serializer.AddAssembly(typeof(Protocol.RpcMessage).Assembly);
            });
            services.AddSingleton<ITypeNameFilter, AllowForkleanTypes>();
            // Note: GrainReferenceCodecProvider and GrainReferenceCopierProvider are internal types
            // Serialization will handle grain references automatically
            services.AddSingleton<OnDeserializedCallbacks>();
            services.AddSingleton<IPostConfigureOptions<ForkleansJsonSerializerOptions>, ConfigureOrleansJsonSerializerOptions>();
            services.AddSingleton<ForkleansJsonSerializer>();

            // RPC Server
            services.TryAddSingleton<RpcServer>(sp => new RpcServer(
                sp.GetRequiredService<ILogger<RpcServer>>(),
                sp.GetRequiredService<ILocalRpcServerDetails>(),
                sp.GetRequiredService<IOptions<RpcServerOptions>>(),
                sp.GetRequiredService<IRpcTransportFactory>(),
                sp.GetRequiredService<IRpcServerLifecycle>(),
                sp.GetRequiredService<RpcCatalog>(),
                sp.GetRequiredService<MessageFactory>(),
                sp.GetRequiredService<Forkleans.Serialization.Serializer>(),
                sp.GetRequiredService<IOptions<ClientMessagingOptions>>(),
                sp.GetRequiredService<ILoggerFactory>(),
                sp.GetRequiredKeyedService<IClusterManifestProvider>("rpc"),
                sp.GetRequiredKeyedService<GrainInterfaceTypeToGrainTypeResolver>("rpc"),
                sp));
            services.AddFromExisting<ILifecycleParticipant<IRpcServerLifecycle>, RpcServer>();
            
            // RPC Transport abstraction
            services.TryAddSingleton<IRpcTransportFactory, DefaultRpcTransportFactory>();
            
            // RPC Protocol
            services.TryAddSingleton<Protocol.RpcMessageSerializer>();
            
            // Configure formatters for required options
            services.ConfigureFormatter<RpcServerOptions>();
            services.ConfigureFormatter<RpcTransportOptions>();
            services.ConfigureFormatter<ClusterOptions>();
            
            // Configure messaging options - use ClientMessagingOptions as a concrete implementation
            services.Configure<ClientMessagingOptions>(options =>
            {
                options.ResponseTimeout = TimeSpan.FromSeconds(30);
                options.MaxMessageBodySize = 128 * 1024 * 1024; // 128MB
            });
            
            // TypeConverter for manifest provider
            services.TryAddSingleton<TypeConverter>();
            
            // Interface mapping cache for method invocation
            services.TryAddSingleton<InterfaceToImplementationMappingCache>();
            
            // Placement services (needed for grain activation)
            services.TryAddSingleton<PlacementStrategyResolver>();
            services.TryAddSingleton<IPlacementStrategyResolver, ClientObserverPlacementStrategyResolver>();
            services.TryAddSingleton<PlacementStrategy, RandomPlacement>();
            
            // Adapter to provide ILocalSiloDetails from RPC server details
            services.TryAddSingleton<ILocalSiloDetails, RpcSiloDetailsAdapter>();
            
            // Grain cancellation token runtime
            services.TryAddSingleton<IGrainCancellationTokenRuntime, GrainCancellationTokenRuntime>();
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

        private class RpcSiloDetailsAdapter : ILocalSiloDetails
        {
            private readonly ILocalRpcServerDetails _rpcServerDetails;
            private readonly SiloAddress _siloAddress;

            public RpcSiloDetailsAdapter(ILocalRpcServerDetails rpcServerDetails)
            {
                _rpcServerDetails = rpcServerDetails;
                // Create a pseudo SiloAddress from RPC server details
                _siloAddress = SiloAddress.New(_rpcServerDetails.ServerEndpoint, 0);
            }

            public string Name => _rpcServerDetails.ServerName;
            public SiloAddress SiloAddress => _siloAddress;
            public SiloAddress GatewayAddress => _siloAddress;
            public string ClusterId => "rpc-cluster";
            public string DnsHostName => _rpcServerDetails.ServerEndpoint.Address.ToString();
            public IPEndPoint SiloListeningEndpoint => _rpcServerDetails.ServerEndpoint;
            public IPEndPoint GatewayListeningEndpoint => _rpcServerDetails.ServerEndpoint;
        }

        private class ServicesAdded { }
    }
}