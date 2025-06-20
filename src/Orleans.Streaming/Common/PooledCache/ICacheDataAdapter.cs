using Forkleans.Streams;

namespace Forkleans.Providers.Streams.Common
{
    /// <summary>
    /// Pooled queue cache stores data in tightly packed structures that need to be transformed to various
    ///   other formats quickly.  Since the data formats may change by queue type and data format,
    ///   this interface allows adapter developers to build custom data transforms appropriate for 
    ///   the various types of queue data.
    /// </summary>
    public interface ICacheDataAdapter
    {
        /// <summary>
        /// Converts a cached message to a batch container for delivery
        /// </summary>
        /// <param name="cachedMessage">The cached message.</param>
        /// <returns>The batch container.</returns>
        IBatchContainer GetBatchContainer(ref CachedMessage cachedMessage);

        /// <summary>
        /// Gets the stream sequence token from a cached message.
        /// </summary>
        /// <param name="cachedMessage">The cached message.</param>
        /// <returns>The sequence token.</returns>
        StreamSequenceToken GetSequenceToken(ref CachedMessage cachedMessage);
    }
}
