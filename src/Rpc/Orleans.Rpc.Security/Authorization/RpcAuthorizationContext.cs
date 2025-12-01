// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

using System.Net;
using System.Reflection;
using Orleans.Runtime;

namespace Granville.Rpc.Security.Authorization;

/// <summary>
/// Context passed to authorization filters containing all information
/// needed to make an authorization decision.
/// </summary>
public sealed class RpcAuthorizationContext
{
    /// <summary>
    /// The grain interface being called.
    /// </summary>
    public required Type GrainInterface { get; init; }

    /// <summary>
    /// The interface method being called.
    /// </summary>
    public required MethodInfo Method { get; init; }

    /// <summary>
    /// The target grain ID.
    /// </summary>
    public required GrainId GrainId { get; init; }

    /// <summary>
    /// The authenticated user, or null if anonymous.
    /// </summary>
    public RpcUserIdentity? User { get; init; }

    /// <summary>
    /// The remote endpoint of the caller.
    /// </summary>
    public IPEndPoint? RemoteEndpoint { get; init; }

    /// <summary>
    /// The connection ID.
    /// </summary>
    public string? ConnectionId { get; init; }

    /// <summary>
    /// The request ID for correlation.
    /// </summary>
    public Guid RequestId { get; init; }

    /// <summary>
    /// The RPC method ID (for logging).
    /// </summary>
    public int MethodId { get; init; }

    /// <summary>
    /// Checks if the method has the specified attribute.
    /// </summary>
    /// <typeparam name="T">The attribute type to check for.</typeparam>
    /// <returns>True if the method has the attribute; otherwise, false.</returns>
    public bool HasMethodAttribute<T>() where T : Attribute =>
        Method.GetCustomAttribute<T>() != null;

    /// <summary>
    /// Checks if the interface has the specified attribute.
    /// </summary>
    /// <typeparam name="T">The attribute type to check for.</typeparam>
    /// <returns>True if the interface has the attribute; otherwise, false.</returns>
    public bool HasInterfaceAttribute<T>() where T : Attribute =>
        GrainInterface.GetCustomAttribute<T>() != null;

    /// <summary>
    /// Gets all attributes of the specified type from method and interface.
    /// </summary>
    /// <typeparam name="T">The attribute type to retrieve.</typeparam>
    /// <returns>All matching attributes from both method and interface.</returns>
    public IEnumerable<T> GetAttributes<T>() where T : Attribute =>
        Method.GetCustomAttributes<T>()
            .Concat(GrainInterface.GetCustomAttributes<T>());
}
