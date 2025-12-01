// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

using Orleans;

namespace Granville.Rpc.Security;

/// <summary>
/// User roles for authorization in Granville RPC.
/// Higher numeric values have more permissions.
/// </summary>
/// <remarks>
/// Role comparison uses >= semantics, so [RequireRole(User)] allows
/// User, Server, and Admin roles.
/// </remarks>
[GenerateSerializer]
public enum UserRole : byte
{
    /// <summary>
    /// Unauthenticated or anonymous user (no PSK session).
    /// </summary>
    Anonymous = 0,

    /// <summary>
    /// Guest user with minimal permissions.
    /// </summary>
    Guest = 1,

    /// <summary>
    /// Authenticated regular user/player.
    /// </summary>
    User = 2,

    /// <summary>
    /// Server component (ActionServer, internal services).
    /// </summary>
    Server = 3,

    /// <summary>
    /// Administrator with full permissions.
    /// </summary>
    Admin = 4
}
