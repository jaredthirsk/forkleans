// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using Granville.Rpc.Security.Configuration;
using Granville.Rpc.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Granville.Rpc.Security.Transport;

/// <summary>
/// Transport decorator that adds PSK-based AES-GCM encryption to any underlying transport.
/// Uses a simple challenge-response handshake to establish the session, then encrypts
/// all subsequent traffic with AES-256-GCM.
/// </summary>
/// <remarks>
/// Protocol overview:
/// 1. Client sends: HELLO + Identity (player ID)
/// 2. Server looks up PSK via callback, sends: CHALLENGE + 16-byte nonce
/// 3. Client computes HMAC(nonce, PSK), sends: RESPONSE + HMAC
/// 4. Server verifies HMAC, both derive session keys from PSK + nonce
/// 5. All subsequent messages are AES-256-GCM encrypted
///
/// Message format after handshake:
/// [1 byte: MessageType] [8 bytes: SequenceNumber] [12 bytes: Nonce] [N bytes: Ciphertext] [16 bytes: Tag]
/// </remarks>
public class PskEncryptedTransport : IRpcTransport
{
    private readonly IRpcTransport _innerTransport;
    private readonly DtlsPskOptions _options;
    private readonly ILogger<PskEncryptedTransport> _logger;
    private readonly ConcurrentDictionary<string, PskSession> _sessions = new();
    private readonly ConcurrentDictionary<IPEndPoint, string> _endpointToConnection = new();
    private bool _disposed;

    // Message types for the handshake protocol
    private const byte MSG_HELLO = 0x01;
    private const byte MSG_CHALLENGE = 0x02;
    private const byte MSG_RESPONSE = 0x03;
    private const byte MSG_ENCRYPTED = 0x10;
    private const byte MSG_HANDSHAKE_ERROR = 0xFF;

    public event EventHandler<RpcDataReceivedEventArgs>? DataReceived;
    public event EventHandler<RpcConnectionEventArgs>? ConnectionEstablished;
    public event EventHandler<RpcConnectionEventArgs>? ConnectionClosed;

    public PskEncryptedTransport(
        IRpcTransport innerTransport,
        IOptions<DtlsPskOptions> options,
        ILogger<PskEncryptedTransport> logger)
    {
        _innerTransport = innerTransport ?? throw new ArgumentNullException(nameof(innerTransport));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Subscribe to inner transport events
        _innerTransport.DataReceived += OnInnerDataReceived;
        _innerTransport.ConnectionEstablished += OnInnerConnectionEstablished;
        _innerTransport.ConnectionClosed += OnInnerConnectionClosed;
    }

