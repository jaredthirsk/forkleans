using System;
using System.Collections.Generic;

namespace Granville.Rpc.Multiplexing
{
    /// <summary>
    /// Describes an RPC server that can be connected to by the multiplexer.
    /// </summary>
    public interface IServerDescriptor
    {
        /// <summary>
        /// Unique identifier for this server.
        /// </summary>
        string ServerId { get; }

        /// <summary>
        /// Hostname or IP address of the server.
        /// </summary>
        string HostName { get; }

        /// <summary>
        /// Port number for RPC connections.
        /// </summary>
        int Port { get; }

        /// <summary>
        /// Server metadata for routing decisions (e.g., "zone" -> "1,0").
        /// </summary>
        Dictionary<string, string> Metadata { get; }

        /// <summary>
        /// Indicates if this is the primary server for global grains.
        /// </summary>
        bool IsPrimary { get; }

        /// <summary>
        /// Last time a health check was performed.
        /// </summary>
        DateTime LastHealthCheck { get; set; }

        /// <summary>
        /// Current health status of the server.
        /// </summary>
        ServerHealthStatus HealthStatus { get; set; }
    }

    /// <summary>
    /// Health status of an RPC server.
    /// </summary>
    public enum ServerHealthStatus
    {
        /// <summary>
        /// Health status is unknown (not checked yet).
        /// </summary>
        Unknown,

        /// <summary>
        /// Server is healthy and accepting connections.
        /// </summary>
        Healthy,

        /// <summary>
        /// Server is experiencing issues but still operational.
        /// </summary>
        Degraded,

        /// <summary>
        /// Server is unhealthy and should not be used.
        /// </summary>
        Unhealthy,

        /// <summary>
        /// Server is offline or unreachable.
        /// </summary>
        Offline
    }

    /// <summary>
    /// Default implementation of IServerDescriptor.
    /// </summary>
    public class ServerDescriptor : IServerDescriptor
    {
        public string ServerId { get; set; } = string.Empty;
        public string HostName { get; set; } = string.Empty;
        public int Port { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
        public bool IsPrimary { get; set; }
        public DateTime LastHealthCheck { get; set; }
        public ServerHealthStatus HealthStatus { get; set; } = ServerHealthStatus.Unknown;
    }
}