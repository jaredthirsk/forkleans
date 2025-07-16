using System.Threading.Tasks;

namespace Orleans.Rpc.Transport
{
    /// <summary>
    /// Bypass API for sending messages directly through the transport layer,
    /// skipping RPC serialization and method dispatch overhead.
    /// Provides ~0.3ms overhead compared to ~1ms for full RPC.
    /// </summary>
    public interface IGranvilleBypass
    {
        /// <summary>
        /// Sends data using unreliable delivery (no guarantees, lowest latency).
        /// Ideal for position updates, non-critical state.
        /// </summary>
        Task SendUnreliableAsync(byte[] data);
        
        /// <summary>
        /// Sends data using reliable ordered delivery on the specified channel.
        /// Messages arrive in order within the channel.
        /// </summary>
        Task SendReliableOrderedAsync(byte[] data, byte channel = 0);
        
        /// <summary>
        /// Sends data using unreliable sequenced delivery on the specified channel.
        /// Out-of-order messages are dropped, only latest is processed.
        /// </summary>
        Task SendUnreliableSequencedAsync(byte[] data, byte channel = 0);
        
        /// <summary>
        /// Sends data using reliable unordered delivery.
        /// Messages are guaranteed to arrive but may be out of order.
        /// </summary>
        Task SendReliableUnorderedAsync(byte[] data);
    }
}