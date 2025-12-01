// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

using System.Security.Cryptography;

namespace Shooter.Shared.GrainInterfaces;

/// <summary>
/// Grain interface for managing player authentication sessions.
/// Uses Pre-Shared Key (PSK) architecture for secure UDP communication.
/// </summary>
/// <remarks>
/// Each player has one session grain keyed by PlayerId.
/// The session stores a 256-bit random key used for DTLS-PSK encryption.
/// Sessions expire after a configurable period (default 4 hours).
/// </remarks>
public interface IPlayerSessionGrain : Orleans.IGrainWithStringKey
{
    /// <summary>
    /// Creates a new session for the player, generating a fresh session key.
    /// </summary>
    /// <param name="session">The session data including player name and role.</param>
    /// <returns>The created session with generated session key.</returns>
    Task<PlayerSession> CreateSessionAsync(CreateSessionRequest request);

    /// <summary>
    /// Gets the current session for this player, if valid.
    /// </summary>
    /// <returns>The session if valid and not expired, null otherwise.</returns>
    Task<PlayerSession?> GetSessionAsync();

    /// <summary>
    /// Validates a provided session key against the stored key.
    /// Uses constant-time comparison to prevent timing attacks.
    /// </summary>
    /// <param name="providedKey">The key to validate (base64 encoded).</param>
    /// <returns>True if the key matches and session is valid.</returns>
    Task<bool> ValidateSessionKeyAsync(string providedKey);

    /// <summary>
    /// Validates a provided session key against the stored key.
    /// Uses constant-time comparison to prevent timing attacks.
    /// </summary>
    /// <param name="providedKey">The key to validate (raw bytes).</param>
    /// <returns>True if the key matches and session is valid.</returns>
    Task<bool> ValidateSessionKeyAsync(byte[] providedKey);

    /// <summary>
    /// Revokes the current session, invalidating the session key.
    /// </summary>
    Task RevokeSessionAsync();

    /// <summary>
    /// Updates the last activity timestamp to prevent session expiry.
    /// </summary>
    Task TouchSessionAsync();
}

/// <summary>
/// Request to create a new player session.
/// </summary>
[Orleans.GenerateSerializer]
public record CreateSessionRequest
{
    /// <summary>
    /// The player's display name.
    /// </summary>
    [Orleans.Id(0)]
    public required string PlayerName { get; init; }

    /// <summary>
    /// The role assigned to this player.
    /// </summary>
    [Orleans.Id(1)]
    public UserRole Role { get; init; } = UserRole.Guest;

    /// <summary>
    /// Optional session duration override. Defaults to 4 hours.
    /// </summary>
    [Orleans.Id(2)]
    public TimeSpan? SessionDuration { get; init; }
}

/// <summary>
/// Represents an active player session with authentication credentials.
/// </summary>
[Orleans.GenerateSerializer]
public record PlayerSession
{
    /// <summary>
    /// The unique player identifier (grain key).
    /// </summary>
    [Orleans.Id(0)]
    public required string PlayerId { get; init; }

    /// <summary>
    /// The player's display name.
    /// </summary>
    [Orleans.Id(1)]
    public required string PlayerName { get; init; }

    /// <summary>
    /// The role assigned to this player for authorization.
    /// </summary>
    [Orleans.Id(2)]
    public UserRole Role { get; init; } = UserRole.Guest;

    /// <summary>
    /// The 256-bit session key for DTLS-PSK authentication (base64 encoded).
    /// This key is shared with the client and used to establish encrypted UDP channels.
    /// </summary>
    [Orleans.Id(3)]
    public required string SessionKey { get; init; }

    /// <summary>
    /// When the session was created.
    /// </summary>
    [Orleans.Id(4)]
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// When the session expires and becomes invalid.
    /// </summary>
    [Orleans.Id(5)]
    public DateTime ExpiresAt { get; init; }

    /// <summary>
    /// Last activity timestamp for session keep-alive.
    /// </summary>
    [Orleans.Id(6)]
    public DateTime LastActivityAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this session is still valid (not expired).
    /// </summary>
    public bool IsValid => DateTime.UtcNow < ExpiresAt;

    /// <summary>
    /// Gets the session key as raw bytes for cryptographic operations.
    /// </summary>
    public byte[] GetSessionKeyBytes() => Convert.FromBase64String(SessionKey);
}

/// <summary>
/// User roles for authorization in Granville RPC.
/// </summary>
[Orleans.GenerateSerializer]
public enum UserRole : byte
{
    /// <summary>
    /// Unauthenticated or guest user with minimal permissions.
    /// </summary>
    Guest = 0,

    /// <summary>
    /// Authenticated regular user/player.
    /// </summary>
    User = 1,

    /// <summary>
    /// Server component (ActionServer, internal services).
    /// </summary>
    Server = 2,

    /// <summary>
    /// Administrator with full permissions.
    /// </summary>
    Admin = 3
}
