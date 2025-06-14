#nullable enable

using Microsoft.Extensions.Options;
using Forkleans.Runtime;
using Forkleans.Serialization;

namespace Forkleans.Streaming.JsonConverters
{
    internal class StreamingConverterConfigurator : IPostConfigureOptions<ForkleansJsonSerializerOptions>
    {
        private readonly IRuntimeClient _runtimeClient;

        public StreamingConverterConfigurator(IRuntimeClient runtimeClient)
        {
            _runtimeClient = runtimeClient;
        }

        public void PostConfigure(string? name, OrleansJsonSerializerOptions options)
        {
            options.JsonSerializerSettings.Converters.Add(new StreamImplConverter(_runtimeClient));
        }
    }
}
