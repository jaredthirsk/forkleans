// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

using Granville.Rpc.Configuration;
using Granville.Rpc.Hosting;
using Granville.Rpc.Security.Configuration;
using Granville.Rpc.Security.Transport;
using Granville.Rpc.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Granville.Rpc.Security;

/// <summary>
/// Extension methods for configuring RPC transport security.
/// </summary>
public static class SecurityExtensions
{
    /// <summary>
    /// Configures the RPC server to use PSK-based encryption.
    /// Must be called AFTER configuring the underlying transport (e.g., UseLiteNetLib()).
    /// </summary>
    /// <param name="builder">The RPC server builder.</param>
    /// <param name="configureOptions">Action to configure PSK options including the PSK lookup callback.</param>
    /// <returns>The builder for chaining.</returns>
    /// <example>
    /// <code>
    /// builder.Host.UseOrleansRpc(rpc =>
    /// {
    ///     rpc.UseLiteNetLib();
    ///     rpc.UsePskEncryption(options =>
    ///     {
    ///         options.IsServer = true;
    ///         options.PskLookup = async (playerId, ct) =>
    ///         {
    ///             var grain = grainFactory.GetGrain&lt;IPlayerSessionGrain&gt;(playerId);
    ///             var session = await grain.GetSessionAsync();
    ///             return session?.GetSessionKeyBytes();
    ///         };
    ///     });
    /// });
    /// </code>
    /// </example>
    public static IRpcServerBuilder UsePskEncryption(
        this IRpcServerBuilder builder,
        Action<DtlsPskOptions> configureOptions)
    {
        var options = new DtlsPskOptions { IsServer = true };
        configureOptions(options);

        // Get the existing transport factory
        var serviceDescriptor = builder.Services.FirstOrDefault(d => d.ServiceType == typeof(IRpcTransportFactory));
        if (serviceDescriptor == null)
        {
            throw new InvalidOperationException(
                "No transport factory configured. Call UseLiteNetLib() or another transport method before UsePskEncryption().");
        }

        // Remove existing factory
        builder.Services.Remove(serviceDescriptor);

        // Create wrapper factory that adds encryption
        IRpcTransportFactory innerFactory;
        if (serviceDescriptor.ImplementationInstance != null)
        {
            innerFactory = (IRpcTransportFactory)serviceDescriptor.ImplementationInstance;
        }
        else if (serviceDescriptor.ImplementationFactory != null)
        {
            // We need to defer creation - create a lazy factory
            var factory = serviceDescriptor.ImplementationFactory;
            innerFactory = new DeferredTransportFactory(sp => (IRpcTransportFactory)factory(sp));
        }
        else
        {
            throw new InvalidOperationException("Cannot wrap transport factory - unsupported registration type.");
        }

        var wrappedFactory = new PskEncryptedTransportFactory(innerFactory, options);
        builder.Services.AddSingleton<IRpcTransportFactory>(wrappedFactory);

        // Register options
        builder.Services.TryAddSingleton<IOptions<DtlsPskOptions>>(
            Microsoft.Extensions.Options.Options.Create(options));

        builder.Services.Configure<RpcTransportOptions>(opt =>
        {
            opt.TransportType = opt.TransportType + "+PSK";
        });

        return builder;
    }

    /// <summary>
    /// Configures the RPC client to use PSK-based encryption.
    /// Must be called AFTER configuring the underlying transport (e.g., UseLiteNetLib()).
    /// </summary>
    /// <param name="builder">The RPC client builder.</param>
    /// <param name="configureOptions">Action to configure PSK options including the PSK identity and key.</param>
    /// <returns>The builder for chaining.</returns>
    /// <example>
    /// <code>
    /// builder.UseOrleansRpcClient(rpc =>
    /// {
    ///     rpc.UseLiteNetLib();
    ///     rpc.UsePskEncryption(options =>
    ///     {
    ///         options.IsServer = false;
    ///         options.PskIdentity = playerId;
    ///         options.PskKey = sessionKeyBytes;
    ///     });
    /// });
    /// </code>
    /// </example>
    public static IRpcClientBuilder UsePskEncryption(
        this IRpcClientBuilder builder,
        Action<DtlsPskOptions> configureOptions)
    {
        var options = new DtlsPskOptions { IsServer = false };
        configureOptions(options);

        // Get the existing transport factory
        var serviceDescriptor = builder.Services.FirstOrDefault(d => d.ServiceType == typeof(IRpcTransportFactory));
        if (serviceDescriptor == null)
        {
            throw new InvalidOperationException(
                "No transport factory configured. Call UseLiteNetLib() or another transport method before UsePskEncryption().");
        }

        // Remove existing factory
        builder.Services.Remove(serviceDescriptor);

        // Create wrapper factory
        IRpcTransportFactory innerFactory;
        if (serviceDescriptor.ImplementationInstance != null)
        {
            innerFactory = (IRpcTransportFactory)serviceDescriptor.ImplementationInstance;
        }
        else if (serviceDescriptor.ImplementationFactory != null)
        {
            var factory = serviceDescriptor.ImplementationFactory;
            innerFactory = new DeferredTransportFactory(sp => (IRpcTransportFactory)factory(sp));
        }
        else
        {
            throw new InvalidOperationException("Cannot wrap transport factory - unsupported registration type.");
        }

        var wrappedFactory = new PskEncryptedTransportFactory(innerFactory, options);
        builder.Services.AddSingleton<IRpcTransportFactory>(wrappedFactory);

        // Register options
        builder.Services.TryAddSingleton<IOptions<DtlsPskOptions>>(
            Microsoft.Extensions.Options.Options.Create(options));

        builder.Services.Configure<RpcTransportOptions>(opt =>
        {
            opt.TransportType = opt.TransportType + "+PSK";
        });

        return builder;
    }

