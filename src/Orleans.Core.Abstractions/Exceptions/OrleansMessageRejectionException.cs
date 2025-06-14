using System;
using System.Runtime.Serialization;

namespace Forkleans.Runtime
{
    /// <summary>
    /// Indicates that an Orleans message was rejected.
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class ForkleansMessageRejectionException : ForkleansException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ForkleansMessageRejectionException"/> class.
        /// </summary>
        /// <param name="message">
        /// The message.
        /// </param>
        internal ForkleansMessageRejectionException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ForkleansMessageRejectionException"/> class.
        /// </summary>
        /// <param name="message">
        /// The message.
        /// </param>
        /// <param name="innerException">
        /// The inner exception.
        /// </param>
        internal ForkleansMessageRejectionException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ForkleansMessageRejectionException"/> class. 
        /// </summary>
        /// <param name="info">
        /// The serialization info.
        /// </param>
        /// <param name="context">
        /// The context.
        /// </param>
        /// <exception cref="SerializationException">
        /// The class name is <see langword="null"/> or <see cref="P:System.Exception.HResult"/> is zero (0).
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="info"/> is <see langword="null"/>.
        /// </exception>
        [Obsolete]
        protected ForkleansMessageRejectionException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}

