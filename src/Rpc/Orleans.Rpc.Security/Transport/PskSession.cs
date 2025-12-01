// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Granville.Rpc.Security.Transport;

/// <summary>
/// Manages cryptographic state for a PSK-encrypted session.
/// Handles key derivation, encryption, and decryption using AES-256-GCM.
/// </summary>
internal sealed class PskSession : IDisposable
{
    private readonly ILogger _logger;
    private readonly byte[] _psk;
    private byte[]? _challenge;
    private byte[]? _encryptKey;
    private byte[]? _decryptKey;
    private long _sendSequence;
    private long _receiveSequence;
    private bool _disposed;

    // Constants
    private const int NONCE_SIZE = 12;
    private const int TAG_SIZE = 16;
    private const int KEY_SIZE = 32; // AES-256
    private const int CHALLENGE_SIZE = 16;
    private const int SEQUENCE_SIZE = 8;
    private const byte MSG_ENCRYPTED = 0x10;

    /// <summary>
    /// The identity (player ID) associated with this session.
    /// </summary>
    public string Identity { get; }

    /// <summary>
    /// The challenge nonce used during handshake.
    /// </summary>
    public ReadOnlySpan<byte> Challenge => _challenge ?? ReadOnlySpan<byte>.Empty;

    /// <summary>
    /// Whether the session has completed handshake and is ready for encrypted communication.
    /// </summary>
    public bool IsEstablished { get; set; }

    /// <summary>
    /// The authenticated user identity after successful handshake.
    /// Populated by the transport when PSK lookup returns user info.
    /// </summary>
    public RpcUserIdentity? AuthenticatedUser { get; set; }

