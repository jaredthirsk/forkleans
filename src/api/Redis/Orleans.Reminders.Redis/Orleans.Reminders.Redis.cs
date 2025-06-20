//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------
namespace Forkleans.Configuration
{
    public partial class RedisReminderTableOptions
    {
        public StackExchange.Redis.ConfigurationOptions ConfigurationOptions { get { throw null; } set { } }

        public System.Func<RedisReminderTableOptions, System.Threading.Tasks.Task<StackExchange.Redis.IConnectionMultiplexer>> CreateMultiplexer { get { throw null; } set { } }

        public System.TimeSpan? EntryExpiry { get { throw null; } set { } }

        public static System.Threading.Tasks.Task<StackExchange.Redis.IConnectionMultiplexer> DefaultCreateMultiplexer(RedisReminderTableOptions options) { throw null; }
    }

    public partial class RedisReminderTableOptionsValidator : IConfigurationValidator
    {
        public RedisReminderTableOptionsValidator(Microsoft.Extensions.Options.IOptions<RedisReminderTableOptions> options) { }

        public void ValidateConfiguration() { }
    }
}

namespace Forkleans.Hosting
{
    public static partial class SiloBuilderReminderExtensions
    {
        public static Microsoft.Extensions.DependencyInjection.IServiceCollection UseRedisReminderService(this Microsoft.Extensions.DependencyInjection.IServiceCollection services, System.Action<Configuration.RedisReminderTableOptions> configure) { throw null; }

        public static ISiloBuilder UseRedisReminderService(this ISiloBuilder builder, System.Action<Configuration.RedisReminderTableOptions> configure) { throw null; }
    }
}

namespace Forkleans.Reminders.Redis
{
    [GenerateSerializer]
    public partial class RedisRemindersException : System.Exception
    {
        public RedisRemindersException() { }

        [System.Obsolete]
        protected RedisRemindersException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) { }

        public RedisRemindersException(string message, System.Exception inner) { }

        public RedisRemindersException(string message) { }
    }
}

namespace ForkleansCodeGen.Forkleans.Reminders.Redis
{
    [System.CodeDom.Compiler.GeneratedCode("ForkleansCodeGen", "9.0.0.0")]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public sealed partial class Codec_RedisRemindersException : global::Forkleans.Serialization.Codecs.IFieldCodec<global::Forkleans.Reminders.Redis.RedisRemindersException>, global::Forkleans.Serialization.Codecs.IFieldCodec, global::Forkleans.Serialization.Serializers.IBaseCodec<global::Forkleans.Reminders.Redis.RedisRemindersException>, global::Forkleans.Serialization.Serializers.IBaseCodec
    {
        public Codec_RedisRemindersException(global::Forkleans.Serialization.Serializers.IBaseCodec<System.Exception> _baseTypeSerializer) { }

        public void Deserialize<TReaderInput>(ref global::Forkleans.Serialization.Buffers.Reader<TReaderInput> reader, global::Forkleans.Reminders.Redis.RedisRemindersException instance) { }

        public global::Forkleans.Reminders.Redis.RedisRemindersException ReadValue<TReaderInput>(ref global::Forkleans.Serialization.Buffers.Reader<TReaderInput> reader, global::Forkleans.Serialization.WireProtocol.Field field) { throw null; }

        public void Serialize<TBufferWriter>(ref global::Forkleans.Serialization.Buffers.Writer<TBufferWriter> writer, global::Forkleans.Reminders.Redis.RedisRemindersException instance)
            where TBufferWriter : System.Buffers.IBufferWriter<byte> { }

        public void WriteField<TBufferWriter>(ref global::Forkleans.Serialization.Buffers.Writer<TBufferWriter> writer, uint fieldIdDelta, System.Type expectedType, global::Forkleans.Reminders.Redis.RedisRemindersException value)
            where TBufferWriter : System.Buffers.IBufferWriter<byte> { }
    }

    [System.CodeDom.Compiler.GeneratedCode("ForkleansCodeGen", "9.0.0.0")]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public sealed partial class Copier_RedisRemindersException : global::Forkleans.Serialization.GeneratedCodeHelpers.ForkleansGeneratedCodeHelper.ExceptionCopier<global::Forkleans.Reminders.Redis.RedisRemindersException, System.Exception>
    {
        public Copier_RedisRemindersException(global::Forkleans.Serialization.Serializers.ICodecProvider codecProvider) : base(default(Serialization.Serializers.ICodecProvider)!) { }
    }
}