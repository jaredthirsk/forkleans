// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

namespace Granville.Rpc.Security;

/// <summary>
/// Interface for accessing the authenticated user associated with a connection.
/// Implemented by the security transport to provide identity info.
/// </summary>
public interface IConnectionUserAccessor
{
    /// <summary>
    /// Gets the authenticated user for the given connection ID.
    /// Returns null if the connection is anonymous or not found.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    /// <returns>The authenticated user identity, or null if not authenticated.</returns>
    RpcUserIdentity? GetUserForConnection(string connectionId);
}
