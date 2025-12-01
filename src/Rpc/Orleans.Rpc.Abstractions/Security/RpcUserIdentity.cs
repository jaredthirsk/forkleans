// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

using System;
using Orleans;

namespace Granville.Rpc.Security;

/// <summary>
/// Represents an authenticated user identity in Granville RPC.
/// Populated from the PSK session after successful handshake.
/// </summary>
[GenerateSerializer]
[Immutable]
public sealed record RpcUserIdentity
{
    /// <summary>
    /// The unique player/user identifier (from PSK identity).
    /// </summary>
    [Id(0)]
    public required string UserId { get; init; }

    /// <summary>
    /// The user's display name.
    /// </summary>
    [Id(1)]
    public required string UserName { get; init; }

    /// <summary>
    /// The user's role for authorization decisions.
    /// </summary>
    [Id(2)]
    public UserRole Role { get; init; } = UserRole.Guest;

    /// <summary>
    /// When the user was authenticated (handshake completion time).
    /// </summary>
    [Id(3)]
    public DateTime AuthenticatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// The connection ID associated with this identity.
    /// </summary>
    [Id(4)]
    public string? ConnectionId { get; init; }
}
