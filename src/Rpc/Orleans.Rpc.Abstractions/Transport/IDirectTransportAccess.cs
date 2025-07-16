using System;
using LiteNetLib;
using Ruffles;

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
        /// Gets the underlying LiteNetLib peer for direct API access.
        /// Returns null if the current transport is not LiteNetLib.
        /// </summary>
        NetPeer GetLiteNetLibPeer();
        
        /// <summary>
        /// Gets the underlying Ruffles connection for direct API access.
        /// Returns null if the current transport is not Ruffles.
        /// </summary>
        Connection GetRufflesConnection();
        
        /// <summary>
        /// Gets the current transport type to help with transport-specific optimizations.
        /// </summary>
        RpcTransportType TransportType { get; }
        
        /// <summary>
        /// Checks if direct access is available for the current transport.
        /// </summary>
        bool IsDirectAccessAvailable { get; }
    }
    
    /// <summary>
    /// Extension methods for safer direct transport access.
    /// </summary>
    public static class DirectTransportAccessExtensions
    {
        /// <summary>
        /// Attempts to send data using direct LiteNetLib access.
        /// </summary>
        public static bool TrySendLiteNetLib(this IDirectTransportAccess access, byte[] data, DeliveryMethod deliveryMethod)
        {
            var peer = access.GetLiteNetLibPeer();
            if (peer != null && peer.ConnectionState == ConnectionState.Connected)
            {
                peer.Send(data, deliveryMethod);
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Attempts to send data using direct Ruffles access.
        /// </summary>
        public static bool TrySendRuffles(this IDirectTransportAccess access, byte[] data, byte channelId, bool reliable)
        {
            var connection = access.GetRufflesConnection();
            if (connection != null && connection.State == ConnectionState.Connected)
            {
                connection.Send(new ArraySegment<byte>(data), channelId, reliable, 0);
                return true;
            }
            return false;
        }
    }
}