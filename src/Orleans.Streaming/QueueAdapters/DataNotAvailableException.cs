using System;
using System.Runtime.Serialization;
using Forkleans.Runtime;

namespace Forkleans.Streams
{
    /// <summary>
    /// Exception indicates that the requested data is not available.
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class DataNotAvailableException : ForkleansException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DataNotAvailableException"/> class.
        /// </summary>
        public DataNotAvailableException() : this("Data not found") { }

        /// <summary>
        /// Initializes a new instance of the <see cref="DataNotAvailableException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        public DataNotAvailableException(string message) : base(message) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="DataNotAvailableException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="inner">The inner.</param>
        public DataNotAvailableException(string message, Exception inner) : base(message, inner) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="DataNotAvailableException"/> class.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The context.</param>
        [Obsolete]
        protected DataNotAvailableException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// Indicates that the queue message cache is full.
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class CacheFullException : ForkleansException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CacheFullException"/> class.
        /// </summary>
        public CacheFullException() : this("Queue message cache is full") { }

        /// <summary>
        /// Initializes a new instance of the <see cref="CacheFullException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        public CacheFullException(string message) : base(message) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="CacheFullException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="inner">The inner.</param>
        public CacheFullException(string message, Exception inner) : base(message, inner) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="CacheFullException"/> class.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The context.</param>
        [Obsolete]
        private CacheFullException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
