//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------
namespace Forkleans.Serialization
{
    [RegisterSerializer]
    public sealed partial class ByteStringCodec : Codecs.IFieldCodec<Google.Protobuf.ByteString>, Codecs.IFieldCodec
    {
        Google.Protobuf.ByteString Codecs.IFieldCodec<Google.Protobuf.ByteString>.ReadValue<TInput>(ref Buffers.Reader<TInput> reader, WireProtocol.Field field) { throw null; }

        void Codecs.IFieldCodec<Google.Protobuf.ByteString>.WriteField<TBufferWriter>(ref Buffers.Writer<TBufferWriter> writer, uint fieldIdDelta, System.Type expectedType, Google.Protobuf.ByteString value) { }
    }

    [RegisterCopier]
    public sealed partial class ByteStringCopier : Cloning.IDeepCopier<Google.Protobuf.ByteString>, Cloning.IDeepCopier
    {
        public Google.Protobuf.ByteString DeepCopy(Google.Protobuf.ByteString input, Cloning.CopyContext context) { throw null; }
    }

    [RegisterSerializer]
    public sealed partial class MapFieldCodec<TKey, TValue> : Codecs.IFieldCodec<Google.Protobuf.Collections.MapField<TKey, TValue>>, Codecs.IFieldCodec
    {
        public MapFieldCodec(Codecs.IFieldCodec<TKey> keyCodec, Codecs.IFieldCodec<TValue> valueCodec) { }

        public Google.Protobuf.Collections.MapField<TKey, TValue> ReadValue<TInput>(ref Buffers.Reader<TInput> reader, WireProtocol.Field field) { throw null; }

        public void WriteField<TBufferWriter>(ref Buffers.Writer<TBufferWriter> writer, uint fieldIdDelta, System.Type expectedType, Google.Protobuf.Collections.MapField<TKey, TValue> value)
            where TBufferWriter : System.Buffers.IBufferWriter<byte> { }
    }

    [RegisterCopier]
    public sealed partial class MapFieldCopier<TKey, TValue> : Cloning.IDeepCopier<Google.Protobuf.Collections.MapField<TKey, TValue>>, Cloning.IDeepCopier, Cloning.IBaseCopier<Google.Protobuf.Collections.MapField<TKey, TValue>>, Cloning.IBaseCopier
    {
        public MapFieldCopier(Cloning.IDeepCopier<TKey> keyCopier, Cloning.IDeepCopier<TValue> valueCopier) { }

        public void DeepCopy(Google.Protobuf.Collections.MapField<TKey, TValue> input, Google.Protobuf.Collections.MapField<TKey, TValue> output, Cloning.CopyContext context) { }

        public Google.Protobuf.Collections.MapField<TKey, TValue> DeepCopy(Google.Protobuf.Collections.MapField<TKey, TValue> input, Cloning.CopyContext context) { throw null; }
    }

    [Alias("protobuf")]
    public sealed partial class ProtobufCodec : Serializers.IGeneralizedCodec, Codecs.IFieldCodec, Cloning.IGeneralizedCopier, Cloning.IDeepCopier, ITypeFilter
    {
        public const string WellKnownAlias = "protobuf";
        public ProtobufCodec(System.Collections.Generic.IEnumerable<Serializers.ICodecSelector> serializableTypeSelectors, System.Collections.Generic.IEnumerable<Serializers.ICopierSelector> copyableTypeSelectors) { }

        public object DeepCopy(object input, Cloning.CopyContext context) { throw null; }

        bool Cloning.IGeneralizedCopier.IsSupportedType(System.Type type) { throw null; }

        object Codecs.IFieldCodec.ReadValue<TInput>(ref Buffers.Reader<TInput> reader, WireProtocol.Field field) { throw null; }

        void Codecs.IFieldCodec.WriteField<TBufferWriter>(ref Buffers.Writer<TBufferWriter> writer, uint fieldIdDelta, System.Type expectedType, object value) { }

        bool? ITypeFilter.IsTypeAllowed(System.Type type) { throw null; }

        bool Serializers.IGeneralizedCodec.IsSupportedType(System.Type type) { throw null; }
    }

    [RegisterSerializer]
    public sealed partial class RepeatedFieldCodec<T> : Codecs.IFieldCodec<Google.Protobuf.Collections.RepeatedField<T>>, Codecs.IFieldCodec
    {
        public RepeatedFieldCodec(Codecs.IFieldCodec<T> fieldCodec) { }

        public Google.Protobuf.Collections.RepeatedField<T> ReadValue<TInput>(ref Buffers.Reader<TInput> reader, WireProtocol.Field field) { throw null; }

        public void WriteField<TBufferWriter>(ref Buffers.Writer<TBufferWriter> writer, uint fieldIdDelta, System.Type expectedType, Google.Protobuf.Collections.RepeatedField<T> value)
            where TBufferWriter : System.Buffers.IBufferWriter<byte> { }
    }

    [RegisterCopier]
    public sealed partial class RepeatedFieldCopier<T> : Cloning.IDeepCopier<Google.Protobuf.Collections.RepeatedField<T>>, Cloning.IDeepCopier, Cloning.IBaseCopier<Google.Protobuf.Collections.RepeatedField<T>>, Cloning.IBaseCopier
    {
        public RepeatedFieldCopier(Cloning.IDeepCopier<T> valueCopier) { }

        public void DeepCopy(Google.Protobuf.Collections.RepeatedField<T> input, Google.Protobuf.Collections.RepeatedField<T> output, Cloning.CopyContext context) { }

        public Google.Protobuf.Collections.RepeatedField<T> DeepCopy(Google.Protobuf.Collections.RepeatedField<T> input, Cloning.CopyContext context) { throw null; }
    }

    public static partial class SerializationHostingExtensions
    {
        public static ISerializerBuilder AddProtobufSerializer(this ISerializerBuilder serializerBuilder, System.Func<System.Type, bool> isSerializable, System.Func<System.Type, bool> isCopyable) { throw null; }

        public static ISerializerBuilder AddProtobufSerializer(this ISerializerBuilder serializerBuilder) { throw null; }
    }
}