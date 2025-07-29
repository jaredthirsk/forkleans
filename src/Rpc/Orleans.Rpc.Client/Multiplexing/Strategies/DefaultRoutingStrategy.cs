using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Granville.Rpc.Multiplexing.Strategies
{
    /// <summary>
    /// Default routing strategy that routes to primary server or any available server.
    /// </summary>
    internal class DefaultRoutingStrategy : IGrainRoutingStrategy
    {
        private readonly ILogger _logger;

        public DefaultRoutingStrategy(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<string> SelectServerAsync(
            Type grainInterface,
            string grainKey,
            IReadOnlyDictionary<string, IServerDescriptor> servers,
            IRoutingContext context)
        {
            if (servers == null || servers.Count == 0)
            {
                _logger.LogWarning("No servers available for routing");
                return Task.FromResult<string>(null);
            }

            // Try primary server first
            var primary = servers.Values.FirstOrDefault(s => 
                s.IsPrimary && 
                s.HealthStatus != ServerHealthStatus.Offline &&
                s.HealthStatus != ServerHealthStatus.Unhealthy);
            
            if (primary != null)
            {
                _logger.LogDebug("Routing grain {Interface}:{Key} to primary server {ServerId}",
                    grainInterface.Name, grainKey, primary.ServerId);
                return Task.FromResult(primary.ServerId);
            }

            // Any healthy server
            var anyHealthy = servers.Values.FirstOrDefault(s =>
                s.HealthStatus != ServerHealthStatus.Offline &&
                s.HealthStatus != ServerHealthStatus.Unhealthy);
            
            if (anyHealthy != null)
            {
                _logger.LogDebug("Routing grain {Interface}:{Key} to any healthy server {ServerId}",
                    grainInterface.Name, grainKey, anyHealthy.ServerId);
                return Task.FromResult(anyHealthy.ServerId);
            }

            _logger.LogError("No healthy servers available for grain {Interface}:{Key}", 
                grainInterface.Name, grainKey);
            return Task.FromResult<string>(null);
        }
    }
}