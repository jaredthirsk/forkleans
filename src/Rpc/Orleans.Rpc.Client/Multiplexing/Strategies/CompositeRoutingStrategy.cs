using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Granville.Rpc.Multiplexing.Strategies
{
    /// <summary>
    /// Combines multiple routing strategies with predicates.
    /// </summary>
    public class CompositeRoutingStrategy : IGrainRoutingStrategy
    {
        private readonly ILogger<CompositeRoutingStrategy> _logger;
        private readonly List<(Func<Type, bool> predicate, IGrainRoutingStrategy strategy)> _strategies;
        private IGrainRoutingStrategy _defaultStrategy;

        public CompositeRoutingStrategy(ILogger<CompositeRoutingStrategy> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _strategies = new List<(Func<Type, bool>, IGrainRoutingStrategy)>();
        }

        /// <summary>
        /// Adds a routing strategy that will be used when the predicate returns true.
        /// </summary>
        public void AddStrategy(Func<Type, bool> predicate, IGrainRoutingStrategy strategy)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            if (strategy == null) throw new ArgumentNullException(nameof(strategy));
            
            _strategies.Add((predicate, strategy));
            _logger.LogInformation("Added routing strategy {StrategyType} to composite",
                strategy.GetType().Name);
        }

        /// <summary>
        /// Sets the default strategy to use when no predicates match.
        /// </summary>
        public void SetDefaultStrategy(IGrainRoutingStrategy strategy)
        {
            _defaultStrategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
            _logger.LogInformation("Set default routing strategy to {StrategyType}",
                strategy.GetType().Name);
        }

        public async Task<string> SelectServerAsync(
            Type grainInterface,
            string grainKey,
            IReadOnlyDictionary<string, IServerDescriptor> servers,
            IRoutingContext context)
        {
            if (servers == null || servers.Count == 0)
            {
                _logger.LogWarning("No servers available for routing");
                return null;
            }

            // Find the first matching strategy
            foreach (var (predicate, strategy) in _strategies)
            {
                try
                {
                    if (predicate(grainInterface))
                    {
                        _logger.LogDebug("Using strategy {StrategyType} for grain {Interface}",
                            strategy.GetType().Name, grainInterface.Name);
                        
                        var result = await strategy.SelectServerAsync(grainInterface, grainKey, servers, context);
                        if (!string.IsNullOrEmpty(result))
                        {
                            return result;
                        }
                        
                        _logger.LogWarning("Strategy {StrategyType} returned no server for grain {Interface}",
                            strategy.GetType().Name, grainInterface.Name);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in routing strategy {StrategyType} for grain {Interface}",
                        strategy.GetType().Name, grainInterface.Name);
                }
            }

            // Use default strategy if configured
            if (_defaultStrategy != null)
            {
                _logger.LogDebug("Using default strategy {StrategyType} for grain {Interface}",
                    _defaultStrategy.GetType().Name, grainInterface.Name);
                
                try
                {
                    return await _defaultStrategy.SelectServerAsync(grainInterface, grainKey, servers, context);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in default routing strategy for grain {Interface}",
                        grainInterface.Name);
                }
            }

            // Last resort - primary server
            var primary = servers.Values.FirstOrDefault(s => 
                s.IsPrimary && 
                s.HealthStatus != ServerHealthStatus.Offline &&
                s.HealthStatus != ServerHealthStatus.Unhealthy);
            
            if (primary != null)
            {
                _logger.LogWarning("No matching strategy found for grain {Interface}, using primary server {ServerId}",
                    grainInterface.Name, primary.ServerId);
                return primary.ServerId;
            }

            // Any healthy server
            var anyHealthy = servers.Values.FirstOrDefault(s =>
                s.HealthStatus != ServerHealthStatus.Offline &&
                s.HealthStatus != ServerHealthStatus.Unhealthy);
            
            if (anyHealthy != null)
            {
                _logger.LogWarning("No matching strategy or primary server for grain {Interface}, using any healthy server {ServerId}",
                    grainInterface.Name, anyHealthy.ServerId);
                return anyHealthy.ServerId;
            }

            _logger.LogError("No healthy servers available for grain {Interface}:{Key}", 
                grainInterface.Name, grainKey);
            return null;
        }
    }
}