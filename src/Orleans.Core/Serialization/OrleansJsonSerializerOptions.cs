using System;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Forkleans.Serialization
{
    public class ForkleansJsonSerializerOptions
    {
        public JsonSerializerSettings JsonSerializerSettings { get; set; }

        public ForkleansJsonSerializerOptions()
        {
            JsonSerializerSettings = OrleansJsonSerializerSettings.GetDefaultSerializerSettings();
        }
    }

    public class ConfigureOrleansJsonSerializerOptions : IPostConfigureOptions<ForkleansJsonSerializerOptions>
    {
        private readonly IServiceProvider _serviceProvider;

        public ConfigureOrleansJsonSerializerOptions(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void PostConfigure(string name, ForkleansJsonSerializerOptions options)
        {
            OrleansJsonSerializerSettings.Configure(_serviceProvider, options.JsonSerializerSettings);
        }
    }
}
