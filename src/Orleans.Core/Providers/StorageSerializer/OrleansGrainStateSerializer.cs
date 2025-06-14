using System;
using System.Buffers;
using Forkleans.Serialization;

namespace Forkleans.Storage
{
    /// <summary>
    /// Grain storage serializer that uses the Orleans <see cref="Serializer"/>.
    /// </summary>
    public class ForkleansGrainStorageSerializer : IGrainStorageSerializer
    {
        private readonly Serializer serializer;

        /// <summary>
        /// Initializes a new instance of the <see cref="ForkleansGrainStorageSerializer"/> class.
        /// </summary>
        /// <param name="serializer">The serializer.</param>
        public ForkleansGrainStorageSerializer(Serializer serializer)
        {
            this.serializer = serializer;
        }

        /// <inheritdoc/>
        public BinaryData Serialize<T>(T value)
        {
            var buffer = new ArrayBufferWriter<byte>();
            this.serializer.Serialize(value, buffer);
            return new BinaryData(buffer.WrittenMemory);
        }

        /// <inheritdoc/>
        public T Deserialize<T>(BinaryData input)
        {
            return this.serializer.Deserialize<T>(input.ToMemory());
        }
    }
}
