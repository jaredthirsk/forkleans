using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Metadata;
using Orleans.Runtime;

namespace Granville.Rpc
{
    /// <summary>
    /// Ultra-simplified grain context for RPC mode that only provides the minimum needed functionality.
    /// </summary>
    internal class SimpleRpcGrainContext : IGrainContext
    {
        private readonly ILogger _logger;
        private readonly GrainClassMap _grainClassMap;

        public SimpleRpcGrainContext(GrainAddress address, GrainType grainType, IServiceProvider serviceProvider, ILogger logger)
        {
            Address = address;
            ActivationServices = serviceProvider;
            _logger = logger;
            _grainClassMap = serviceProvider.GetRequiredService<GrainClassMap>();


            // Get the grain class for this type
            if (!_grainClassMap.TryGetGrainClass(grainType, out var grainClass))
            {
                throw new InvalidOperationException($"Could not find grain class for grain type {grainType}");
            }


            // Create the grain instance using dependency injection
            try
            {
                GrainInstance = ActivatorUtilities.CreateInstance(serviceProvider, grainClass);
                
                // If the grain derives from Grain, we need to set its GrainContext property
                if (GrainInstance is Orleans.Grain grain)
                {
                    // Use reflection to set the GrainContext property since it has a private setter
                    var grainContextProperty = typeof(Orleans.Grain).GetProperty("GrainContext", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (grainContextProperty != null)
                    {
                        grainContextProperty.SetValue(grain, this);
                    }
                    else
                    {
                        _logger.LogError("SimpleRpcGrainContext: Could not find GrainContext property on Grain type");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SimpleRpcGrainContext: Failed to create grain instance of type {GrainClass}", grainClass.Name);
                throw;
            }
        }

        public GrainAddress Address { get; }

        public GrainId GrainId => Address.GrainId;

        public object GrainInstance { get; }

        public ActivationId ActivationId => Address.ActivationId;

        public bool IsValid => true;

        public IServiceProvider ActivationServices { get; }

        public IGrainLifecycle ObservableLifecycle => new NoOpGrainLifecycle();

        public IWorkItemScheduler Scheduler => throw new NotSupportedException("Scheduler not supported in RPC mode");

        public GrainReference GrainReference => throw new NotSupportedException("GrainReference not supported in RPC mode");

        public Task Deactivated => Task.CompletedTask;

        public void Activate(Dictionary<string, object> requestContext, CancellationToken cancellationToken = default)
        {
            // No-op in RPC mode
        }

        public void Deactivate(DeactivationReason reason, CancellationToken cancellationToken = default)
        {
            // No-op in RPC mode
        }

        public TComponent GetComponent<TComponent>() where TComponent : class
        {
            if (GrainInstance is TComponent grainResult)
            {
                return grainResult;
            }

            return ActivationServices.GetService<TComponent>();
        }

        public void SetComponent<TComponent>(TComponent instance) where TComponent : class
        {
            // No-op in RPC mode
        }

        public void ReceiveMessage(object message)
        {
            throw new NotSupportedException("Direct message receiving not supported in RPC mode");
        }

        public void Rehydrate(IRehydrationContext context)
        {
            // No-op in RPC mode
        }

        public void Migrate(Dictionary<string, object> requestContext, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Migration not supported in RPC mode");
        }

        public TTarget GetTarget<TTarget>() where TTarget : class => (TTarget)GrainInstance;

        public bool Equals(IGrainContext other) => ReferenceEquals(this, other);
    }

    /// <summary>
    /// No-op implementation of IGrainLifecycle for RPC mode.
    /// </summary>
    internal class NoOpGrainLifecycle : IGrainLifecycle
    {
        public IDisposable Subscribe<T>(string observerName, int stage, ILifecycleObserver observer) where T : ILifecycleObservable
        {
            return new NoOpDisposable();
        }

        public IDisposable Subscribe(string observerName, int stage, ILifecycleObserver observer)
        {
            return new NoOpDisposable();
        }

        public Task OnStart(CancellationToken ct = default) => Task.CompletedTask;

        public Task OnStop(CancellationToken ct = default) => Task.CompletedTask;

        public void AddMigrationParticipant(IGrainMigrationParticipant participant)
        {
            // No-op
        }

        public void RemoveMigrationParticipant(IGrainMigrationParticipant participant)
        {
            // No-op
        }

        public List<IGrainMigrationParticipant> GetMigrationParticipants() => new List<IGrainMigrationParticipant>();
    }

    internal class NoOpDisposable : IDisposable
    {
        public void Dispose() { }
    }
}