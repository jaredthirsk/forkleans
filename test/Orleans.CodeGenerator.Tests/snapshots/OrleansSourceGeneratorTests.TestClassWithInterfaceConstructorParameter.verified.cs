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
    public sealed class Codec_InterfaceCtorParam : global::Forkleans.Serialization.Codecs.IFieldCodec<global::TestProject.InterfaceCtorParam>, global::Forkleans.Serialization.Serializers.IBaseCodec<global::TestProject.InterfaceCtorParam>
    {
        private readonly global::System.Type _codecFieldType = typeof(global::TestProject.InterfaceCtorParam);
        private readonly global::Forkleans.Serialization.Activators.IActivator<global::TestProject.InterfaceCtorParam> _activator;
        private readonly global::System.Type _type0 = typeof(global::TestProject.IMyInterface);
        private readonly global::Forkleans.Serialization.Codecs.IFieldCodec<global::TestProject.IMyInterface> _codec0;
        private static readonly global::System.Func<global::TestProject.InterfaceCtorParam, global::TestProject.IMyInterface> getField0 = (global::System.Func<global::TestProject.InterfaceCtorParam, global::TestProject.IMyInterface>)global::Forkleans.Serialization.Utilities.FieldAccessor.GetGetter(typeof(global::TestProject.InterfaceCtorParam), "_iface");
        private static readonly global::System.Action<global::TestProject.InterfaceCtorParam, global::TestProject.IMyInterface> setField0 = (global::System.Action<global::TestProject.InterfaceCtorParam, global::TestProject.IMyInterface>)global::Forkleans.Serialization.Utilities.FieldAccessor.GetReferenceSetter(typeof(global::TestProject.InterfaceCtorParam), "_iface");
        public Codec_InterfaceCtorParam(global::Forkleans.Serialization.Activators.IActivator<global::TestProject.InterfaceCtorParam> _activator, global::Forkleans.Serialization.Serializers.ICodecProvider codecProvider)
        {
            this._activator = OrleansGeneratedCodeHelper.UnwrapService(this, _activator);
            _codec0 = OrleansGeneratedCodeHelper.GetService<global::Forkleans.Serialization.Codecs.IFieldCodec<global::TestProject.IMyInterface>>(this, codecProvider);
        }

        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void Serialize<TBufferWriter>(ref global::Forkleans.Serialization.Buffers.Writer<TBufferWriter> writer, global::TestProject.InterfaceCtorParam instance)
            where TBufferWriter : global::System.Buffers.IBufferWriter<byte>
        {
            _codec0.WriteField(ref writer, 0U, _type0, getField0(instance));
        }

        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void Deserialize<TReaderInput>(ref global::Forkleans.Serialization.Buffers.Reader<TReaderInput> reader, global::TestProject.InterfaceCtorParam instance)
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
                    setField0(instance, _codec0.ReadValue(ref reader, header));
                    reader.ReadFieldHeader(ref header);
                }

                reader.ConsumeEndBaseOrEndObject(ref header);
                break;
            }
        }

        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void WriteField<TBufferWriter>(ref global::Forkleans.Serialization.Buffers.Writer<TBufferWriter> writer, uint fieldIdDelta, global::System.Type expectedType, global::TestProject.InterfaceCtorParam @value)
            where TBufferWriter : global::System.Buffers.IBufferWriter<byte>
        {
            if (@value is null || @value.GetType() == typeof(global::TestProject.InterfaceCtorParam))
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
        public global::TestProject.InterfaceCtorParam ReadValue<TReaderInput>(ref global::Forkleans.Serialization.Buffers.Reader<TReaderInput> reader, global::Forkleans.Serialization.WireProtocol.Field field)
        {
            if (field.IsReference)
                return ReferenceCodec.ReadReference<global::TestProject.InterfaceCtorParam, TReaderInput>(ref reader, field);
            field.EnsureWireTypeTagDelimited();
            global::System.Type valueType = field.FieldType;
            if (valueType is null || valueType == _codecFieldType)
            {
                var result = _activator.Create();
                ReferenceCodec.RecordObject(reader.Session, result);
                Deserialize(ref reader, result);
                return result;
            }

            return reader.DeserializeUnexpectedType<TReaderInput, global::TestProject.InterfaceCtorParam>(ref field);
        }
    }

    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("OrleansCodeGen", "9.0.0.0"), global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never), global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute]
    public sealed class Copier_InterfaceCtorParam : global::Forkleans.Serialization.Cloning.IDeepCopier<global::TestProject.InterfaceCtorParam>, global::Forkleans.Serialization.Cloning.IBaseCopier<global::TestProject.InterfaceCtorParam>
    {
        private readonly global::Forkleans.Serialization.Activators.IActivator<global::TestProject.InterfaceCtorParam> _activator;
        private readonly global::Forkleans.Serialization.Cloning.IDeepCopier<global::TestProject.IMyInterface> _copier0;
        private static readonly global::System.Func<global::TestProject.InterfaceCtorParam, global::TestProject.IMyInterface> getField0 = (global::System.Func<global::TestProject.InterfaceCtorParam, global::TestProject.IMyInterface>)global::Forkleans.Serialization.Utilities.FieldAccessor.GetGetter(typeof(global::TestProject.InterfaceCtorParam), "_iface");
        private static readonly global::System.Action<global::TestProject.InterfaceCtorParam, global::TestProject.IMyInterface> setField0 = (global::System.Action<global::TestProject.InterfaceCtorParam, global::TestProject.IMyInterface>)global::Forkleans.Serialization.Utilities.FieldAccessor.GetReferenceSetter(typeof(global::TestProject.InterfaceCtorParam), "_iface");
        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public global::TestProject.InterfaceCtorParam DeepCopy(global::TestProject.InterfaceCtorParam original, global::Forkleans.Serialization.Cloning.CopyContext context)
        {
            if (context.TryGetCopy(original, out global::TestProject.InterfaceCtorParam existing))
                return existing;
            if (original.GetType() != typeof(global::TestProject.InterfaceCtorParam))
                return context.DeepCopy(original);
            var result = _activator.Create();
            context.RecordCopy(original, result);
            DeepCopy(original, result, context);
            return result;
        }

        public Copier_InterfaceCtorParam(global::Forkleans.Serialization.Activators.IActivator<global::TestProject.InterfaceCtorParam> _activator, global::Forkleans.Serialization.Serializers.ICodecProvider codecProvider)
        {
            this._activator = OrleansGeneratedCodeHelper.UnwrapService(this, _activator);
            _copier0 = OrleansGeneratedCodeHelper.GetService<global::Forkleans.Serialization.Cloning.IDeepCopier<global::TestProject.IMyInterface>>(this, codecProvider);
        }

        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void DeepCopy(global::TestProject.InterfaceCtorParam input, global::TestProject.InterfaceCtorParam output, global::Forkleans.Serialization.Cloning.CopyContext context)
        {
            setField0(output, _copier0.DeepCopy(getField0(input), context));
        }
    }

    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("OrleansCodeGen", "9.0.0.0"), global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never), global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute]
    internal sealed class Metadata_TestProject : global::Forkleans.Serialization.Configuration.TypeManifestProviderBase
    {
        protected override void ConfigureInner(global::Forkleans.Serialization.Configuration.TypeManifestOptions config)
        {
            config.Serializers.Add(typeof(OrleansCodeGen.TestProject.Codec_InterfaceCtorParam));
            config.Copiers.Add(typeof(OrleansCodeGen.TestProject.Copier_InterfaceCtorParam));
        }
    }
}
#pragma warning restore CS1591, RS0016, RS0041
