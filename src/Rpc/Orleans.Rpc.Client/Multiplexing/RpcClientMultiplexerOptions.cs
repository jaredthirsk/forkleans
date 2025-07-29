using System;

namespace Granville.Rpc.Multiplexing
{
    /// <summary>
    /// Configuration options for RpcClientMultiplexer.
    /// </summary>
    public class RpcClientMultiplexerOptions
    {
        /// <summary>
        /// Whether to eagerly connect to servers when they are registered.
        /// Default is false (connect on first use).
        /// </summary>
        public bool EagerConnect { get; set; } = false;

        /// <summary>
        /// Whether to enable periodic health checks for registered servers.
        /// Default is true.
        /// </summary>
        public bool EnableHealthChecks { get; set; } = true;

        /// <summary>
        /// Interval between health checks.
        /// Default is 30 seconds.
        /// </summary>
        public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Timeout for establishing connections to servers.
        /// Default is 10 seconds.
        /// </summary>
        public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Maximum number of connection retry attempts.
        /// Default is 3.
        /// </summary>
        public int MaxConnectionRetries { get; set; } = 3;

        /// <summary>
        /// Base time for exponential backoff between retry attempts.
        /// Default is 2 seconds.
        /// </summary>
        public TimeSpan RetryBackoffBase { get; set; } = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Whether to automatically remove unhealthy servers from routing.
        /// Default is false.
        /// </summary>
        public bool AutoRemoveUnhealthyServers { get; set; } = false;

        /// <summary>
        /// Number of consecutive health check failures before marking a server as unhealthy.
        /// Default is 3.
        /// </summary>
        public int UnhealthyThreshold { get; set; } = 3;
    }
}