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
            services.TryAddSingleton<RpcGrainFactory>();
            services.TryAddSingleton<GrainFactory>(sp => sp.GetRequiredService<RpcGrainFactory>());
            services.TryAddSingleton<InterfaceToImplementationMappingCache>();
            services.TryAddSingleton<GrainInterfaceTypeToGrainTypeResolver>();
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
            
            // Grain activation
            services.TryAddSingleton<RpcCatalog>();
            services.AddFromExisting<ILifecycleParticipant<IRpcServerLifecycle>, RpcCatalog>();
            services.AddSingleton<GrainContextActivator>();
            services.AddSingleton<IConfigureGrainTypeComponents, ConfigureDefaultGrainActivator>();
            services.AddSingleton<IGrainContextActivatorProvider, ActivationDataActivatorProvider>();
            services.AddSingleton<IGrainContextAccessor, GrainContextAccessor>();
            services.AddSingleton<GrainTypeSharedContextResolver>();
            services.AddSingleton<IActivationWorkingSet, ActivationWorkingSet>();
            
            // Scoped to a grain activation
            // Note: RuntimeContext is internal, using a factory that will be provided at activation time
            services.AddScoped<IGrainContext>(sp => throw new InvalidOperationException("Grain context must be set during activation"));

            // Type metadata
            services.AddSingleton<RpcManifestProvider>();
            services.AddFromExisting<IClusterManifestProvider, RpcManifestProvider>();
            services.AddSingleton<GrainClassMap>(sp => sp.GetRequiredService<RpcManifestProvider>().GrainTypeMap);
            services.AddSingleton<GrainTypeResolver>();
            services.AddSingleton<IGrainTypeProvider, AttributeGrainTypeProvider>();
            services.AddSingleton<GrainPropertiesResolver>();
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
            services.AddSingleton<IPostConfigureOptions<OrleansJsonSerializerOptions>, ConfigureOrleansJsonSerializerOptions>();
            services.AddSingleton<OrleansJsonSerializer>();

            // RPC Server
            services.TryAddSingleton<RpcServer>();
            services.AddFromExisting<ILifecycleParticipant<IRpcServerLifecycle>, RpcServer>();
            
            // RPC Transport abstraction
            services.TryAddSingleton<IRpcTransportFactory, DefaultRpcTransportFactory>();
            
            // RPC Protocol
            services.TryAddSingleton<Protocol.RpcMessageSerializer>();
            
            // Configure formatters for required options
            services.ConfigureFormatter<RpcServerOptions>();
            services.ConfigureFormatter<RpcTransportOptions>();
            services.ConfigureFormatter<ClusterOptions>();
            
            // TypeConverter for manifest provider
            services.TryAddSingleton<TypeConverter>();
            
            // Interface mapping cache for method invocation
            services.TryAddSingleton<InterfaceToImplementationMappingCache>();
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