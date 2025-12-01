// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

using System.Net;

namespace Granville.Rpc.Security.Authorization;

/// <summary>
/// Provides access to security context for the current RPC request.
/// Uses <see cref="AsyncLocal{T}"/> to flow through async call chains, similar to
/// Orleans' RequestContext.
/// </summary>
/// <remarks>
/// This context is set by RpcConnection before processing each request
/// and is available to grain methods and authorization filters.
/// </remarks>
public static class RpcSecurityContext
{
    private static readonly AsyncLocal<SecurityContextData?> _current = new();

    /// <summary>
    /// Gets the authenticated user for the current request.
    /// Returns null if no user is authenticated (anonymous request or
    /// PSK transport not enabled).
    /// </summary>
    public static RpcUserIdentity? CurrentUser => _current.Value?.User;

    /// <summary>
    /// Gets the connection ID for the current request.
    /// </summary>
    public static string? ConnectionId => _current.Value?.ConnectionId;

    /// <summary>
    /// Gets the remote endpoint for the current request.
    /// </summary>
    public static IPEndPoint? RemoteEndpoint => _current.Value?.RemoteEndpoint;

    /// <summary>
    /// Gets the request ID for correlation/logging.
    /// </summary>
    public static Guid RequestId => _current.Value?.RequestId ?? Guid.Empty;

    /// <summary>
    /// Returns true if the current request has an authenticated user.
    /// </summary>
    public static bool IsAuthenticated => _current.Value?.User != null;

    /// <summary>
    /// Sets the security context for the duration of request processing.
    /// Returns an <see cref="IDisposable"/> that restores the previous context when disposed.
    /// </summary>
    /// <param name="user">The authenticated user, or null for anonymous.</param>
    /// <param name="connectionId">The connection ID.</param>
    /// <param name="remoteEndpoint">The remote endpoint.</param>
    /// <param name="requestId">Optional request ID for correlation.</param>
    /// <returns>A disposable scope that restores the previous context.</returns>
    public static IDisposable SetContext(
        RpcUserIdentity? user,
        string connectionId,
        IPEndPoint remoteEndpoint,
        Guid? requestId = null)
    {
        var previous = _current.Value;
        _current.Value = new SecurityContextData
        {
            User = user,
            ConnectionId = connectionId,
            RemoteEndpoint = remoteEndpoint,
            RequestId = requestId ?? Guid.NewGuid()
        };
        return new ContextScope(previous);
    }

    /// <summary>
    /// Clears the current security context.
    /// </summary>
    internal static void Clear()
    {
        _current.Value = null;
    }

    private sealed class SecurityContextData
    {
        public RpcUserIdentity? User { get; init; }
        public string? ConnectionId { get; init; }
        public IPEndPoint? RemoteEndpoint { get; init; }
        public Guid RequestId { get; init; }
    }

    private sealed class ContextScope : IDisposable
    {
        private readonly SecurityContextData? _previous;
        private bool _disposed;

        public ContextScope(SecurityContextData? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _current.Value = _previous;
        }
    }
}
