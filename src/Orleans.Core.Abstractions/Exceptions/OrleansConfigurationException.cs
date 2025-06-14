using System;
using System.Runtime.Serialization;

namespace Forkleans.Runtime
{
    /// <summary>
    /// Represents a configuration exception.
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class ForkleansConfigurationException : Exception
    {
        /// <inheritdoc />
        public ForkleansConfigurationException(string message)
            : base(message)
        {
        }

        /// <inheritdoc />
        public ForkleansConfigurationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <inheritdoc />
        /// <exception cref="SerializationException">The class name is <see langword="null" /> or <see cref="P:System.Exception.HResult" /> is zero (0).</exception>
        /// <exception cref="ArgumentNullException"><paramref name="info" /> is <see langword="null" />.</exception>
        [Obsolete]
        private ForkleansConfigurationException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}