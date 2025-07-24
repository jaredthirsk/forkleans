using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Granville.Rpc.Configuration;
using Granville.Rpc.Hosting;

namespace Granville.Rpc.Transport.LiteNetLib
{
    /// <summary>
    /// Extension methods for configuring LiteNetLib transport.
    /// </summary>
    public static class LiteNetLibExtensions
    {
        /// <summary>
        /// Configures the RPC server to use LiteNetLib transport.
        /// </summary>
        public static IRpcServerBuilder UseLiteNetLib(this IRpcServerBuilder builder)
        {
            builder.Services.Configure<RpcTransportOptions>(options =>
            {
                options.TransportType = "LiteNetLib";
            });

            // Replace any existing transport factory
            builder.Services.RemoveAll<IRpcTransportFactory>();
            builder.Services.AddSingleton<IRpcTransportFactory>(_ => new LiteNetLibTransportFactory(isServer: true));

            // Register default LiteNetLib options
            builder.Services.TryAddSingleton<IOptions<LiteNetLibOptions>>(sp =>
                Microsoft.Extensions.Options.Options.Create(new LiteNetLibOptions()));

            return builder;
        }

        /// <summary>
        /// Configures the RPC client to use LiteNetLib transport.
        /// </summary>
        public static IRpcClientBuilder UseLiteNetLib(this IRpcClientBuilder builder)
        {
            builder.Services.Configure<RpcTransportOptions>(options =>
            {
                options.TransportType = "LiteNetLib";
            });

            // Replace any existing transport factory
            builder.Services.RemoveAll<IRpcTransportFactory>();
            builder.Services.AddSingleton<IRpcTransportFactory>(_ => new LiteNetLibTransportFactory(isServer: false));

            // Register default LiteNetLib options with proper IOptions<T> pattern
            builder.Services.TryAddSingleton<IOptions<LiteNetLibOptions>>(sp =>
                Microsoft.Extensions.Options.Options.Create(new LiteNetLibOptions()));

            return builder;
        }

        /// <summary>
        /// Configures LiteNetLib-specific options.
        /// </summary>
        public static IRpcServerBuilder ConfigureLiteNetLib(this IRpcServerBuilder builder, System.Action<LiteNetLibOptions> configureOptions)
        {
            builder.Services.Configure(configureOptions);
            return builder;
        }

        /// <summary>
        /// Configures LiteNetLib-specific options.
        /// </summary>
        public static IRpcClientBuilder ConfigureLiteNetLib(this IRpcClientBuilder builder, System.Action<LiteNetLibOptions> configureOptions)
        {
            builder.Services.Configure(configureOptions);
            return builder;
        }
    }

    /// <summary>
    /// LiteNetLib-specific configuration options.
    /// </summary>
    public class LiteNetLibOptions
    {
        /// <summary>
        /// Gets or sets whether to enable statistics collection.
        /// </summary>
        public bool EnableStatistics { get; set; } = true;

        /// <summary>
        /// Gets or sets the polling interval in milliseconds.
        /// </summary>
        public int PollingIntervalMs { get; set; } = 15;

        /// <summary>
        /// Gets or sets the maximum packet loss percentage before considering connection failed.
        /// </summary>
        public int MaxPacketLossPercentage { get; set; } = 10;

        /// <summary>
        /// Gets or sets whether to enable NAT punch-through.
        /// </summary>
        public bool EnableNatPunchthrough { get; set; } = false;

        /// <summary>
        /// Gets or sets the connection timeout in milliseconds.
        /// </summary>
        public int ConnectionTimeoutMs { get; set; }
#if DEBUG
            = 120_000;
#else
            = 5_000;
#endif
    }
}
