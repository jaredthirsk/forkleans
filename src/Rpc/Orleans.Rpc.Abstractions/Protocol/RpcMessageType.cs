namespace Forkleans.Rpc.Protocol
{
    /// <summary>
    /// RPC message types.
    /// </summary>
    public enum RpcMessageType
    {
        /// <summary>
        /// Handshake message for establishing connections.
        /// </summary>
        Handshake = 1,

        /// <summary>
        /// Request message for invoking methods.
        /// </summary>
        Request = 2,

        /// <summary>
        /// Response message containing method results.
        /// </summary>
        Response = 3,

        /// <summary>
        /// Heartbeat message for connection health checks.
        /// </summary>
        Heartbeat = 4,

        /// <summary>
        /// Error message for protocol-level errors.
        /// </summary>
        Error = 5
    }
}