using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Granville.Rpc.Configuration;
using Granville.Rpc.Hosting;

namespace Granville.Rpc.Transport.Ruffles
{
    /// <summary>
    /// Extension methods for configuring Ruffles transport.
    /// </summary>
    public static class RufflesExtensions
    {
        /// <summary>
        /// Configures the RPC server to use Ruffles transport.
        /// </summary>
        public static IRpcServerBuilder UseRuffles(this IRpcServerBuilder builder)
        {
            builder.Services.Configure<RpcTransportOptions>(options =>
            {
                options.TransportType = "Ruffles";
            });

            // Replace any existing transport factory
            builder.Services.RemoveAll<IRpcTransportFactory>();
            builder.Services.AddSingleton<IRpcTransportFactory>(_ => new RufflesTransportFactory(isServer: true));

            // Register default Ruffles options
            builder.Services.TryAddSingleton<IOptions<RufflesOptions>>(sp =>
                Microsoft.Extensions.Options.Options.Create(new RufflesOptions()));

            return builder;
        }

        /// <summary>
        /// Configures the RPC client to use Ruffles transport.
        /// </summary>
        public static IRpcClientBuilder UseRuffles(this IRpcClientBuilder builder)
        {
            builder.Services.Configure<RpcTransportOptions>(options =>
            {
                options.TransportType = "Ruffles";
            });

            // Replace any existing transport factory
            builder.Services.RemoveAll<IRpcTransportFactory>();
            builder.Services.AddSingleton<IRpcTransportFactory>(_ => new RufflesTransportFactory(isServer: false));

            // Register default Ruffles options
            builder.Services.TryAddSingleton<IOptions<RufflesOptions>>(sp =>
                Microsoft.Extensions.Options.Options.Create(new RufflesOptions()));

            return builder;
        }

        /// <summary>
        /// Configures Ruffles-specific options.
        /// </summary>
        public static IRpcServerBuilder ConfigureRuffles(this IRpcServerBuilder builder, System.Action<RufflesOptions> configureOptions)
        {
            builder.Services.Configure(configureOptions);
            return builder;
        }

        /// <summary>
        /// Configures Ruffles-specific options.
        /// </summary>
        public static IRpcClientBuilder ConfigureRuffles(this IRpcClientBuilder builder, System.Action<RufflesOptions> configureOptions)
        {
            builder.Services.Configure(configureOptions);
            return builder;
        }
    }

    /// <summary>
    /// Ruffles-specific configuration options.
    /// </summary>
    public class RufflesOptions
    {
        /// <summary>
        /// Gets or sets the challenge difficulty for connection security.
        /// </summary>
        public int ChallengeDifficulty { get; set; } = 0;

        /// <summary>
        /// Gets or sets the heartbeat delay in milliseconds.
        /// </summary>
        public int HeartbeatDelayMs { get; set; } = 5000;

        /// <summary>
        /// Gets or sets the handshake timeout in milliseconds.
        /// </summary>
        public int HandshakeTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// Gets or sets the connection request timeout in milliseconds.
        /// </summary>
        public int ConnectionRequestTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// Gets or sets the maximum buffer size.
        /// </summary>
        public int MaxBufferSize { get; set; } = 1024 * 1024; // 1MB

        /// <summary>
        /// Gets or sets the minimum MTU size.
        /// </summary>
        public int MinimumMTU { get; set; } = 512;

        /// <summary>
        /// Gets or sets the maximum MTU size.
        /// </summary>
        public int MaximumMTU { get; set; } = 4096;

        /// <summary>
        /// Gets or sets the reliability window size.
        /// </summary>
        public int ReliabilityWindowSize { get; set; } = 512;

        /// <summary>
        /// Gets or sets the maximum number of reliability resend attempts.
        /// </summary>
        public int ReliabilityMaxResendAttempts { get; set; } = 30;

        /// <summary>
        /// Gets or sets whether to enable timeout detection.
        /// </summary>
        public bool EnableTimeouts { get; set; } = true;

        /// <summary>
        /// Gets or sets the polling interval in milliseconds.
        /// </summary>
        public int PollingIntervalMs { get; set; } = 1;
    }
}