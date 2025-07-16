using System;

namespace Orleans.Rpc.Transport
{
    /// <summary>
    /// Provides direct access to underlying transport APIs for zero-overhead hot path scenarios.
    /// WARNING: Using direct transport access bypasses all Granville RPC abstractions including
    /// message tracking, error handling, and connection management. Use only for performance-critical paths.
    /// </summary>
    public interface IDirectTransportAccess
    {
        /// <summary>
        /// Gets the underlying transport object for direct API access.
        /// Returns null if direct access is not available.
        /// The returned object must be cast to the appropriate transport type.
        /// </summary>
        object? GetUnderlyingTransport();
        
        /// <summary>
        /// Gets the current transport type name to help with transport-specific optimizations.
        /// </summary>
        string TransportTypeName { get; }
        
        /// <summary>
        /// Checks if direct access is available for the current transport.
        /// </summary>
        bool IsDirectAccessAvailable { get; }
        
        /// <summary>
        /// Attempts to send data directly through the underlying transport.
        /// This is a generic method that works with any transport type.
        /// </summary>
        bool TrySendDirect(byte[] data, string target, bool reliable = false);
    }
}