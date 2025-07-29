using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Granville.Rpc.Multiplexing.Strategies
{
    /// <summary>
    /// Routes grains to servers based on zone information.
    /// </summary>
    public class ZoneBasedRoutingStrategy : IGrainRoutingStrategy
    {
        private readonly ILogger<ZoneBasedRoutingStrategy> _logger;

        public ZoneBasedRoutingStrategy(ILogger<ZoneBasedRoutingStrategy> logger)
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

            // Check if this is a zone-aware grain
            var isZoneAware = typeof(IZoneAwareGrain).IsAssignableFrom(grainInterface);
            
            if (isZoneAware)
            {
                // Check if context has zone information
                var zone = context?.GetProperty<string>("Zone");
                if (!string.IsNullOrEmpty(zone))
                {
                    // Find server handling this zone
                    var zoneServer = servers.Values.FirstOrDefault(s => 
                        s.Metadata != null &&
                        s.Metadata.TryGetValue("zone", out var serverZone) && 
                        serverZone == zone &&
                        s.HealthStatus != ServerHealthStatus.Offline &&
                        s.HealthStatus != ServerHealthStatus.Unhealthy);

                    if (zoneServer != null)
                    {
                        _logger.LogDebug("Routing zone-aware grain {Interface}:{Key} to zone server {ServerId} for zone {Zone}",
                            grainInterface.Name, grainKey, zoneServer.ServerId, zone);
                        return Task.FromResult(zoneServer.ServerId);
                    }

                    _logger.LogWarning("No healthy server found for zone {Zone}, falling back to primary", zone);
                }
                else
                {
                    _logger.LogWarning("Zone-aware grain {Interface} requested but no zone in context", 
                        grainInterface.Name);
                }
            }

            // For non-zone-aware grains or when zone server not found, use primary
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

            // Last resort - any healthy server
            var anyHealthy = servers.Values.FirstOrDefault(s =>
                s.HealthStatus != ServerHealthStatus.Offline &&
                s.HealthStatus != ServerHealthStatus.Unhealthy);
            
            if (anyHealthy != null)
            {
                _logger.LogWarning("No primary server available, routing grain {Interface}:{Key} to any healthy server {ServerId}",
                    grainInterface.Name, grainKey, anyHealthy.ServerId);
                return Task.FromResult(anyHealthy.ServerId);
            }

            _logger.LogError("No healthy servers available for grain {Interface}:{Key}", 
                grainInterface.Name, grainKey);
            return Task.FromResult<string>(null);
        }
    }
}