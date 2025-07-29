using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Granville.Rpc.Multiplexing.Strategies
{
    /// <summary>
    /// Routes grains to servers based on service type mappings.
    /// </summary>
    public class ServiceBasedRoutingStrategy : IGrainRoutingStrategy
    {
        private readonly ILogger<ServiceBasedRoutingStrategy> _logger;
        private readonly Dictionary<Type, string> _serviceMapping;

        public ServiceBasedRoutingStrategy(ILogger<ServiceBasedRoutingStrategy> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceMapping = new Dictionary<Type, string>();
        }

        /// <summary>
        /// Maps a grain interface to a specific server.
        /// </summary>
        public void MapService<TGrainInterface>(string serverId)
        {
            _serviceMapping[typeof(TGrainInterface)] = serverId;
            _logger.LogInformation("Mapped grain interface {Interface} to server {ServerId}", 
                typeof(TGrainInterface).Name, serverId);
        }

        /// <summary>
        /// Maps a grain interface type to a specific server.
        /// </summary>
        public void MapService(Type grainInterface, string serverId)
        {
            _serviceMapping[grainInterface] = serverId;
            _logger.LogInformation("Mapped grain interface {Interface} to server {ServerId}", 
                grainInterface.Name, serverId);
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

            // Check explicit mappings first
            if (_serviceMapping.TryGetValue(grainInterface, out var mappedServerId))
            {
                // Verify the mapped server exists and is healthy
                if (servers.TryGetValue(mappedServerId, out var server) &&
                    server.HealthStatus != ServerHealthStatus.Offline &&
                    server.HealthStatus != ServerHealthStatus.Unhealthy)
                {
                    _logger.LogDebug("Routing grain {Interface}:{Key} to mapped server {ServerId}",
                        grainInterface.Name, grainKey, mappedServerId);
                    return Task.FromResult(mappedServerId);
                }
                
                _logger.LogWarning("Mapped server {ServerId} for interface {Interface} is not available",
                    mappedServerId, grainInterface.Name);
            }

            // Check interface attributes for hints
            var serviceAttr = grainInterface.GetCustomAttribute<RpcServiceAttribute>();
            if (serviceAttr != null && !string.IsNullOrEmpty(serviceAttr.PreferredServer))
            {
                var preferredServerId = serviceAttr.PreferredServer;
                if (servers.TryGetValue(preferredServerId, out var server) &&
                    server.HealthStatus != ServerHealthStatus.Offline &&
                    server.HealthStatus != ServerHealthStatus.Unhealthy)
                {
                    _logger.LogDebug("Routing grain {Interface}:{Key} to attribute-specified server {ServerId}",
                        grainInterface.Name, grainKey, preferredServerId);
                    return Task.FromResult(preferredServerId);
                }
            }

            // Check for service type in metadata
            var serviceType = grainInterface.Name.Contains("Game") ? "game" :
                             grainInterface.Name.Contains("Chat") ? "chat" :
                             grainInterface.Name.Contains("Leaderboard") ? "stats" :
                             null;

            if (!string.IsNullOrEmpty(serviceType))
            {
                var serviceServer = servers.Values.FirstOrDefault(s =>
                    s.Metadata != null &&
                    s.Metadata.TryGetValue("service", out var serverService) &&
                    serverService == serviceType &&
                    s.HealthStatus != ServerHealthStatus.Offline &&
                    s.HealthStatus != ServerHealthStatus.Unhealthy);

                if (serviceServer != null)
                {
                    _logger.LogDebug("Routing grain {Interface}:{Key} to service server {ServerId} for service {Service}",
                        grainInterface.Name, grainKey, serviceServer.ServerId, serviceType);
                    return Task.FromResult(serviceServer.ServerId);
                }
            }

            // Default to primary
            var primary = servers.Values.FirstOrDefault(s => 
                s.IsPrimary && 
                s.HealthStatus != ServerHealthStatus.Offline &&
                s.HealthStatus != ServerHealthStatus.Unhealthy);
            
            if (primary != null)
            {
                _logger.LogDebug("Routing grain {Interface}:{Key} to primary server {ServerId} (fallback)",
                    grainInterface.Name, grainKey, primary.ServerId);
                return Task.FromResult(primary.ServerId);
            }

            // Any healthy server
            var anyHealthy = servers.Values.FirstOrDefault(s =>
                s.HealthStatus != ServerHealthStatus.Offline &&
                s.HealthStatus != ServerHealthStatus.Unhealthy);
            
            if (anyHealthy != null)
            {
                _logger.LogWarning("Routing grain {Interface}:{Key} to any healthy server {ServerId}",
                    grainInterface.Name, grainKey, anyHealthy.ServerId);
                return Task.FromResult(anyHealthy.ServerId);
            }

            _logger.LogError("No healthy servers available for grain {Interface}:{Key}", 
                grainInterface.Name, grainKey);
            return Task.FromResult<string>(null);
        }
    }

    /// <summary>
    /// Attribute to specify preferred server for a grain interface.
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface)]
    public class RpcServiceAttribute : Attribute
    {
        /// <summary>
        /// The preferred server ID for this service.
        /// </summary>
        public string PreferredServer { get; set; }

        /// <summary>
        /// The service type (e.g., "game", "chat", "stats").
        /// </summary>
        public string ServiceType { get; set; }
    }
}