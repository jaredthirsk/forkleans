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
    public sealed class Codec_DemoDataWithFields : global::Forkleans.Serialization.Codecs.IFieldCodec<global::TestProject.DemoDataWithFields>, global::Forkleans.Serialization.Serializers.IBaseCodec<global::TestProject.DemoDataWithFields>
    {
        private readonly global::System.Type _codecFieldType = typeof(global::TestProject.DemoDataWithFields);
        private readonly global::Forkleans.Serialization.Activators.IActivator<global::TestProject.DemoDataWithFields> _activator;
        private static readonly global::System.Func<global::TestProject.DemoDataWithFields, int> getField0 = (global::System.Func<global::TestProject.DemoDataWithFields, int>)global::Forkleans.Serialization.Utilities.FieldAccessor.GetGetter(typeof(global::TestProject.DemoDataWithFields), "_intValue");
        private static readonly global::System.Action<global::TestProject.DemoDataWithFields, int> setField0 = (global::System.Action<global::TestProject.DemoDataWithFields, int>)global::Forkleans.Serialization.Utilities.FieldAccessor.GetReferenceSetter(typeof(global::TestProject.DemoDataWithFields), "_intValue");
        private static readonly global::System.Func<global::TestProject.DemoDataWithFields, string> getField1 = (global::System.Func<global::TestProject.DemoDataWithFields, string>)global::Forkleans.Serialization.Utilities.FieldAccessor.GetGetter(typeof(global::TestProject.DemoDataWithFields), "_stringValue");
        private static readonly global::System.Action<global::TestProject.DemoDataWithFields, string> setField1 = (global::System.Action<global::TestProject.DemoDataWithFields, string>)global::Forkleans.Serialization.Utilities.FieldAccessor.GetReferenceSetter(typeof(global::TestProject.DemoDataWithFields), "_stringValue");
        public Codec_DemoDataWithFields(global::Forkleans.Serialization.Activators.IActivator<global::TestProject.DemoDataWithFields> _activator)
        {
            this._activator = OrleansGeneratedCodeHelper.UnwrapService(this, _activator);
        }

        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void Serialize<TBufferWriter>(ref global::Forkleans.Serialization.Buffers.Writer<TBufferWriter> writer, global::TestProject.DemoDataWithFields instance)
            where TBufferWriter : global::System.Buffers.IBufferWriter<byte>
        {
            global::Forkleans.Serialization.Codecs.Int32Codec.WriteField(ref writer, 0U, getField0(instance));
            global::Forkleans.Serialization.Codecs.StringCodec.WriteField(ref writer, 1U, getField1(instance));
        }

        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void Deserialize<TReaderInput>(ref global::Forkleans.Serialization.Buffers.Reader<TReaderInput> reader, global::TestProject.DemoDataWithFields instance)
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
                    setField0(instance, global::Forkleans.Serialization.Codecs.Int32Codec.ReadValue(ref reader, header));
                    reader.ReadFieldHeader(ref header);
                    if (header.IsEndBaseOrEndObject)
                        break;
                    id += header.FieldIdDelta;
                }

                if (id == 1U)
                {
                    setField1(instance, global::Forkleans.Serialization.Codecs.StringCodec.ReadValue(ref reader, header));
                    reader.ReadFieldHeader(ref header);
                }

                reader.ConsumeEndBaseOrEndObject(ref header);
                break;
            }
        }

        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void WriteField<TBufferWriter>(ref global::Forkleans.Serialization.Buffers.Writer<TBufferWriter> writer, uint fieldIdDelta, global::System.Type expectedType, global::TestProject.DemoDataWithFields @value)
            where TBufferWriter : global::System.Buffers.IBufferWriter<byte>
        {
            if (@value is null || @value.GetType() == typeof(global::TestProject.DemoDataWithFields))
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
        public global::TestProject.DemoDataWithFields ReadValue<TReaderInput>(ref global::Forkleans.Serialization.Buffers.Reader<TReaderInput> reader, global::Forkleans.Serialization.WireProtocol.Field field)
        {
            if (field.IsReference)
                return ReferenceCodec.ReadReference<global::TestProject.DemoDataWithFields, TReaderInput>(ref reader, field);
            field.EnsureWireTypeTagDelimited();
            global::System.Type valueType = field.FieldType;
            if (valueType is null || valueType == _codecFieldType)
            {
                var result = _activator.Create();
                ReferenceCodec.RecordObject(reader.Session, result);
                Deserialize(ref reader, result);
                return result;
            }

            return reader.DeserializeUnexpectedType<TReaderInput, global::TestProject.DemoDataWithFields>(ref field);
        }
    }

    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("OrleansCodeGen", "9.0.0.0"), global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never), global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute]
    public sealed class Copier_DemoDataWithFields : global::Forkleans.Serialization.Cloning.IDeepCopier<global::TestProject.DemoDataWithFields>, global::Forkleans.Serialization.Cloning.IBaseCopier<global::TestProject.DemoDataWithFields>
    {
        private readonly global::Forkleans.Serialization.Activators.IActivator<global::TestProject.DemoDataWithFields> _activator;
        private static readonly global::System.Func<global::TestProject.DemoDataWithFields, int> getField0 = (global::System.Func<global::TestProject.DemoDataWithFields, int>)global::Forkleans.Serialization.Utilities.FieldAccessor.GetGetter(typeof(global::TestProject.DemoDataWithFields), "_intValue");
        private static readonly global::System.Action<global::TestProject.DemoDataWithFields, int> setField0 = (global::System.Action<global::TestProject.DemoDataWithFields, int>)global::Forkleans.Serialization.Utilities.FieldAccessor.GetReferenceSetter(typeof(global::TestProject.DemoDataWithFields), "_intValue");
        private static readonly global::System.Func<global::TestProject.DemoDataWithFields, string> getField1 = (global::System.Func<global::TestProject.DemoDataWithFields, string>)global::Forkleans.Serialization.Utilities.FieldAccessor.GetGetter(typeof(global::TestProject.DemoDataWithFields), "_stringValue");
        private static readonly global::System.Action<global::TestProject.DemoDataWithFields, string> setField1 = (global::System.Action<global::TestProject.DemoDataWithFields, string>)global::Forkleans.Serialization.Utilities.FieldAccessor.GetReferenceSetter(typeof(global::TestProject.DemoDataWithFields), "_stringValue");
        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public global::TestProject.DemoDataWithFields DeepCopy(global::TestProject.DemoDataWithFields original, global::Forkleans.Serialization.Cloning.CopyContext context)
        {
            if (context.TryGetCopy(original, out global::TestProject.DemoDataWithFields existing))
                return existing;
            if (original.GetType() != typeof(global::TestProject.DemoDataWithFields))
                return context.DeepCopy(original);
            var result = _activator.Create();
            context.RecordCopy(original, result);
            DeepCopy(original, result, context);
            return result;
        }

        public Copier_DemoDataWithFields(global::Forkleans.Serialization.Activators.IActivator<global::TestProject.DemoDataWithFields> _activator)
        {
            this._activator = OrleansGeneratedCodeHelper.UnwrapService(this, _activator);
        }

        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void DeepCopy(global::TestProject.DemoDataWithFields input, global::TestProject.DemoDataWithFields output, global::Forkleans.Serialization.Cloning.CopyContext context)
        {
            setField0(output, getField0(input));
            setField1(output, getField1(input));
        }
    }

    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("OrleansCodeGen", "9.0.0.0"), global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never), global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute]
    internal sealed class Activator_DemoDataWithFields : global::Forkleans.Serialization.Activators.IActivator<global::TestProject.DemoDataWithFields>
    {
        private readonly int _arg0;
        private readonly string _arg1;
        public Activator_DemoDataWithFields(int arg0, string arg1)
        {
            _arg0 = OrleansGeneratedCodeHelper.UnwrapService(this, arg0);
            _arg1 = OrleansGeneratedCodeHelper.UnwrapService(this, arg1);
        }

        public global::TestProject.DemoDataWithFields Create() => new global::TestProject.DemoDataWithFields(_arg0, _arg1);
    }

    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("OrleansCodeGen", "9.0.0.0"), global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never), global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute]
    internal sealed class Metadata_TestProject : global::Forkleans.Serialization.Configuration.TypeManifestProviderBase
    {
        protected override void ConfigureInner(global::Forkleans.Serialization.Configuration.TypeManifestOptions config)
        {
            config.Serializers.Add(typeof(OrleansCodeGen.TestProject.Codec_DemoDataWithFields));
            config.Copiers.Add(typeof(OrleansCodeGen.TestProject.Copier_DemoDataWithFields));
            config.Activators.Add(typeof(OrleansCodeGen.TestProject.Activator_DemoDataWithFields));
        }
    }
}
#pragma warning restore CS1591, RS0016, RS0041