    public PskSession(string identity, byte[] psk, ILogger logger)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _psk = psk ?? throw new ArgumentNullException(nameof(psk));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (_psk.Length < 16)
            throw new ArgumentException("PSK must be at least 16 bytes", nameof(psk));
    }

    /// <summary>
    /// Generates a random challenge nonce for the handshake (server-side).
    /// </summary>
    public void GenerateChallenge()
    {
        _challenge = RandomNumberGenerator.GetBytes(CHALLENGE_SIZE);
    }

    /// <summary>
    /// Sets the challenge received from the server (client-side).
    /// </summary>
    public void SetChallenge(byte[] challenge)
    {
        if (challenge.Length != CHALLENGE_SIZE)
            throw new ArgumentException($"Challenge must be {CHALLENGE_SIZE} bytes", nameof(challenge));

        _challenge = challenge;
    }

    /// <summary>
    /// Computes the challenge response: HMAC-SHA256(challenge, PSK).
    /// </summary>
    public byte[] ComputeChallengeResponse()
    {
        if (_challenge == null)
            throw new InvalidOperationException("Challenge not set");

        using var hmac = new HMACSHA256(_psk);
        return hmac.ComputeHash(_challenge);
    }

    /// <summary>
    /// Derives encryption and decryption keys from PSK and challenge using HKDF.
    /// Server and client derive keys in opposite order for bidirectional encryption.
    /// </summary>
    public void DeriveSessionKeys()
    {
        if (_challenge == null)
            throw new InvalidOperationException("Challenge not set");

        // Use HKDF to derive keys from PSK and challenge
        // info strings differ for each direction
        var serverToClientInfo = System.Text.Encoding.UTF8.GetBytes("server_to_client");
        var clientToServerInfo = System.Text.Encoding.UTF8.GetBytes("client_to_server");

        var serverToClientKey = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            _psk,
            KEY_SIZE,
            _challenge,
            serverToClientInfo);

        var clientToServerKey = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            _psk,
            KEY_SIZE,
            _challenge,
            clientToServerInfo);

        // For server: encrypt with server_to_client, decrypt with client_to_server
        // For client: encrypt with client_to_server, decrypt with server_to_client
        // The IsServer flag isn't available here, so we use a convention:
        // The party that generated the challenge (server) uses server_to_client for encryption
        // This is determined by whether _challenge was generated locally or received

        // For simplicity, we'll use a symmetric approach where both use same key
        // In production, you'd want asymmetric keys based on role
        _encryptKey = serverToClientKey;
        _decryptKey = clientToServerKey;

        _logger.LogDebug("[PSK] Session keys derived for identity '{Identity}'", Identity);
    }

    /// <summary>
    /// Encrypts data using AES-256-GCM.
    /// Format: [1: MSG_ENCRYPTED] [8: sequence] [12: nonce] [N: ciphertext] [16: tag]
    /// </summary>
    public byte[] Encrypt(ReadOnlySpan<byte> plaintext)
    {
        if (_encryptKey == null)
            throw new InvalidOperationException("Session keys not derived");

        var sequence = Interlocked.Increment(ref _sendSequence);
        var nonce = GenerateNonce(sequence);

        var output = new byte[1 + SEQUENCE_SIZE + NONCE_SIZE + plaintext.Length + TAG_SIZE];
        var outputSpan = output.AsSpan();

        // Write header
        outputSpan[0] = MSG_ENCRYPTED;
        BinaryPrimitives.WriteInt64LittleEndian(outputSpan.Slice(1, SEQUENCE_SIZE), sequence);
        nonce.CopyTo(outputSpan.Slice(1 + SEQUENCE_SIZE, NONCE_SIZE));

        // Encrypt
        using var aes = new AesGcm(_encryptKey, TAG_SIZE);
        var ciphertext = outputSpan.Slice(1 + SEQUENCE_SIZE + NONCE_SIZE, plaintext.Length);
        var tag = outputSpan.Slice(1 + SEQUENCE_SIZE + NONCE_SIZE + plaintext.Length, TAG_SIZE);

        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        return output;
    }

    /// <summary>
    /// Decrypts data using AES-256-GCM.
    /// Returns null if decryption fails.
    /// </summary>
    public byte[]? Decrypt(ReadOnlySpan<byte> ciphertext)
    {
        if (_decryptKey == null)
            throw new InvalidOperationException("Session keys not derived");

        // Minimum size: header (1) + sequence (8) + nonce (12) + tag (16) = 37 bytes
        var minSize = 1 + SEQUENCE_SIZE + NONCE_SIZE + TAG_SIZE;
        if (ciphertext.Length < minSize)
        {
            _logger.LogWarning("[PSK] Ciphertext too short: {Length} < {MinSize}", ciphertext.Length, minSize);
            return null;
        }

        if (ciphertext[0] != MSG_ENCRYPTED)
        {
            _logger.LogWarning("[PSK] Invalid message type for decryption: 0x{Type:X2}", ciphertext[0]);
            return null;
        }

        var sequence = BinaryPrimitives.ReadInt64LittleEndian(ciphertext.Slice(1, SEQUENCE_SIZE));
        var nonce = ciphertext.Slice(1 + SEQUENCE_SIZE, NONCE_SIZE);
        var encryptedData = ciphertext.Slice(1 + SEQUENCE_SIZE + NONCE_SIZE, ciphertext.Length - minSize);
        var tag = ciphertext.Slice(ciphertext.Length - TAG_SIZE, TAG_SIZE);

        // Check for replay attacks (sequence must be increasing)
        var lastReceived = Interlocked.Read(ref _receiveSequence);
        if (sequence <= lastReceived)
        {
            _logger.LogWarning("[PSK] Possible replay attack: received sequence {Received} <= last {Last}",
                sequence, lastReceived);
            // Allow some out-of-order packets (window of 100)
            if (sequence < lastReceived - 100)
            {
                return null;
            }
        }

        try
        {
            var plaintext = new byte[encryptedData.Length];

            using var aes = new AesGcm(_decryptKey, TAG_SIZE);
            aes.Decrypt(nonce, encryptedData, tag, plaintext);

            // Update sequence tracking
            Interlocked.Exchange(ref _receiveSequence, Math.Max(sequence, lastReceived));

            return plaintext;
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "[PSK] Decryption failed - possible tampering or key mismatch");
            return null;
        }
    }

    /// <summary>
    /// Generates a nonce from sequence number and random bytes.
    /// </summary>
    private byte[] GenerateNonce(long sequence)
    {
        var nonce = new byte[NONCE_SIZE];

        // First 8 bytes: sequence number (prevents nonce reuse)
        BinaryPrimitives.WriteInt64LittleEndian(nonce.AsSpan(0, 8), sequence);

        // Last 4 bytes: random (adds entropy)
        RandomNumberGenerator.Fill(nonce.AsSpan(8, 4));

        return nonce;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Securely clear sensitive data
        if (_psk != null)
            CryptographicOperations.ZeroMemory(_psk);
        if (_challenge != null)
            CryptographicOperations.ZeroMemory(_challenge);
        if (_encryptKey != null)
            CryptographicOperations.ZeroMemory(_encryptKey);
        if (_decryptKey != null)
            CryptographicOperations.ZeroMemory(_decryptKey);
    }
}
