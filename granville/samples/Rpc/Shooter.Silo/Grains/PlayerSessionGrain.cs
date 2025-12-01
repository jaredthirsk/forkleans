// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Shooter.Shared.GrainInterfaces;

namespace Shooter.Silo.Grains;

/// <summary>
/// Grain implementation for managing player authentication sessions.
/// Generates and validates 256-bit PSK session keys for DTLS encryption.
/// </summary>
public class PlayerSessionGrain : Grain, IPlayerSessionGrain
{
    private readonly IPersistentState<PlayerSessionState> _state;
    private readonly ILogger<PlayerSessionGrain> _logger;

    /// <summary>
    /// Default session duration (4 hours).
    /// </summary>
    private static readonly TimeSpan DefaultSessionDuration = TimeSpan.FromHours(4);

    /// <summary>
    /// Session key size in bytes (256 bits).
    /// </summary>
    private const int SessionKeySize = 32;

    public PlayerSessionGrain(
        [PersistentState("playerSession", "sessionStore")]
        IPersistentState<PlayerSessionState> state,
        ILogger<PlayerSessionGrain> logger)
    {
        _state = state;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<PlayerSession> CreateSessionAsync(CreateSessionRequest request)
    {
        var playerId = this.GetPrimaryKeyString();
        var sessionDuration = request.SessionDuration ?? DefaultSessionDuration;

        // Generate cryptographically secure random session key
        var sessionKeyBytes = RandomNumberGenerator.GetBytes(SessionKeySize);
        var sessionKey = Convert.ToBase64String(sessionKeyBytes);

        var now = DateTime.UtcNow;
        var session = new PlayerSession
        {
            PlayerId = playerId,
            PlayerName = request.PlayerName,
            Role = request.Role,
            SessionKey = sessionKey,
            CreatedAt = now,
            ExpiresAt = now.Add(sessionDuration),
            LastActivityAt = now
        };

        // Store session state
        _state.State = new PlayerSessionState
        {
            SessionKeyHash = ComputeKeyHash(sessionKeyBytes),
            PlayerName = request.PlayerName,
            Role = request.Role,
            CreatedAt = now,
            ExpiresAt = session.ExpiresAt,
            LastActivityAt = now
        };

        _logger.LogInformation(
            "Created session for player {PlayerId} ({PlayerName}) with role {Role}, expires at {ExpiresAt}",
            playerId, request.PlayerName, request.Role, session.ExpiresAt);

        // Note: We intentionally don't persist the session key itself,
        // only a hash for validation. The actual key is returned to the client
        // and should be stored securely on their end.
        // For this demo, we store the actual key bytes for validation.
        _state.State.SessionKeyBytes = sessionKeyBytes;

        return Task.FromResult(session);
    }

    /// <inheritdoc/>
    public Task<PlayerSession?> GetSessionAsync()
    {
        if (!HasValidSession())
        {
            return Task.FromResult<PlayerSession?>(null);
        }

        var playerId = this.GetPrimaryKeyString();
        var session = new PlayerSession
        {
            PlayerId = playerId,
            PlayerName = _state.State.PlayerName,
            Role = _state.State.Role,
            SessionKey = Convert.ToBase64String(_state.State.SessionKeyBytes),
            CreatedAt = _state.State.CreatedAt,
            ExpiresAt = _state.State.ExpiresAt,
            LastActivityAt = _state.State.LastActivityAt
        };

        return Task.FromResult<PlayerSession?>(session);
    }

    /// <inheritdoc/>
    public Task<bool> ValidateSessionKeyAsync(string providedKey)
    {
        if (string.IsNullOrEmpty(providedKey))
        {
            return Task.FromResult(false);
        }

        try
        {
            var providedKeyBytes = Convert.FromBase64String(providedKey);
            return ValidateSessionKeyAsync(providedKeyBytes);
        }
        catch (FormatException)
        {
            _logger.LogWarning(
                "Invalid base64 session key format for player {PlayerId}",
                this.GetPrimaryKeyString());
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    public Task<bool> ValidateSessionKeyAsync(byte[] providedKey)
    {
        if (!HasValidSession())
        {
            _logger.LogDebug(
                "Session validation failed for player {PlayerId}: no valid session",
                this.GetPrimaryKeyString());
            return Task.FromResult(false);
        }

        if (providedKey == null || providedKey.Length != SessionKeySize)
        {
            _logger.LogWarning(
                "Session validation failed for player {PlayerId}: invalid key length",
                this.GetPrimaryKeyString());
            return Task.FromResult(false);
        }

        // Use constant-time comparison to prevent timing attacks
        var isValid = CryptographicOperations.FixedTimeEquals(
            _state.State.SessionKeyBytes,
            providedKey);

        if (isValid)
        {
            // Update last activity on successful validation
            _state.State.LastActivityAt = DateTime.UtcNow;
            _logger.LogDebug(
                "Session validated successfully for player {PlayerId}",
                this.GetPrimaryKeyString());
        }
        else
        {
            _logger.LogWarning(
                "Session validation failed for player {PlayerId}: key mismatch",
                this.GetPrimaryKeyString());
        }

        return Task.FromResult(isValid);
    }

    /// <inheritdoc/>
    public Task RevokeSessionAsync()
    {
        var playerId = this.GetPrimaryKeyString();

        if (_state.State.SessionKeyBytes != null)
        {
            // Securely clear the session key from memory
            CryptographicOperations.ZeroMemory(_state.State.SessionKeyBytes);
        }

        _state.State = new PlayerSessionState();

        _logger.LogInformation("Session revoked for player {PlayerId}", playerId);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task TouchSessionAsync()
    {
        if (HasValidSession())
        {
            _state.State.LastActivityAt = DateTime.UtcNow;
            _logger.LogDebug(
                "Session touched for player {PlayerId}",
                this.GetPrimaryKeyString());
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Checks if there is a valid, non-expired session.
    /// </summary>
    private bool HasValidSession()
    {
        return _state.State.SessionKeyBytes != null
            && _state.State.SessionKeyBytes.Length == SessionKeySize
            && DateTime.UtcNow < _state.State.ExpiresAt;
    }

    /// <summary>
    /// Computes a SHA-256 hash of the session key for logging/auditing purposes.
    /// </summary>
    private static string ComputeKeyHash(byte[] key)
    {
        var hash = SHA256.HashData(key);
        return Convert.ToBase64String(hash)[..16]; // Truncated for brevity
    }
}

/// <summary>
/// Persistent state for player sessions.
/// </summary>
[Orleans.GenerateSerializer]
public class PlayerSessionState
{
    /// <summary>
    /// The actual session key bytes for validation.
    /// In production, consider using a secure key store.
    /// </summary>
    [Orleans.Id(0)]
    public byte[] SessionKeyBytes { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Hash of the session key for logging (not the actual key).
    /// </summary>
    [Orleans.Id(1)]
    public string SessionKeyHash { get; set; } = string.Empty;

    /// <summary>
    /// The player's display name.
    /// </summary>
    [Orleans.Id(2)]
    public string PlayerName { get; set; } = string.Empty;

    /// <summary>
    /// The player's assigned role.
    /// </summary>
    [Orleans.Id(3)]
    public UserRole Role { get; set; } = UserRole.Guest;

    /// <summary>
    /// When the session was created.
    /// </summary>
    [Orleans.Id(4)]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the session expires.
    /// </summary>
    [Orleans.Id(5)]
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Last activity timestamp.
    /// </summary>
    [Orleans.Id(6)]
    public DateTime LastActivityAt { get; set; }
}
