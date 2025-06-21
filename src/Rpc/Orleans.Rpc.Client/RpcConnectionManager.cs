using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Forkleans.Rpc.Protocol;

namespace Forkleans.Rpc
{
    /// <summary>
    /// Manages multiple RPC connections to different servers with zone-based routing.
    /// </summary>
    internal class RpcConnectionManager : IDisposable
    {
        private readonly ILogger<RpcConnectionManager> _logger;
        private readonly ConcurrentDictionary<string, RpcConnection> _connections = new();
        private readonly ConcurrentDictionary<int, string> _zoneToServerMapping = new();
        private readonly SemaphoreSlim _connectionLock = new(1, 1);
        private bool _disposed;

        public RpcConnectionManager(ILogger<RpcConnectionManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Adds or updates a connection to a server.
        /// </summary>
        public async Task AddConnectionAsync(string serverId, RpcConnection connection, int? zoneId = null)
        {
            await _connectionLock.WaitAsync();
            try
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(RpcConnectionManager));

                // Add or update the connection
                var added = _connections.TryAdd(serverId, connection);
                if (!added)
                {
                    // Connection already exists, replace it
                    if (_connections.TryGetValue(serverId, out var oldConnection))
                    {
                        oldConnection.Dispose();
                    }
                    _connections[serverId] = connection;
                }

                // Update zone mapping if provided
                if (zoneId.HasValue)
                {
                    _zoneToServerMapping[zoneId.Value] = serverId;
                    _logger.LogInformation("Mapped zone {ZoneId} to server {ServerId}", zoneId.Value, serverId);
                }

                _logger.LogInformation("Added connection to server {ServerId} (zone: {ZoneId})", 
                    serverId, zoneId?.ToString() ?? "none");
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        /// <summary>
        /// Updates zone-to-server mappings from server information.
        /// </summary>
        public void UpdateZoneMappings(Dictionary<int, string> zoneMappings)
        {
            if (zoneMappings == null) return;

            foreach (var kvp in zoneMappings)
            {
                _zoneToServerMapping[kvp.Key] = kvp.Value;
            }

            _logger.LogDebug("Updated zone mappings: {Count} zones mapped", zoneMappings.Count);
        }

        /// <summary>
        /// Gets a connection for a specific server ID.
        /// </summary>
        public RpcConnection GetConnection(string serverId)
        {
            if (_connections.TryGetValue(serverId, out var connection))
            {
                return connection;
            }
            return null;
        }

        /// <summary>
        /// Gets a connection for a specific zone ID.
        /// </summary>
        public RpcConnection GetConnectionForZone(int zoneId)
        {
            if (_zoneToServerMapping.TryGetValue(zoneId, out var serverId))
            {
                return GetConnection(serverId);
            }
            return null;
        }

        /// <summary>
        /// Gets the best connection for a request, considering zone routing.
        /// </summary>
        public RpcConnection GetConnectionForRequest(RpcRequest request)
        {
            // If request has a target zone, try to route to that zone's server
            if (request.TargetZoneId.HasValue)
            {
                var connection = GetConnectionForZone(request.TargetZoneId.Value);
                if (connection != null)
                {
                    _logger.LogDebug("Routing request to zone {ZoneId} via server {ServerId}", 
                        request.TargetZoneId.Value, 
                        _zoneToServerMapping.GetValueOrDefault(request.TargetZoneId.Value));
                    return connection;
                }

                _logger.LogWarning("No server found for zone {ZoneId}, falling back to default", 
                    request.TargetZoneId.Value);
            }

            // Fall back to first available connection
            var firstConnection = _connections.Values.FirstOrDefault();
            if (firstConnection == null)
            {
                throw new InvalidOperationException("No RPC connections available");
            }

            return firstConnection;
        }

        /// <summary>
        /// Removes a connection to a server.
        /// </summary>
        public async Task RemoveConnectionAsync(string serverId)
        {
            await _connectionLock.WaitAsync();
            try
            {
                if (_connections.TryRemove(serverId, out var connection))
                {
                    connection.Dispose();

                    // Remove any zone mappings for this server
                    var zonesToRemove = _zoneToServerMapping
                        .Where(kvp => kvp.Value == serverId)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var zoneId in zonesToRemove)
                    {
                        _zoneToServerMapping.TryRemove(zoneId, out _);
                    }

                    _logger.LogInformation("Removed connection to server {ServerId} and {ZoneCount} zone mappings", 
                        serverId, zonesToRemove.Count);
                }
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        /// <summary>
        /// Gets all active connections.
        /// </summary>
        public IReadOnlyDictionary<string, RpcConnection> GetAllConnections()
        {
            return _connections;
        }

        /// <summary>
        /// Gets all zone mappings.
        /// </summary>
        public IReadOnlyDictionary<int, string> GetZoneMappings()
        {
            return _zoneToServerMapping;
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _connectionLock.Wait();
            try
            {
                foreach (var connection in _connections.Values)
                {
                    connection.Dispose();
                }
                _connections.Clear();
                _zoneToServerMapping.Clear();
            }
            finally
            {
                _connectionLock.Release();
                _connectionLock.Dispose();
            }
        }
    }
}