    /// <summary>
    /// Explicitly disables transport security.
    /// Should only be used for local development. Logs a warning at startup.
    /// </summary>
    /// <param name="builder">The RPC server builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static IRpcServerBuilder UseNoSecurity(this IRpcServerBuilder builder)
    {
        // Get the existing transport factory
        var serviceDescriptor = builder.Services.FirstOrDefault(d => d.ServiceType == typeof(IRpcTransportFactory));
        if (serviceDescriptor == null)
        {
            throw new InvalidOperationException(
                "No transport factory configured. Call UseLiteNetLib() or another transport method before UseNoSecurity().");
        }

        // Remove and re-add with wrapper that logs warning
        builder.Services.Remove(serviceDescriptor);

        IRpcTransportFactory innerFactory;
        if (serviceDescriptor.ImplementationInstance != null)
        {
            innerFactory = (IRpcTransportFactory)serviceDescriptor.ImplementationInstance;
        }
        else if (serviceDescriptor.ImplementationFactory != null)
        {
            var factory = serviceDescriptor.ImplementationFactory;
            innerFactory = new DeferredTransportFactory(sp => (IRpcTransportFactory)factory(sp));
        }
        else
        {
            throw new InvalidOperationException("Cannot wrap transport factory - unsupported registration type.");
        }

        var wrappedFactory = new NoSecurityTransportFactory(innerFactory);
        builder.Services.AddSingleton<IRpcTransportFactory>(wrappedFactory);

        builder.Services.Configure<RpcTransportOptions>(opt =>
        {
            opt.TransportType = opt.TransportType + " (INSECURE)";
        });

        return builder;
    }

    /// <summary>
    /// Explicitly disables transport security.
    /// Should only be used for local development. Logs a warning at startup.
    /// </summary>
    /// <param name="builder">The RPC client builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static IRpcClientBuilder UseNoSecurity(this IRpcClientBuilder builder)
    {
        // Get the existing transport factory
        var serviceDescriptor = builder.Services.FirstOrDefault(d => d.ServiceType == typeof(IRpcTransportFactory));
        if (serviceDescriptor == null)
        {
            throw new InvalidOperationException(
                "No transport factory configured. Call UseLiteNetLib() or another transport method before UseNoSecurity().");
        }

        // Remove and re-add with wrapper that logs warning
        builder.Services.Remove(serviceDescriptor);

        IRpcTransportFactory innerFactory;
        if (serviceDescriptor.ImplementationInstance != null)
        {
            innerFactory = (IRpcTransportFactory)serviceDescriptor.ImplementationInstance;
        }
        else if (serviceDescriptor.ImplementationFactory != null)
        {
            var factory = serviceDescriptor.ImplementationFactory;
            innerFactory = new DeferredTransportFactory(sp => (IRpcTransportFactory)factory(sp));
        }
        else
        {
            throw new InvalidOperationException("Cannot wrap transport factory - unsupported registration type.");
        }

        var wrappedFactory = new NoSecurityTransportFactory(innerFactory);
        builder.Services.AddSingleton<IRpcTransportFactory>(wrappedFactory);

        builder.Services.Configure<RpcTransportOptions>(opt =>
        {
            opt.TransportType = opt.TransportType + " (INSECURE)";
        });

        return builder;
    }
}

/// <summary>
/// Helper factory that defers inner factory resolution to runtime.
/// </summary>
internal class DeferredTransportFactory : IRpcTransportFactory
{
    private readonly Func<IServiceProvider, IRpcTransportFactory> _factoryResolver;

    public DeferredTransportFactory(Func<IServiceProvider, IRpcTransportFactory> factoryResolver)
    {
        _factoryResolver = factoryResolver;
    }

    public IRpcTransport CreateTransport(IServiceProvider serviceProvider)
    {
        var innerFactory = _factoryResolver(serviceProvider);
        return innerFactory.CreateTransport(serviceProvider);
    }
}
