using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Forkleans.Configuration;
using Forkleans.GrainReferences;
using Forkleans.Metadata;
using Forkleans.Runtime.Scheduler;

namespace Forkleans.Runtime
{
    internal partial class ActivationDataActivatorProvider : IGrainContextActivatorProvider
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IActivationWorkingSet _activationWorkingSet;
        private readonly ILogger<WorkItemGroup> _workItemGroupLogger;
        private readonly ILogger<Grain> _grainLogger;
        private readonly ILogger<ActivationTaskScheduler> _activationTaskSchedulerLogger;
        private readonly IOptions<SchedulingOptions> _schedulingOptions;
        private readonly GrainTypeSharedContextResolver _sharedComponentsResolver;
        private readonly GrainClassMap _grainClassMap;
        private readonly ILoggerFactory _loggerFactory;
        private readonly GrainReferenceActivator _grainReferenceActivator;

        public ActivationDataActivatorProvider(
            GrainClassMap grainClassMap,
            IServiceProvider serviceProvider,
            ILoggerFactory loggerFactory,
            GrainReferenceActivator grainReferenceActivator,
            GrainTypeSharedContextResolver sharedComponentsResolver,
            IActivationWorkingSet activationWorkingSet,
            ILogger<Grain> grainLogger,
            ILogger<WorkItemGroup> workItemGroupLogger,
            ILogger<ActivationTaskScheduler> activationTaskSchedulerLogger,
            IOptions<SchedulingOptions> schedulingOptions)
        {
            _activationWorkingSet = activationWorkingSet;
            _workItemGroupLogger = workItemGroupLogger;
            _grainLogger = grainLogger;
            _activationTaskSchedulerLogger = activationTaskSchedulerLogger;
            _schedulingOptions = schedulingOptions;
            _sharedComponentsResolver = sharedComponentsResolver;
            _grainClassMap = grainClassMap;
            _serviceProvider = serviceProvider;
            _loggerFactory = loggerFactory;
            _grainReferenceActivator = grainReferenceActivator;
        }

        public bool TryGet(GrainType grainType, out IGrainContextActivator activator)
        {
            if (!_grainClassMap.TryGetGrainClass(grainType, out var grainClass) || !typeof(IGrain).IsAssignableFrom(grainClass))
            {
                activator = null;
                return false;
            }

            var sharedContext = _sharedComponentsResolver.GetComponents(grainType);
            var instanceActivator = sharedContext.GetComponent<IGrainActivator>();
            if (instanceActivator is null)
            {
                throw new InvalidOperationException($"Could not find a suitable {nameof(IGrainActivator)} implementation for grain type {grainType}");
            }

            var innerActivator = new ActivationDataActivator(
                instanceActivator,
                _serviceProvider,
                sharedContext,
                _grainLogger,
                _workItemGroupLogger,
                _activationTaskSchedulerLogger,
                _schedulingOptions);

            if (sharedContext.PlacementStrategy is StatelessWorkerPlacement)
            {
                activator = new StatelessWorkerActivator(sharedContext, innerActivator);
            }
            else
            {
                activator = innerActivator;
            }

            return true;
        }

        private partial class ActivationDataActivator : IGrainContextActivator
        {
            private readonly ILogger<WorkItemGroup> _workItemGroupLogger;
            private readonly ILogger<ActivationTaskScheduler> _activationTaskSchedulerLogger;
            private readonly IOptions<SchedulingOptions> _schedulingOptions;
            private readonly IGrainActivator _grainActivator;
            private readonly IServiceProvider _serviceProvider;
            private readonly GrainTypeSharedContext _sharedComponents;
            private readonly ILogger<Grain> _grainLogger;
            private readonly Func<IGrainContext, WorkItemGroup> _createWorkItemGroup;

            public ActivationDataActivator(
                IGrainActivator grainActivator,
                IServiceProvider serviceProvider,
                GrainTypeSharedContext sharedComponents,
                ILogger<Grain> grainLogger,
                ILogger<WorkItemGroup> workItemGroupLogger,
                ILogger<ActivationTaskScheduler> activationTaskSchedulerLogger,
                IOptions<SchedulingOptions> schedulingOptions)
            {
                _workItemGroupLogger = workItemGroupLogger;
                _activationTaskSchedulerLogger = activationTaskSchedulerLogger;
                _schedulingOptions = schedulingOptions;
                _grainActivator = grainActivator;
                _serviceProvider = serviceProvider;
                _sharedComponents = sharedComponents;
                _grainLogger = grainLogger;
                _createWorkItemGroup = context => new WorkItemGroup(
                    context,
                    _workItemGroupLogger,
                    _activationTaskSchedulerLogger,
                    _schedulingOptions);
            }

            public IGrainContext CreateContext(GrainAddress activationAddress)
            {
                var context = new ActivationData(
                    activationAddress,
                    _createWorkItemGroup,
                    _serviceProvider,
                    _sharedComponents);

                RuntimeContext.SetExecutionContext(context, out var originalContext);

                try
                {
                    // Instantiate the grain itself
                    var instance = _grainActivator.CreateInstance(context);
                    context.SetGrainInstance(instance);
                }
                catch (Exception exception)
                {
                    LogErrorFailedToConstructGrain(_grainLogger, exception, activationAddress.GrainId);
                    throw;
                }
                finally
                {
                    RuntimeContext.ResetExecutionContext(originalContext);
                }

                return context;
            }

            [LoggerMessage(
                Level = LogLevel.Error,
                Message = "Failed to construct grain '{GrainId}'."
            )]
            private static partial void LogErrorFailedToConstructGrain(ILogger logger, Exception exception, GrainId grainId);
        }
    }

    internal class StatelessWorkerActivator : IGrainContextActivator
    {
        private readonly IGrainContextActivator _innerActivator;
        private readonly GrainTypeSharedContext _sharedContext;

        public StatelessWorkerActivator(GrainTypeSharedContext sharedContext, IGrainContextActivator innerActivator)
        {
            _innerActivator = innerActivator;
            _sharedContext = sharedContext;
        }

        public IGrainContext CreateContext(GrainAddress address) => new StatelessWorkerGrainContext(address, _sharedContext, _innerActivator);
    }
}
