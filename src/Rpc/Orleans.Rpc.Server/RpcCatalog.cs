using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Forkleans.Runtime;

namespace Forkleans.Rpc
{
    /// <summary>
    /// Simplified catalog for managing grain activations in RPC server.
    /// Unlike Orleans Catalog, this doesn't handle distributed activations.
    /// </summary>
    internal sealed class RpcCatalog : ILifecycleParticipant<IRpcServerLifecycle>
    {
        private readonly ILogger<RpcCatalog> _logger;
        private readonly ConcurrentDictionary<GrainId, IGrainContext> _activations;
        private readonly IServiceProvider _serviceProvider;

        public IServiceProvider ServiceProvider => _serviceProvider;

        public RpcCatalog(
            ILogger<RpcCatalog> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _activations = new ConcurrentDictionary<GrainId, IGrainContext>();
        }

        public void Participate(IRpcServerLifecycle lifecycle)
        {
            lifecycle.Subscribe<RpcCatalog>(
                RpcServerLifecycleStage.ActivatorsInit,
                OnInit,
                OnStop);
        }

        private Task OnInit(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Initializing RPC catalog");
            return Task.CompletedTask;
        }

        private Task OnStop(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping RPC catalog");
            
            // Deactivate all grains
            var tasks = new List<Task>();
            foreach (var activation in _activations.Values)
            {
                tasks.Add(DeactivateGrainAsync(activation));
            }

            return Task.WhenAll(tasks);
        }

        /// <summary>
        /// Gets or creates a grain activation for RPC.
        /// </summary>
        public async Task<IGrainContext> GetOrCreateActivationAsync(GrainId grainId)
        {
            if (_activations.TryGetValue(grainId, out var existing))
            {
                return existing;
            }

            // Create new activation
            var grainContext = await CreateActivationAsync(grainId);
            
            if (_activations.TryAdd(grainId, grainContext))
            {
                _logger.LogDebug("Created new activation for grain {GrainId}", grainId);
                return grainContext;
            }

            // Another thread created it first
            await DeactivateGrainAsync(grainContext);
            return _activations[grainId];
        }

        private Task<IGrainContext> CreateActivationAsync(GrainId grainId)
        {
            _logger.LogInformation("CreateActivationAsync: Starting activation for {GrainId}", grainId);
            
            // Bypass the Orleans activation system entirely for RPC mode
            // Create a simple grain instance directly using dependency injection
            var grainType = grainId.Type;
            var activationId = ActivationId.NewId();
            var address = new GrainAddress
            {
                GrainId = grainId,
                ActivationId = activationId,
                SiloAddress = null // No silo for RPC mode
            };

            _logger.LogInformation("CreateActivationAsync: Created address {Address}", address);
            
            try
            {
                // Instead of using the complex Orleans activator, create a simple wrapper
                _logger.LogInformation("Creating simple grain context wrapper for {GrainId}", grainId);
                var grainContext = new SimpleRpcGrainContext(address, grainType, _serviceProvider, _logger);
                
                _logger.LogInformation("Successfully created grain {GrainId} with type {GrainType}", 
                    grainId, grainContext.GrainInstance?.GetType().Name ?? "null");
                    
                return Task.FromResult<IGrainContext>(grainContext);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to activate grain {GrainId}", grainId);
                throw;
            }
        }

        private async Task DeactivateGrainAsync(IGrainContext grainContext)
        {
            try
            {
                // Use the Deactivate method instead of DeactivateAsync
                grainContext.Deactivate(new(DeactivationReasonCode.ShuttingDown, "RPC server stopping"));
                _activations.TryRemove(grainContext.GrainId, out _);
                
                // Give the grain some time to complete deactivation
                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deactivating grain {GrainId}", grainContext.GrainId);
            }
        }

        /// <summary>
        /// Dispatches a message to a grain activation.
        /// </summary>
        public async Task DispatchMessage(Message message)
        {
            var grainId = message.TargetGrain;
            var grainContext = await GetOrCreateActivationAsync(grainId);
            
            // Dispatch the message to the grain
            grainContext.ReceiveMessage(message);
        }
    }
}