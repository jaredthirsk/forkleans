#pragma warning disable CS1591, RS0016, RS0041
[assembly: global::Forkleans.ApplicationPartAttribute("TestProject")]
[assembly: global::Forkleans.ApplicationPartAttribute("Forkleans.Core.Abstractions")]
[assembly: global::Forkleans.ApplicationPartAttribute("Forkleans.Serialization")]
[assembly: global::Forkleans.ApplicationPartAttribute("Forkleans.Core")]
[assembly: global::Forkleans.ApplicationPartAttribute("Forkleans.Runtime")]
[assembly: global::Forkleans.Serialization.Configuration.TypeManifestProviderAttribute(typeof(OrleansCodeGen.TestProject.Metadata_TestProject))]
namespace ForkleansCodeGen.TestProject
{
    using global::Forkleans.Serialization.Codecs;
    using global::Forkleans.Serialization.GeneratedCodeHelpers;

    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("OrleansCodeGen", "9.0.0.0"), global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never), global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute]
    public sealed class Codec_GenericData<T> : global::Forkleans.Serialization.Codecs.IFieldCodec<global::TestProject.GenericData<T>>, global::Forkleans.Serialization.Serializers.IBaseCodec<global::TestProject.GenericData<T>>
    {
        private readonly global::System.Type _codecFieldType = typeof(global::TestProject.GenericData<T>);
        private readonly global::System.Type _type0 = typeof(T);
        private readonly global::Forkleans.Serialization.Codecs.IFieldCodec<T> _codec0;
        public Codec_GenericData(global::Forkleans.Serialization.Serializers.ICodecProvider codecProvider)
        {
            _codec0 = OrleansGeneratedCodeHelper.GetService<global::Forkleans.Serialization.Codecs.IFieldCodec<T>>(this, codecProvider);
        }

        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void Serialize<TBufferWriter>(ref global::Forkleans.Serialization.Buffers.Writer<TBufferWriter> writer, global::TestProject.GenericData<T> instance)
            where TBufferWriter : global::System.Buffers.IBufferWriter<byte>
        {
            _codec0.WriteField(ref writer, 0U, _type0, instance.Value);
            global::Forkleans.Serialization.Codecs.StringCodec.WriteField(ref writer, 1U, instance.Description);
        }

        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void Deserialize<TReaderInput>(ref global::Forkleans.Serialization.Buffers.Reader<TReaderInput> reader, global::TestProject.GenericData<T> instance)
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
                    instance.Value = _codec0.ReadValue(ref reader, header);
                    reader.ReadFieldHeader(ref header);
                    if (header.IsEndBaseOrEndObject)
                        break;
                    id += header.FieldIdDelta;
                }

                if (id == 1U)
                {
                    instance.Description = global::Forkleans.Serialization.Codecs.StringCodec.ReadValue(ref reader, header);
                    reader.ReadFieldHeader(ref header);
                }

                reader.ConsumeEndBaseOrEndObject(ref header);
                break;
            }
        }

        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void WriteField<TBufferWriter>(ref global::Forkleans.Serialization.Buffers.Writer<TBufferWriter> writer, uint fieldIdDelta, global::System.Type expectedType, global::TestProject.GenericData<T> @value)
            where TBufferWriter : global::System.Buffers.IBufferWriter<byte>
        {
            if (@value is null || @value.GetType() == typeof(global::TestProject.GenericData<T>))
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
        public global::TestProject.GenericData<T> ReadValue<TReaderInput>(ref global::Forkleans.Serialization.Buffers.Reader<TReaderInput> reader, global::Forkleans.Serialization.WireProtocol.Field field)
        {
            if (field.IsReference)
                return ReferenceCodec.ReadReference<global::TestProject.GenericData<T>, TReaderInput>(ref reader, field);
            field.EnsureWireTypeTagDelimited();
            global::System.Type valueType = field.FieldType;
            if (valueType is null || valueType == _codecFieldType)
            {
                var result = new global::TestProject.GenericData<T>();
                ReferenceCodec.RecordObject(reader.Session, result);
                Deserialize(ref reader, result);
                return result;
            }

            return reader.DeserializeUnexpectedType<TReaderInput, global::TestProject.GenericData<T>>(ref field);
        }
    }

    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("OrleansCodeGen", "9.0.0.0"), global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never), global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute]
    public sealed class Copier_GenericData<T> : global::Forkleans.Serialization.Cloning.IDeepCopier<global::TestProject.GenericData<T>>, global::Forkleans.Serialization.Cloning.IBaseCopier<global::TestProject.GenericData<T>>
    {
        private readonly global::Forkleans.Serialization.Cloning.IDeepCopier<T> _copier0;
        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public global::TestProject.GenericData<T> DeepCopy(global::TestProject.GenericData<T> original, global::Forkleans.Serialization.Cloning.CopyContext context)
        {
            if (context.TryGetCopy(original, out global::TestProject.GenericData<T> existing))
                return existing;
            if (original.GetType() != typeof(global::TestProject.GenericData<T>))
                return context.DeepCopy(original);
            var result = new global::TestProject.GenericData<T>();
            context.RecordCopy(original, result);
            DeepCopy(original, result, context);
            return result;
        }

        public Copier_GenericData(global::Forkleans.Serialization.Serializers.ICodecProvider codecProvider)
        {
            _copier0 = OrleansGeneratedCodeHelper.GetService<global::Forkleans.Serialization.Cloning.IDeepCopier<T>>(this, codecProvider);
        }

        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void DeepCopy(global::TestProject.GenericData<T> input, global::TestProject.GenericData<T> output, global::Forkleans.Serialization.Cloning.CopyContext context)
        {
            output.Value = _copier0.DeepCopy(input.Value, context);
            output.Description = input.Description;
        }
    }

    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("OrleansCodeGen", "9.0.0.0"), global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never), global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute]
    internal sealed class Activator_GenericData<T> : global::Forkleans.Serialization.Activators.IActivator<global::TestProject.GenericData<T>>
    {
        public global::TestProject.GenericData<T> Create() => new global::TestProject.GenericData<T>();
    }

    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("OrleansCodeGen", "9.0.0.0"), global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never), global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute]
    public sealed class Codec_ConcreteUsage : global::Forkleans.Serialization.Codecs.IFieldCodec<global::TestProject.ConcreteUsage>, global::Forkleans.Serialization.Serializers.IBaseCodec<global::TestProject.ConcreteUsage>
    {
        private readonly global::System.Type _codecFieldType = typeof(global::TestProject.ConcreteUsage);
        private readonly global::System.Type _type0 = typeof(global::TestProject.GenericData<int>);
        private readonly OrleansCodeGen.TestProject.Codec_GenericData<int> _codec0;
        private readonly global::System.Type _type1 = typeof(global::TestProject.GenericData<string>);
        private readonly OrleansCodeGen.TestProject.Codec_GenericData<string> _codec1;
        public Codec_ConcreteUsage(global::Forkleans.Serialization.Serializers.ICodecProvider codecProvider)
        {
            _codec0 = OrleansGeneratedCodeHelper.GetService<ForkleansCodeGen.TestProject.Codec_GenericData<int>>(this, codecProvider);
            _codec1 = OrleansGeneratedCodeHelper.GetService<ForkleansCodeGen.TestProject.Codec_GenericData<string>>(this, codecProvider);
        }

        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void Serialize<TBufferWriter>(ref global::Forkleans.Serialization.Buffers.Writer<TBufferWriter> writer, global::TestProject.ConcreteUsage instance)
            where TBufferWriter : global::System.Buffers.IBufferWriter<byte>
        {
            _codec0.WriteField(ref writer, 0U, _type0, instance.IntData);
            _codec1.WriteField(ref writer, 1U, _type1, instance.StringData);
        }

        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void Deserialize<TReaderInput>(ref global::Forkleans.Serialization.Buffers.Reader<TReaderInput> reader, global::TestProject.ConcreteUsage instance)
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
                    instance.IntData = _codec0.ReadValue(ref reader, header);
                    reader.ReadFieldHeader(ref header);
                    if (header.IsEndBaseOrEndObject)
                        break;
                    id += header.FieldIdDelta;
                }

                if (id == 1U)
                {
                    instance.StringData = _codec1.ReadValue(ref reader, header);
                    reader.ReadFieldHeader(ref header);
                }

                reader.ConsumeEndBaseOrEndObject(ref header);
                break;
            }
        }

        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void WriteField<TBufferWriter>(ref global::Forkleans.Serialization.Buffers.Writer<TBufferWriter> writer, uint fieldIdDelta, global::System.Type expectedType, global::TestProject.ConcreteUsage @value)
            where TBufferWriter : global::System.Buffers.IBufferWriter<byte>
        {
            if (@value is null || @value.GetType() == typeof(global::TestProject.ConcreteUsage))
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
        public global::TestProject.ConcreteUsage ReadValue<TReaderInput>(ref global::Forkleans.Serialization.Buffers.Reader<TReaderInput> reader, global::Forkleans.Serialization.WireProtocol.Field field)
        {
            if (field.IsReference)
                return ReferenceCodec.ReadReference<global::TestProject.ConcreteUsage, TReaderInput>(ref reader, field);
            field.EnsureWireTypeTagDelimited();
            global::System.Type valueType = field.FieldType;
            if (valueType is null || valueType == _codecFieldType)
            {
                var result = new global::TestProject.ConcreteUsage();
                ReferenceCodec.RecordObject(reader.Session, result);
                Deserialize(ref reader, result);
                return result;
            }

            return reader.DeserializeUnexpectedType<TReaderInput, global::TestProject.ConcreteUsage>(ref field);
        }
    }

    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("OrleansCodeGen", "9.0.0.0"), global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never), global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute]
    public sealed class Copier_ConcreteUsage : global::Forkleans.Serialization.Cloning.IDeepCopier<global::TestProject.ConcreteUsage>, global::Forkleans.Serialization.Cloning.IBaseCopier<global::TestProject.ConcreteUsage>
    {
        private readonly OrleansCodeGen.TestProject.Copier_GenericData<int> _copier0;
        private readonly OrleansCodeGen.TestProject.Copier_GenericData<string> _copier1;
        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public global::TestProject.ConcreteUsage DeepCopy(global::TestProject.ConcreteUsage original, global::Forkleans.Serialization.Cloning.CopyContext context)
        {
            if (context.TryGetCopy(original, out global::TestProject.ConcreteUsage existing))
                return existing;
            if (original.GetType() != typeof(global::TestProject.ConcreteUsage))
                return context.DeepCopy(original);
            var result = new global::TestProject.ConcreteUsage();
            context.RecordCopy(original, result);
            DeepCopy(original, result, context);
            return result;
        }

        public Copier_ConcreteUsage(global::Forkleans.Serialization.Serializers.ICodecProvider codecProvider)
        {
            _copier0 = OrleansGeneratedCodeHelper.GetService<ForkleansCodeGen.TestProject.Copier_GenericData<int>>(this, codecProvider);
            _copier1 = OrleansGeneratedCodeHelper.GetService<ForkleansCodeGen.TestProject.Copier_GenericData<string>>(this, codecProvider);
        }

        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void DeepCopy(global::TestProject.ConcreteUsage input, global::TestProject.ConcreteUsage output, global::Forkleans.Serialization.Cloning.CopyContext context)
        {
            output.IntData = _copier0.DeepCopy(input.IntData, context);
            output.StringData = _copier1.DeepCopy(input.StringData, context);
        }
    }

    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("OrleansCodeGen", "9.0.0.0"), global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never), global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute]
    internal sealed class Activator_ConcreteUsage : global::Forkleans.Serialization.Activators.IActivator<global::TestProject.ConcreteUsage>
    {
        public global::TestProject.ConcreteUsage Create() => new global::TestProject.ConcreteUsage();
    }

    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("OrleansCodeGen", "9.0.0.0"), global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never), global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute]
    internal sealed class Metadata_TestProject : global::Forkleans.Serialization.Configuration.TypeManifestProviderBase
    {
        protected override void ConfigureInner(global::Forkleans.Serialization.Configuration.TypeManifestOptions config)
        {
            config.Serializers.Add(typeof(OrleansCodeGen.TestProject.Codec_GenericData<>));
            config.Serializers.Add(typeof(OrleansCodeGen.TestProject.Codec_ConcreteUsage));
            config.Copiers.Add(typeof(OrleansCodeGen.TestProject.Copier_GenericData<>));
            config.Copiers.Add(typeof(OrleansCodeGen.TestProject.Copier_ConcreteUsage));
            config.Activators.Add(typeof(OrleansCodeGen.TestProject.Activator_GenericData<>));
            config.Activators.Add(typeof(OrleansCodeGen.TestProject.Activator_ConcreteUsage));
        }
    }
}
#pragma warning restore CS1591, RS0016, RS0041
