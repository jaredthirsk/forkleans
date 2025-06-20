using System;
using Forkleans.Serialization;

namespace Forkleans.Storage
{
    /// <summary>
    /// Grain storage serializer that uses Newtonsoft.Json
    /// </summary>
    public class JsonGrainStorageSerializer : IGrainStorageSerializer
    {
        private readonly ForkleansJsonSerializer _orleansJsonSerializer;

        /// <summary>
        /// Initializes a new instance of the <see cref="JsonGrainStorageSerializer"/> class.
        /// </summary>
        public JsonGrainStorageSerializer(ForkleansJsonSerializer orleansJsonSerializer)
        {
            _orleansJsonSerializer = orleansJsonSerializer;
        }

        /// <inheritdoc/>
        public BinaryData Serialize<T>(T value)
        {
            var data = _orleansJsonSerializer.Serialize(value, typeof(T));
            return new BinaryData(data);
        }

        /// <inheritdoc/>
        public T Deserialize<T>(BinaryData input)
        {
            return (T)_orleansJsonSerializer.Deserialize(typeof(T), input.ToString());
        }
    }
}
