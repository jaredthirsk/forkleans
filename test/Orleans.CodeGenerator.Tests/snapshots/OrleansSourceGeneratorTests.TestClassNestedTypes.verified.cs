#pragma warning disable CS1591, RS0016, RS0041
[assembly: global::Forkleans.ApplicationPartAttribute("TestProject")]
[assembly: global::Forkleans.ApplicationPartAttribute("Forkleans.Core.Abstractions")]
[assembly: global::Forkleans.ApplicationPartAttribute("Forkleans.Serialization")]
[assembly: global::Forkleans.ApplicationPartAttribute("Forkleans.Core")]
[assembly: global::Forkleans.ApplicationPartAttribute("Forkleans.Runtime")]
[assembly: global::Forkleans.Serialization.Configuration.TypeManifestProviderAttribute(typeof(ForkleansCodeGen.TestProject.Metadata_TestProject))]
namespace ForkleansCodeGen.TestProject
{
    using global::Forkleans.Serialization.Codecs;
    using global::Forkleans.Serialization.GeneratedCodeHelpers;

    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("ForkleansCodeGen", "9.0.0.0"), global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never), global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute]
    public sealed class Codec_DemoData : global::Forkleans.Serialization.Codecs.IFieldCodec<global::TestProject.DemoData>, global::Forkleans.Serialization.Serializers.IBaseCodec<global::TestProject.DemoData>
    {
        private readonly global::System.Type _codecFieldType = typeof(global::TestProject.DemoData);
        private readonly global::System.Type _type0 = typeof(global::TestProject.CyclicClass);
        private readonly global::Forkleans.Serialization.Codecs.IFieldCodec<global::TestProject.CyclicClass> _codec0;
        private readonly global::System.Type _type1 = typeof(global::TestProject.NestedClass1);
        private readonly global::Forkleans.Serialization.Codecs.IFieldCodec<global::TestProject.NestedClass1> _codec1;
        private readonly global::System.Type _type2 = typeof(global::System.Collections.Generic.List<global::TestProject.NestedClass1>);
        private readonly global::Forkleans.Serialization.Codecs.ListCodec<global::TestProject.NestedClass1> _codec2;
        public Codec_DemoData(global::Forkleans.Serialization.Serializers.ICodecProvider codecProvider)
        {
            _codec0 = ForkleansGeneratedCodeHelper.GetService<global::Forkleans.Serialization.Codecs.IFieldCodec<global::TestProject.CyclicClass>>(this, codecProvider);
            _codec1 = ForkleansGeneratedCodeHelper.GetService<global::Forkleans.Serialization.Codecs.IFieldCodec<global::TestProject.NestedClass1>>(this, codecProvider);
            _codec2 = ForkleansGeneratedCodeHelper.GetService<global::Forkleans.Serialization.Codecs.ListCodec<global::TestProject.NestedClass1>>(this, codecProvider);
        }

        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void Serialize<TBufferWriter>(ref global::Forkleans.Serialization.Buffers.Writer<TBufferWriter> writer, global::TestProject.DemoData instance)
            where TBufferWriter : global::System.Buffers.IBufferWriter<byte>
        {
            _codec1.WriteField(ref writer, 0U, _type1, instance.Nested1);
            _codec2.WriteField(ref writer, 1U, _type2, instance.NestedList);
            _codec0.WriteField(ref writer, 1U, _type0, instance.Cyclic);
        }

        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void Deserialize<TReaderInput>(ref global::Forkleans.Serialization.Buffers.Reader<TReaderInput> reader, global::TestProject.DemoData instance)
        {
            uint id = 0U;
            global::Forkleans.Serialization.WireProtocol.Field header = default;
            while (true)
            {
                reader.ReadFieldHeader(ref header);
                if (header.IsEndBaseOrEndObject)
                    break;
                id += header.FieldIdDelta;
                if (id == 0U)
                {
                    instance.Nested1 = _codec1.ReadValue(ref reader, header);
                    reader.ReadFieldHeader(ref header);
                    if (header.IsEndBaseOrEndObject)
                        break;
                    id += header.FieldIdDelta;
                }

                if (id == 1U)
                {
                    instance.NestedList = _codec2.ReadValue(ref reader, header);
                    reader.ReadFieldHeader(ref header);
                    if (header.IsEndBaseOrEndObject)
                        break;
                    id += header.FieldIdDelta;
                }

                if (id == 2U)
                {
                    instance.Cyclic = _codec0.ReadValue(ref reader, header);
                    reader.ReadFieldHeader(ref header);
                }

                reader.ConsumeEndBaseOrEndObject(ref header);
                break;
            }
        }

        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void WriteField<TBufferWriter>(ref global::Forkleans.Serialization.Buffers.Writer<TBufferWriter> writer, uint fieldIdDelta, global::System.Type expectedType, global::TestProject.DemoData @value)
            where TBufferWriter : global::System.Buffers.IBufferWriter<byte>
        {
            if (@value is null || @value.GetType() == typeof(global::TestProject.DemoData))
            {
                if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, @value))
                    return;
                writer.WriteStartObject(fieldIdDelta, expectedType, _codecFieldType);
                Serialize(ref writer, @value);
                writer.WriteEndObject();
            }
            else
                writer.SerializeUnexpectedType(fieldIdDelta, expectedType, @value);
        }

        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public global::TestProject.DemoData ReadValue<TReaderInput>(ref global::Forkleans.Serialization.Buffers.Reader<TReaderInput> reader, global::Forkleans.Serialization.WireProtocol.Field field)
        {
            if (field.IsReference)
                return ReferenceCodec.ReadReference<global::TestProject.DemoData, TReaderInput>(ref reader, field);
            field.EnsureWireTypeTagDelimited();
            global::System.Type valueType = field.FieldType;
            if (valueType is null || valueType == _codecFieldType)
            {
                var result = new global::TestProject.DemoData();
                ReferenceCodec.RecordObject(reader.Session, result);
                Deserialize(ref reader, result);
                return result;
            }

            return reader.DeserializeUnexpectedType<TReaderInput, global::TestProject.DemoData>(ref field);
        }
    }

    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("ForkleansCodeGen", "9.0.0.0"), global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never), global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute]
    public sealed class Copier_DemoData : global::Forkleans.Serialization.Cloning.IDeepCopier<global::TestProject.DemoData>, global::Forkleans.Serialization.Cloning.IBaseCopier<global::TestProject.DemoData>
    {
        private readonly global::Forkleans.Serialization.Cloning.IDeepCopier<global::TestProject.CyclicClass> _copier0;
        private readonly global::Forkleans.Serialization.Cloning.IDeepCopier<global::TestProject.NestedClass1> _copier1;
        private readonly global::Forkleans.Serialization.Codecs.ListCopier<global::TestProject.NestedClass1> _copier2;
        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public global::TestProject.DemoData DeepCopy(global::TestProject.DemoData original, global::Forkleans.Serialization.Cloning.CopyContext context)
        {
            if (context.TryGetCopy(original, out global::TestProject.DemoData existing))
                return existing;
            if (original.GetType() != typeof(global::TestProject.DemoData))
                return context.DeepCopy(original);
            var result = new global::TestProject.DemoData();
            context.RecordCopy(original, result);
            DeepCopy(original, result, context);
            return result;
        }

        public Copier_DemoData(global::Forkleans.Serialization.Serializers.ICodecProvider codecProvider)
        {
            _copier0 = ForkleansGeneratedCodeHelper.GetService<global::Forkleans.Serialization.Cloning.IDeepCopier<global::TestProject.CyclicClass>>(this, codecProvider);
            _copier1 = ForkleansGeneratedCodeHelper.GetService<global::Forkleans.Serialization.Cloning.IDeepCopier<global::TestProject.NestedClass1>>(this, codecProvider);
            _copier2 = ForkleansGeneratedCodeHelper.GetService<global::Forkleans.Serialization.Codecs.ListCopier<global::TestProject.NestedClass1>>(this, codecProvider);
        }

        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void DeepCopy(global::TestProject.DemoData input, global::TestProject.DemoData output, global::Forkleans.Serialization.Cloning.CopyContext context)
        {
            output.Nested1 = _copier1.DeepCopy(input.Nested1, context);
            output.NestedList = _copier2.DeepCopy(input.NestedList, context);
            output.Cyclic = _copier0.DeepCopy(input.Cyclic, context);
        }
    }

    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("ForkleansCodeGen", "9.0.0.0"), global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never), global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute]
    internal sealed class Activator_DemoData : global::Forkleans.Serialization.Activators.IActivator<global::TestProject.DemoData>
    {
        public global::TestProject.DemoData Create() => new global::TestProject.DemoData();
    }

    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("ForkleansCodeGen", "9.0.0.0"), global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never), global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute]
    internal sealed class Metadata_TestProject : global::Forkleans.Serialization.Configuration.TypeManifestProviderBase
    {
        protected override void ConfigureInner(global::Forkleans.Serialization.Configuration.TypeManifestOptions config)
        {
            config.Serializers.Add(typeof(ForkleansCodeGen.TestProject.Codec_DemoData));
            config.Copiers.Add(typeof(ForkleansCodeGen.TestProject.Copier_DemoData));
            config.Activators.Add(typeof(ForkleansCodeGen.TestProject.Activator_DemoData));
        }
    }
}
#pragma warning restore CS1591, RS0016, RS0041