    public Task StartAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[PSK] Starting encrypted transport on {Endpoint}", endpoint);
        return _innerTransport.StartAsync(endpoint, cancellationToken);
    }

    public async Task ConnectAsync(IPEndPoint remoteEndpoint, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[PSK] Connecting to {Endpoint}", remoteEndpoint);

        // First, establish the underlying connection
        await _innerTransport.ConnectAsync(remoteEndpoint, cancellationToken);

        // Then perform PSK handshake
        if (!string.IsNullOrEmpty(_options.PskIdentity) && _options.PskKey != null)
        {
            await PerformClientHandshakeAsync(remoteEndpoint, cancellationToken);
        }
        else
        {
            _logger.LogWarning("[PSK] No PSK credentials configured - connection will not be encrypted");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[PSK] Stopping encrypted transport");

        // Clear all sessions
        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }
        _sessions.Clear();
        _endpointToConnection.Clear();

        return _innerTransport.StopAsync(cancellationToken);
    }

    public async Task SendAsync(IPEndPoint remoteEndpoint, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        // Find session for this endpoint
        if (_endpointToConnection.TryGetValue(remoteEndpoint, out var connectionId) &&
            _sessions.TryGetValue(connectionId, out var session) &&
            session.IsEstablished)
        {
            // Encrypt and send
            var encrypted = session.Encrypt(data.Span);
            await _innerTransport.SendAsync(remoteEndpoint, encrypted, cancellationToken);
        }
        else
        {
            // No session or not established - send plaintext (for handshake messages)
            await _innerTransport.SendAsync(remoteEndpoint, data, cancellationToken);
        }
    }

    public async Task SendToConnectionAsync(string connectionId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        if (_sessions.TryGetValue(connectionId, out var session) && session.IsEstablished)
        {
            // Encrypt and send
            var encrypted = session.Encrypt(data.Span);
            await _innerTransport.SendToConnectionAsync(connectionId, encrypted, cancellationToken);
        }
        else
        {
            // No session - send plaintext
            await _innerTransport.SendToConnectionAsync(connectionId, data, cancellationToken);
        }
    }

    private async void OnInnerDataReceived(object? sender, RpcDataReceivedEventArgs e)
    {
        try
        {
            await ProcessReceivedDataAsync(e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PSK] Error processing received data from {Endpoint}", e.RemoteEndPoint);
        }
    }

    private async Task ProcessReceivedDataAsync(RpcDataReceivedEventArgs e)
    {
        if (e.Data.Length == 0)
            return;

        var messageType = e.Data.Span[0];
        var connectionId = e.ConnectionId ?? e.RemoteEndPoint.ToString();

        switch (messageType)
        {
            case MSG_HELLO:
                await HandleHelloAsync(e, connectionId);
                break;

            case MSG_CHALLENGE:
                await HandleChallengeAsync(e, connectionId);
                break;

            case MSG_RESPONSE:
                await HandleResponseAsync(e, connectionId);
                break;

            case MSG_ENCRYPTED:
                HandleEncryptedMessage(e, connectionId);
                break;

            case MSG_HANDSHAKE_ERROR:
                _logger.LogWarning("[PSK] Received handshake error from {Endpoint}", e.RemoteEndPoint);
                break;

            default:
                // Not a PSK message - pass through (may be plaintext fallback)
                _logger.LogDebug("[PSK] Passing through non-PSK message type 0x{Type:X2}", messageType);
                DataReceived?.Invoke(this, e);
                break;
        }
    }

    private async Task HandleHelloAsync(RpcDataReceivedEventArgs e, string connectionId)
    {
        if (!_options.IsServer)
        {
            _logger.LogWarning("[PSK] Received HELLO but not in server mode");
            return;
        }

        // Parse identity from HELLO message
        // Format: [1: MSG_HELLO] [4: identity length] [N: identity bytes]
        if (e.Data.Length < 5)
        {
            _logger.LogWarning("[PSK] Invalid HELLO message - too short");
            return;
        }

        var identityLength = BinaryPrimitives.ReadInt32LittleEndian(e.Data.Span.Slice(1, 4));
        if (e.Data.Length < 5 + identityLength || identityLength > 256)
        {
            _logger.LogWarning("[PSK] Invalid HELLO message - identity length mismatch");
            return;
        }

        var identity = System.Text.Encoding.UTF8.GetString(e.Data.Span.Slice(5, identityLength));
        _logger.LogDebug("[PSK] Received HELLO from identity '{Identity}'", identity);

        // Look up PSK for this identity
        if (_options.PskLookup == null)
        {
            _logger.LogError("[PSK] No PSK lookup callback configured");
            await SendHandshakeErrorAsync(e.RemoteEndPoint, "No PSK lookup configured");
            return;
        }

        var psk = await _options.PskLookup(identity, CancellationToken.None);
        if (psk == null)
        {
            _logger.LogWarning("[PSK] No PSK found for identity '{Identity}'", identity);
            await SendHandshakeErrorAsync(e.RemoteEndPoint, "Unknown identity");
            return;
        }

        // Create session with challenge
        var session = new PskSession(identity, psk, _logger);
        session.GenerateChallenge();
        _sessions[connectionId] = session;
        _endpointToConnection[e.RemoteEndPoint] = connectionId;

        // Send CHALLENGE
        // Format: [1: MSG_CHALLENGE] [16: challenge nonce]
        var response = new byte[1 + 16];
        response[0] = MSG_CHALLENGE;
        session.Challenge.CopyTo(response.AsSpan(1));

        await _innerTransport.SendAsync(e.RemoteEndPoint, response, CancellationToken.None);
        _logger.LogDebug("[PSK] Sent CHALLENGE to {Endpoint}", e.RemoteEndPoint);
    }

    private async Task HandleChallengeAsync(RpcDataReceivedEventArgs e, string connectionId)
    {
        if (_options.IsServer)
        {
            _logger.LogWarning("[PSK] Received CHALLENGE but in server mode");
            return;
        }

        // Parse challenge
        // Format: [1: MSG_CHALLENGE] [16: challenge nonce]
        if (e.Data.Length != 17)
        {
            _logger.LogWarning("[PSK] Invalid CHALLENGE message - wrong size");
            return;
        }

        var challenge = e.Data.Slice(1, 16).ToArray();
        _logger.LogDebug("[PSK] Received CHALLENGE from {Endpoint}", e.RemoteEndPoint);

        // Get or create session
        if (!_sessions.TryGetValue(connectionId, out var session))
        {
            if (_options.PskKey == null || string.IsNullOrEmpty(_options.PskIdentity))
            {
                _logger.LogError("[PSK] No PSK credentials configured for client");
                return;
            }

            session = new PskSession(_options.PskIdentity, _options.PskKey, _logger);
            _sessions[connectionId] = session;
            _endpointToConnection[e.RemoteEndPoint] = connectionId;
        }

        // Compute response: HMAC-SHA256(challenge, psk)
        session.SetChallenge(challenge);
        var responseHash = session.ComputeChallengeResponse();

        // Send RESPONSE
        // Format: [1: MSG_RESPONSE] [32: HMAC response]
        var response = new byte[1 + 32];
        response[0] = MSG_RESPONSE;
        responseHash.CopyTo(response.AsSpan(1));

        await _innerTransport.SendAsync(e.RemoteEndPoint, response, CancellationToken.None);
        _logger.LogDebug("[PSK] Sent RESPONSE to {Endpoint}", e.RemoteEndPoint);

        // Derive session keys and mark as established
        session.DeriveSessionKeys();
        session.IsEstablished = true;

        _logger.LogInformation("[PSK] Session established with {Endpoint} (client side)", e.RemoteEndPoint);

        // Notify connection established
        ConnectionEstablished?.Invoke(this, new RpcConnectionEventArgs(e.RemoteEndPoint)
        {
            ConnectionId = connectionId
        });
    }

    private async Task HandleResponseAsync(RpcDataReceivedEventArgs e, string connectionId)
    {
        if (!_options.IsServer)
        {
            _logger.LogWarning("[PSK] Received RESPONSE but not in server mode");
            return;
        }

        // Parse response
        // Format: [1: MSG_RESPONSE] [32: HMAC response]
        if (e.Data.Length != 33)
        {
            _logger.LogWarning("[PSK] Invalid RESPONSE message - wrong size");
            return;
        }

        if (!_sessions.TryGetValue(connectionId, out var session))
        {
            _logger.LogWarning("[PSK] Received RESPONSE but no session for {ConnectionId}", connectionId);
            return;
        }

        var receivedHash = e.Data.Slice(1, 32).ToArray();

        // Verify response
        var expectedHash = session.ComputeChallengeResponse();
        if (!CryptographicOperations.FixedTimeEquals(receivedHash, expectedHash))
        {
            _logger.LogWarning("[PSK] RESPONSE verification failed for {Endpoint}", e.RemoteEndPoint);
            await SendHandshakeErrorAsync(e.RemoteEndPoint, "Authentication failed");
            _sessions.TryRemove(connectionId, out _);
            return;
        }

        // Derive session keys and mark as established
        session.DeriveSessionKeys();
        session.IsEstablished = true;

        _logger.LogInformation("[PSK] Session established with {Endpoint} for identity '{Identity}' (server side)",
            e.RemoteEndPoint, session.Identity);

        // Notify connection established
        ConnectionEstablished?.Invoke(this, new RpcConnectionEventArgs(e.RemoteEndPoint)
        {
            ConnectionId = connectionId
        });
    }

    private void HandleEncryptedMessage(RpcDataReceivedEventArgs e, string connectionId)
    {
        if (!_sessions.TryGetValue(connectionId, out var session) || !session.IsEstablished)
        {
            _logger.LogWarning("[PSK] Received encrypted message but no established session for {ConnectionId}", connectionId);
            return;
        }

        try
        {
            var decrypted = session.Decrypt(e.Data.Span);
            if (decrypted != null)
            {
                // Raise event with decrypted data
                DataReceived?.Invoke(this, new RpcDataReceivedEventArgs(e.RemoteEndPoint, decrypted)
                {
                    ConnectionId = connectionId
                });
            }
            else
            {
                _logger.LogWarning("[PSK] Failed to decrypt message from {Endpoint}", e.RemoteEndPoint);
            }
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "[PSK] Decryption failed for message from {Endpoint}", e.RemoteEndPoint);
        }
    }

    private async Task SendHandshakeErrorAsync(IPEndPoint endpoint, string message)
    {
        var msgBytes = System.Text.Encoding.UTF8.GetBytes(message);
        var packet = new byte[1 + msgBytes.Length];
        packet[0] = MSG_HANDSHAKE_ERROR;
        msgBytes.CopyTo(packet.AsSpan(1));

        await _innerTransport.SendAsync(endpoint, packet, CancellationToken.None);
    }

    private async Task PerformClientHandshakeAsync(IPEndPoint remoteEndpoint, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[PSK] Initiating handshake with {Endpoint}", remoteEndpoint);

        // Send HELLO
        // Format: [1: MSG_HELLO] [4: identity length] [N: identity bytes]
        var identityBytes = System.Text.Encoding.UTF8.GetBytes(_options.PskIdentity!);
        var hello = new byte[1 + 4 + identityBytes.Length];
        hello[0] = MSG_HELLO;
        BinaryPrimitives.WriteInt32LittleEndian(hello.AsSpan(1, 4), identityBytes.Length);
        identityBytes.CopyTo(hello.AsSpan(5));

        await _innerTransport.SendAsync(remoteEndpoint, hello, cancellationToken);
        _logger.LogDebug("[PSK] Sent HELLO to {Endpoint}", remoteEndpoint);

        // The rest of the handshake happens via OnInnerDataReceived callbacks
        // Wait for session to be established
        var connectionId = remoteEndpoint.ToString();
        var timeout = TimeSpan.FromMilliseconds(_options.HandshakeTimeoutMs);
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (_sessions.TryGetValue(connectionId, out var session) && session.IsEstablished)
            {
                _logger.LogInformation("[PSK] Handshake completed with {Endpoint}", remoteEndpoint);
                return;
            }
            await Task.Delay(10, cancellationToken);
        }

        throw new TimeoutException($"PSK handshake with {remoteEndpoint} timed out after {timeout.TotalMilliseconds}ms");
    }

    private void OnInnerConnectionEstablished(object? sender, RpcConnectionEventArgs e)
    {
        // Don't forward yet - wait for PSK handshake to complete
        _logger.LogDebug("[PSK] Inner transport connection established with {Endpoint}", e.RemoteEndPoint);
    }

    private void OnInnerConnectionClosed(object? sender, RpcConnectionEventArgs e)
    {
        var connectionId = e.ConnectionId ?? e.RemoteEndPoint.ToString();

        if (_sessions.TryRemove(connectionId, out var session))
        {
            session.Dispose();
            _logger.LogInformation("[PSK] Session closed for {Endpoint}", e.RemoteEndPoint);
        }

        _endpointToConnection.TryRemove(e.RemoteEndPoint, out _);
        ConnectionClosed?.Invoke(this, e);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _innerTransport.DataReceived -= OnInnerDataReceived;
        _innerTransport.ConnectionEstablished -= OnInnerConnectionEstablished;
        _innerTransport.ConnectionClosed -= OnInnerConnectionClosed;

        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }
        _sessions.Clear();

        _innerTransport.Dispose();
    }
}
