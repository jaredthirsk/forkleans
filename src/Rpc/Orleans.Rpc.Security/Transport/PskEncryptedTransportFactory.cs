// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

using Granville.Rpc.Security.Configuration;
using Granville.Rpc.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Granville.Rpc.Security.Transport;

/// <summary>
/// Factory that creates PSK-encrypted transport wrappers around an inner transport factory.
/// </summary>
public class PskEncryptedTransportFactory : IRpcTransportFactory
{
    private readonly IRpcTransportFactory _innerFactory;
    private readonly DtlsPskOptions _options;

    public PskEncryptedTransportFactory(IRpcTransportFactory innerFactory, DtlsPskOptions options)
    {
        _innerFactory = innerFactory ?? throw new ArgumentNullException(nameof(innerFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public IRpcTransport CreateTransport(IServiceProvider serviceProvider)
    {
        // Create the inner transport
        var innerTransport = _innerFactory.CreateTransport(serviceProvider);

        // Wrap with encryption
        var logger = serviceProvider.GetRequiredService<ILogger<PskEncryptedTransport>>();
        var options = Microsoft.Extensions.Options.Options.Create(_options);

        return new PskEncryptedTransport(innerTransport, options, logger);
    }
}

/// <summary>
/// Factory for no-security (plaintext) transport - just returns the inner factory's transport.
/// Used to explicitly disable security for development/testing.
/// </summary>
public class NoSecurityTransportFactory : IRpcTransportFactory
{
    private readonly IRpcTransportFactory _innerFactory;

    public NoSecurityTransportFactory(IRpcTransportFactory innerFactory)
    {
        _innerFactory = innerFactory ?? throw new ArgumentNullException(nameof(innerFactory));
    }

    public IRpcTransport CreateTransport(IServiceProvider serviceProvider)
    {
        var logger = serviceProvider.GetService<ILogger<NoSecurityTransportFactory>>();
        logger?.LogWarning("[SECURITY] Transport security is DISABLED. Traffic will be unencrypted. " +
            "Do NOT use in production or over untrusted networks.");

        return _innerFactory.CreateTransport(serviceProvider);
    }
}
