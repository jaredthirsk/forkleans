using System;
using System.Linq;
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
    /// Hosted service for managing the RPC server lifecycle.
    /// </summary>
    internal sealed class RpcServerHostedService : IHostedService
    {
        private readonly ILogger<RpcServerHostedService> _logger;
        private readonly IRpcServerLifecycle _lifecycle;
        private readonly IServiceProvider _serviceProvider;

        public RpcServerHostedService(
            ILogger<RpcServerHostedService> logger,
            IRpcServerLifecycle lifecycle,
            IServiceProvider serviceProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting RPC server");
            
            try
            {
                var lifecycleSubject = _lifecycle as ILifecycleSubject;
                if (lifecycleSubject == null)
                {
                    _logger.LogError("Lifecycle is not ILifecycleSubject. Type: {Type}", _lifecycle?.GetType().FullName);
                    throw new InvalidOperationException("Lifecycle must implement ILifecycleSubject");
                }
                
                // Ensure lifecycle participants are created
                var lifecycleParticipants = _serviceProvider.GetServices<ILifecycleParticipant<IRpcServerLifecycle>>().ToList();
                _logger.LogInformation("Found {Count} lifecycle participants", lifecycleParticipants.Count);
                
                // Make sure each participant has registered with the lifecycle
                foreach (var participant in lifecycleParticipants)
                {
                    _logger.LogInformation("Registering participant {Type} with lifecycle", participant.GetType().Name);
                    participant.Participate(_lifecycle);
                }
                
                _logger.LogInformation("Starting lifecycle");
                await lifecycleSubject.OnStart(cancellationToken);
                _logger.LogInformation("RPC server started successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start RPC server");
                throw;
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping RPC server");
            
            try
            {
                await (_lifecycle as ILifecycleSubject)?.OnStop(cancellationToken);
                _logger.LogInformation("RPC server stopped successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while stopping RPC server");
                throw;
            }
        }
    }
}