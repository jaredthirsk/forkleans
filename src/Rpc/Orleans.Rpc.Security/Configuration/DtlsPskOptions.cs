// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

namespace Granville.Rpc.Security.Configuration;

/// <summary>
/// Configuration options for DTLS-PSK transport encryption.
/// </summary>
public class DtlsPskOptions
{
    /// <summary>
    /// Callback to look up the PSK for a given identity (player ID).
    /// Server-side: Called during DTLS handshake to get the PSK for validation.
    /// </summary>
    public Func<string, CancellationToken, Task<byte[]?>>? PskLookup { get; set; }

    /// <summary>
    /// Callback to look up PSK and user info by identity.
    /// Returns null if identity is not valid.
    /// Preferred over <see cref="PskLookup"/> when user identity info is available.
    /// </summary>
    public Func<string, CancellationToken, Task<PskLookupResult?>>? PskLookupWithIdentity { get; set; }

    /// <summary>
    /// Client-side: The PSK identity (player ID) to present during handshake.
    /// </summary>
    public string? PskIdentity { get; set; }

    /// <summary>
    /// Client-side: The pre-shared key to use for encryption.
    /// </summary>
    public byte[]? PskKey { get; set; }

    /// <summary>
    /// Timeout for DTLS handshake in milliseconds.
    /// Default: 5000ms (5 seconds)
    /// </summary>
    public int HandshakeTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Maximum time to wait for retransmission during handshake.
    /// Default: 1000ms (1 second)
    /// </summary>
    public int RetransmissionTimeoutMs { get; set; } = 1000;

    /// <summary>
    /// DTLS record size limit to avoid fragmentation.
    /// Default: 1200 bytes (safe for most networks)
    /// </summary>
    public int MaxRecordSize { get; set; } = 1200;

    /// <summary>
    /// Whether to enable DTLS session resumption for faster reconnects.
    /// Default: true
    /// </summary>
    public bool EnableSessionResumption { get; set; } = true;

    /// <summary>
    /// Session resumption cache size (number of sessions to cache).
    /// Default: 1000
    /// </summary>
    public int SessionCacheSize { get; set; } = 1000;

    /// <summary>
    /// Cipher suite preference. Default uses AES-256-GCM with SHA384.
    /// </summary>
    public DtlsCipherSuite CipherSuite { get; set; } = DtlsCipherSuite.TLS_PSK_WITH_AES_256_GCM_SHA384;

    /// <summary>
    /// Whether this is a server (listening) or client (connecting) instance.
    /// </summary>
    public bool IsServer { get; set; } = true;

    /// <summary>
    /// Whether to log DTLS handshake details for debugging.
    /// Default: false (only log errors)
    /// </summary>
    public bool EnableHandshakeLogging { get; set; } = false;
}

/// <summary>
/// Supported DTLS cipher suites for PSK.
/// </summary>
public enum DtlsCipherSuite
{
    /// <summary>
    /// TLS_PSK_WITH_AES_128_GCM_SHA256 - Good performance, adequate security.
    /// </summary>
    TLS_PSK_WITH_AES_128_GCM_SHA256,

    /// <summary>
    /// TLS_PSK_WITH_AES_256_GCM_SHA384 - Maximum security, recommended.
    /// </summary>
    TLS_PSK_WITH_AES_256_GCM_SHA384,

    /// <summary>
    /// TLS_PSK_WITH_CHACHA20_POLY1305_SHA256 - Fast on mobile, good security.
    /// </summary>
    TLS_PSK_WITH_CHACHA20_POLY1305_SHA256
}

/// <summary>
/// Result of a PSK lookup including user identity info.
/// </summary>
public sealed record PskLookupResult
{
    /// <summary>
    /// The pre-shared key bytes.
    /// </summary>
    public required byte[] Psk { get; init; }

    /// <summary>
    /// The authenticated user identity.
    /// </summary>
    public required RpcUserIdentity User { get; init; }
}
