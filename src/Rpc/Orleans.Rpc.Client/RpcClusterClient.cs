using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace Granville.Rpc
{
    /// <summary>
    /// Client for communicating with RPC servers.
    /// This is a thin wrapper following Orleans' pattern of separating public API from implementation.
    /// </summary>
    internal class RpcClusterClient : IInternalClusterClient, IRpcClient, IHostedService
    {
        private readonly OutsideRpcClient _runtimeClient;
        private readonly ILogger<RpcClusterClient> _logger;
        private readonly IClusterClientLifecycle _clusterClientLifecycle;

        /// <summary>
        /// Initializes a new instance of the <see cref="RpcClusterClient"/> class.
        /// </summary>
        public RpcClusterClient(
            IServiceProvider serviceProvider,
            OutsideRpcClient runtimeClient,
            ILoggerFactory loggerFactory,
            IClusterClientLifecycle clusterClientLifecycle)
        {
            ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _runtimeClient = runtimeClient ?? throw new ArgumentNullException(nameof(runtimeClient));
            _logger = loggerFactory?.CreateLogger<RpcClusterClient>() ?? throw new ArgumentNullException(nameof(loggerFactory));
            _clusterClientLifecycle = clusterClientLifecycle ?? throw new ArgumentNullException(nameof(clusterClientLifecycle));
            
            ValidateSystemConfiguration(serviceProvider);
            
            // Register all lifecycle participants
            var lifecycleParticipants = ServiceProvider.GetServices<ILifecycleParticipant<IClusterClientLifecycle>>();
            foreach (var participant in lifecycleParticipants)
            {
                participant?.Participate(_clusterClientLifecycle);
            }
        }

        /// <inheritdoc />
        public IServiceProvider ServiceProvider { get; }

        /// <inheritdoc />
        public bool IsInitialized => _runtimeClient.IsInitialized;

        /// <inheritdoc />
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting RPC client");
            
            await _runtimeClient.StartAsync(cancellationToken).ConfigureAwait(false);
            
            if (_clusterClientLifecycle is ILifecycleSubject lifecycleSubject)
            {
                await lifecycleSubject.OnStart(cancellationToken).ConfigureAwait(false);
            }
            
            _logger.LogInformation("RPC client started successfully");
        }

        /// <inheritdoc />
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("RPC client shutting down");

                if (_clusterClientLifecycle is ILifecycleSubject lifecycleSubject)
                {
                    await lifecycleSubject.OnStop(cancellationToken).ConfigureAwait(false);
                }
                
                await _runtimeClient.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _logger.LogInformation("RPC client shutdown completed");
            }
        }

        /// <inheritdoc />
        public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithGuidKey 
            => _runtimeClient.InternalGrainFactory.GetGrain<TGrainInterface>(primaryKey, grainClassNamePrefix);

        /// <inheritdoc />
        public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithIntegerKey 
            => _runtimeClient.InternalGrainFactory.GetGrain<TGrainInterface>(primaryKey, grainClassNamePrefix);

        /// <inheritdoc />
        public TGrainInterface GetGrain<TGrainInterface>(string primaryKey, string grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithStringKey 
            => _runtimeClient.InternalGrainFactory.GetGrain<TGrainInterface>(primaryKey, grainClassNamePrefix);

        /// <inheritdoc />
        public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string keyExtension, string grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithGuidCompoundKey 
            => _runtimeClient.InternalGrainFactory.GetGrain<TGrainInterface>(primaryKey, keyExtension, grainClassNamePrefix);

        /// <inheritdoc />
        public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string keyExtension, string grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithIntegerCompoundKey 
            => _runtimeClient.InternalGrainFactory.GetGrain<TGrainInterface>(primaryKey, keyExtension, grainClassNamePrefix);

        /// <inheritdoc />
        public TGrainInterface GetGrain<TGrainInterface>(GrainId grainId) 
            where TGrainInterface : IAddressable
            => _runtimeClient.InternalGrainFactory.GetGrain<TGrainInterface>(grainId);

        /// <inheritdoc />
        public IAddressable GetGrain(GrainId grainId)
            => _runtimeClient.InternalGrainFactory.GetGrain(grainId);

        /// <inheritdoc />
        public IAddressable GetGrain(GrainId grainId, GrainInterfaceType interfaceType)
            => _runtimeClient.InternalGrainFactory.GetGrain(grainId, interfaceType);

        /// <inheritdoc />
        public IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey)
            => _runtimeClient.InternalGrainFactory.GetGrain(grainInterfaceType, grainPrimaryKey);

        /// <inheritdoc />
        public IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey)
            => _runtimeClient.InternalGrainFactory.GetGrain(grainInterfaceType, grainPrimaryKey);

        /// <inheritdoc />
        public IGrain GetGrain(Type grainInterfaceType, string grainPrimaryKey)
            => _runtimeClient.InternalGrainFactory.GetGrain(grainInterfaceType, grainPrimaryKey);

        /// <inheritdoc />
        public IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey, string grainClassNamePrefix)
            => _runtimeClient.InternalGrainFactory.GetGrain(grainInterfaceType, grainPrimaryKey, grainClassNamePrefix);

        /// <inheritdoc />
        public IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey, string grainClassNamePrefix)
            => _runtimeClient.InternalGrainFactory.GetGrain(grainInterfaceType, grainPrimaryKey, grainClassNamePrefix);

        /// <inheritdoc />
        public TGrainObserverInterface CreateObjectReference<TGrainObserverInterface>(IGrainObserver obj) 
            where TGrainObserverInterface : IGrainObserver
        {
            throw new NotSupportedException("Grain observers are not supported in RPC mode");
        }

        /// <inheritdoc />
        public void DeleteObjectReference<TGrainObserverInterface>(IGrainObserver obj) 
            where TGrainObserverInterface : IGrainObserver
        {
            // Not supported in RPC mode
        }

        /// <inheritdoc />
        public async Task WaitForManifestAsync(TimeSpan timeout = default)
        {
            await _runtimeClient.WaitForManifestAsync(timeout).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public TGrainObserverInterface CreateObjectReference<TGrainObserverInterface>(IAddressable obj) 
            where TGrainObserverInterface : IAddressable
        {
            return _runtimeClient.InternalGrainFactory.CreateObjectReference<TGrainObserverInterface>(obj);
        }

        /// <inheritdoc />
        public TGrainInterface GetSystemTarget<TGrainInterface>(GrainType grainType, SiloAddress destination)
            where TGrainInterface : ISystemTarget
        {
            return _runtimeClient.InternalGrainFactory.GetSystemTarget<TGrainInterface>(grainType, destination);
        }

        /// <inheritdoc />
        public TGrainInterface GetSystemTarget<TGrainInterface>(GrainId grainId)
            where TGrainInterface : ISystemTarget
        {
            return _runtimeClient.InternalGrainFactory.GetSystemTarget<TGrainInterface>(grainId);
        }

        /// <inheritdoc />
        TGrainInterface IInternalGrainFactory.Cast<TGrainInterface>(IAddressable grain)
        {
            return _runtimeClient.InternalGrainFactory.Cast<TGrainInterface>(grain);
        }

        /// <inheritdoc />
        public object Cast(IAddressable grain, Type interfaceType)
        {
            return _runtimeClient.InternalGrainFactory.Cast(grain, interfaceType);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _runtimeClient?.Dispose();
        }

        private static void ValidateSystemConfiguration(IServiceProvider serviceProvider)
        {
            var validators = serviceProvider.GetServices<IConfigurationValidator>();
            foreach (var validator in validators)
            {
                validator.ValidateConfiguration();
            }
        }
    }
}