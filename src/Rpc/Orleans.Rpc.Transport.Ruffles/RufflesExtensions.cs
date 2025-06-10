using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Forkleans.Rpc.Configuration;
using Forkleans.Rpc.Hosting;

namespace Forkleans.Rpc.Transport.Ruffles
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

            builder.Services.TryAddSingleton<IRpcTransportFactory, RufflesTransportFactory>();

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

            builder.Services.TryAddSingleton<IRpcTransportFactory, RufflesTransportFactory>();

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
        /// Gets or sets the channel count.
        /// </summary>
        public int ChannelCount { get; set; } = 4;

        /// <summary>
        /// Gets or sets whether to enable congestion control.
        /// </summary>
        public bool EnableCongestionControl { get; set; } = true;

        /// <summary>
        /// Gets or sets the window size for reliable channels.
        /// </summary>
        public int ReliableWindowSize { get; set; } = 512;

        /// <summary>
        /// Gets or sets the resend delay in milliseconds.
        /// </summary>
        public int ResendDelayMs { get; set; } = 100;

        /// <summary>
        /// Gets or sets whether to enable message coalescing.
        /// </summary>
        public bool EnableMessageCoalescing { get; set; } = true;
    }
}