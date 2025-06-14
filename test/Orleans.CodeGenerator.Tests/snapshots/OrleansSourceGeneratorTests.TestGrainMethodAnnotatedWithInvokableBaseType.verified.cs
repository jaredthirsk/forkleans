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
    [global::Forkleans.CompoundTypeAliasAttribute("inv", typeof(global::Forkleans.Runtime.GrainReference), typeof(global::TestProject.IHelloGrain), "5336307F")]
    public sealed class Invokable_IHelloGrain_GrainReference_5336307F : global::Forkleans.Runtime.TaskRequest<string>
    {
        public string arg0;
        global::TestProject.IHelloGrain _target;
        private static readonly global::System.Reflection.MethodInfo MethodBackingField = ForkleansGeneratedCodeHelper.GetMethodInfoOrDefault(typeof(global::TestProject.IHelloGrain), "SayHello", null, new[] { typeof(string) });
        public Invokable_IHelloGrain_GrainReference_5336307F() : base()
        {
            SetLoggingOptions("Hello");
        }

        public override int GetArgumentCount() => 1;
        public override string GetMethodName() => "SayHello";
        public override string GetInterfaceName() => "TestProject.IHelloGrain";
        public override string GetActivityName() => "IHelloGrain/SayHello";
        public override global::System.Type GetInterfaceType() => typeof(global::TestProject.IHelloGrain);
        public override global::System.Reflection.MethodInfo GetMethod() => MethodBackingField;
        public override void SetTarget(global::Forkleans.Serialization.Invocation.ITargetHolder holder) => _target = holder.GetTarget<global::TestProject.IHelloGrain>();
        public override object GetTarget() => _target;
        public override void Dispose()
        {
            arg0 = default;
            _target = default;
        }

        public override object GetArgument(int index)
        {
            switch (index)
            {
                case 0:
                    return arg0;
                default:
                    return ForkleansGeneratedCodeHelper.InvokableThrowArgumentOutOfRange(index, 0);
            }
        }

        public override void SetArgument(int index, object value)
        {
            switch (index)
            {
                case 0:
                    arg0 = (string)value;
                    return;
                default:
                    ForkleansGeneratedCodeHelper.InvokableThrowArgumentOutOfRange(index, 0);
                    return;
            }
        }

        protected override global::System.Threading.Tasks.Task<string> InvokeInner() => _target.SayHello(arg0);
    }

    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("ForkleansCodeGen", "9.0.0.0"), global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never), global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute]
    internal sealed class Proxy_IHelloGrain : global::Forkleans.Runtime.GrainReference, global::TestProject.IHelloGrain
    {
        public Proxy_IHelloGrain(global::Forkleans.Runtime.GrainReferenceShared arg0, global::Forkleans.Runtime.IdSpan arg1) : base(arg0, arg1)
        {
        }

        global::System.Threading.Tasks.Task<string> global::TestProject.IHelloGrain.SayHello(string arg0)
        {
            var request = new ForkleansCodeGen.TestProject.Invokable_IHelloGrain_GrainReference_5336307F();
            request.arg0 = arg0;
            return base.InvokeAsync<string>(request).AsTask();
        }
    }

    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("ForkleansCodeGen", "9.0.0.0"), global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never), global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute]
    public sealed class Codec_Invokable_IHelloGrain_GrainReference_5336307F : global::Forkleans.Serialization.Codecs.IFieldCodec<ForkleansCodeGen.TestProject.Invokable_IHelloGrain_GrainReference_5336307F>
    {
        private readonly global::System.Type _codecFieldType = typeof(ForkleansCodeGen.TestProject.Invokable_IHelloGrain_GrainReference_5336307F);
        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void Serialize<TBufferWriter>(ref global::Forkleans.Serialization.Buffers.Writer<TBufferWriter> writer, ForkleansCodeGen.TestProject.Invokable_IHelloGrain_GrainReference_5336307F instance)
            where TBufferWriter : global::System.Buffers.IBufferWriter<byte>
        {
            global::Forkleans.Serialization.Codecs.StringCodec.WriteField(ref writer, 0U, instance.arg0);
        }

        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void Deserialize<TReaderInput>(ref global::Forkleans.Serialization.Buffers.Reader<TReaderInput> reader, ForkleansCodeGen.TestProject.Invokable_IHelloGrain_GrainReference_5336307F instance)
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
                    instance.arg0 = global::Forkleans.Serialization.Codecs.StringCodec.ReadValue(ref reader, header);
                    reader.ReadFieldHeader(ref header);
                }

                reader.ConsumeEndBaseOrEndObject(ref header);
                break;
            }
        }

        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void WriteField<TBufferWriter>(ref global::Forkleans.Serialization.Buffers.Writer<TBufferWriter> writer, uint fieldIdDelta, global::System.Type expectedType, ForkleansCodeGen.TestProject.Invokable_IHelloGrain_GrainReference_5336307F @value)
            where TBufferWriter : global::System.Buffers.IBufferWriter<byte>
        {
            if (@value is null)
            {
                ReferenceCodec.WriteNullReference(ref writer, fieldIdDelta);
                return;
            }

            ReferenceCodec.MarkValueField(writer.Session);
            writer.WriteStartObject(fieldIdDelta, expectedType, _codecFieldType);
            Serialize(ref writer, @value);
            writer.WriteEndObject();
        }

        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public ForkleansCodeGen.TestProject.Invokable_IHelloGrain_GrainReference_5336307F ReadValue<TReaderInput>(ref global::Forkleans.Serialization.Buffers.Reader<TReaderInput> reader, global::Forkleans.Serialization.WireProtocol.Field field)
        {
            if (field.IsReference)
                return ReferenceCodec.ReadReference<ForkleansCodeGen.TestProject.Invokable_IHelloGrain_GrainReference_5336307F, TReaderInput>(ref reader, field);
            field.EnsureWireTypeTagDelimited();
            var result = new ForkleansCodeGen.TestProject.Invokable_IHelloGrain_GrainReference_5336307F();
            ReferenceCodec.MarkValueField(reader.Session);
            Deserialize(ref reader, result);
            return result;
        }
    }

    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("ForkleansCodeGen", "9.0.0.0"), global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never), global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute]
    public sealed class Copier_Invokable_IHelloGrain_GrainReference_5336307F : global::Forkleans.Serialization.Cloning.IDeepCopier<ForkleansCodeGen.TestProject.Invokable_IHelloGrain_GrainReference_5336307F>
    {
        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public ForkleansCodeGen.TestProject.Invokable_IHelloGrain_GrainReference_5336307F DeepCopy(ForkleansCodeGen.TestProject.Invokable_IHelloGrain_GrainReference_5336307F original, global::Forkleans.Serialization.Cloning.CopyContext context)
        {
            if (original is null)
                return null;
            var result = new ForkleansCodeGen.TestProject.Invokable_IHelloGrain_GrainReference_5336307F();
            result.arg0 = original.arg0;
            return result;
        }
    }

    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("ForkleansCodeGen", "9.0.0.0"), global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never), global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute]
    public sealed class Codec_HelloGrain : global::Forkleans.Serialization.Codecs.IFieldCodec<global::TestProject.HelloGrain>, global::Forkleans.Serialization.Serializers.IBaseCodec<global::TestProject.HelloGrain>
    {
        private readonly global::System.Type _codecFieldType = typeof(global::TestProject.HelloGrain);
        private readonly global::Forkleans.Serialization.Serializers.IBaseCodec<global::Forkleans.Grain> _baseTypeSerializer;
        public Codec_HelloGrain(global::Forkleans.Serialization.Serializers.IBaseCodec<global::Forkleans.Grain> _baseTypeSerializer)
        {
            this._baseTypeSerializer = ForkleansGeneratedCodeHelper.UnwrapService(this, _baseTypeSerializer);
        }

        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void Serialize<TBufferWriter>(ref global::Forkleans.Serialization.Buffers.Writer<TBufferWriter> writer, global::TestProject.HelloGrain instance)
            where TBufferWriter : global::System.Buffers.IBufferWriter<byte>
        {
            _baseTypeSerializer.Serialize(ref writer, instance);
            writer.WriteEndBase();
        }

        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void Deserialize<TReaderInput>(ref global::Forkleans.Serialization.Buffers.Reader<TReaderInput> reader, global::TestProject.HelloGrain instance)
        {
            _baseTypeSerializer.Deserialize(ref reader, instance);
            reader.ConsumeEndBaseOrEndObject();
        }

        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void WriteField<TBufferWriter>(ref global::Forkleans.Serialization.Buffers.Writer<TBufferWriter> writer, uint fieldIdDelta, global::System.Type expectedType, global::TestProject.HelloGrain @value)
            where TBufferWriter : global::System.Buffers.IBufferWriter<byte>
        {
            if (@value is null || @value.GetType() == typeof(global::TestProject.HelloGrain))
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
        public global::TestProject.HelloGrain ReadValue<TReaderInput>(ref global::Forkleans.Serialization.Buffers.Reader<TReaderInput> reader, global::Forkleans.Serialization.WireProtocol.Field field)
        {
            if (field.IsReference)
                return ReferenceCodec.ReadReference<global::TestProject.HelloGrain, TReaderInput>(ref reader, field);
            field.EnsureWireTypeTagDelimited();
            global::System.Type valueType = field.FieldType;
            if (valueType is null || valueType == _codecFieldType)
            {
                var result = new global::TestProject.HelloGrain();
                ReferenceCodec.RecordObject(reader.Session, result);
                Deserialize(ref reader, result);
                return result;
            }

            return reader.DeserializeUnexpectedType<TReaderInput, global::TestProject.HelloGrain>(ref field);
        }
    }

    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("ForkleansCodeGen", "9.0.0.0"), global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never), global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute]
    public sealed class Copier_HelloGrain : global::Forkleans.Serialization.Cloning.IDeepCopier<global::TestProject.HelloGrain>, global::Forkleans.Serialization.Cloning.IBaseCopier<global::TestProject.HelloGrain>
    {
        private readonly global::Forkleans.Serialization.Cloning.IBaseCopier<global::Forkleans.Grain> _baseTypeCopier;
        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public global::TestProject.HelloGrain DeepCopy(global::TestProject.HelloGrain original, global::Forkleans.Serialization.Cloning.CopyContext context)
        {
            if (context.TryGetCopy(original, out global::TestProject.HelloGrain existing))
                return existing;
            if (original.GetType() != typeof(global::TestProject.HelloGrain))
                return context.DeepCopy(original);
            var result = new global::TestProject.HelloGrain();
            context.RecordCopy(original, result);
            DeepCopy(original, result, context);
            return result;
        }

        public Copier_HelloGrain(global::Forkleans.Serialization.Cloning.IBaseCopier<global::Forkleans.Grain> _baseTypeCopier)
        {
            this._baseTypeCopier = ForkleansGeneratedCodeHelper.UnwrapService(this, _baseTypeCopier);
        }

        [global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void DeepCopy(global::TestProject.HelloGrain input, global::TestProject.HelloGrain output, global::Forkleans.Serialization.Cloning.CopyContext context)
        {
            _baseTypeCopier.DeepCopy(input, output, context);
        }
    }

    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("ForkleansCodeGen", "9.0.0.0"), global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never), global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute]
    internal sealed class Activator_HelloGrain : global::Forkleans.Serialization.Activators.IActivator<global::TestProject.HelloGrain>
    {
        public global::TestProject.HelloGrain Create() => new global::TestProject.HelloGrain();
    }

    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("ForkleansCodeGen", "9.0.0.0"), global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never), global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute]
    internal sealed class Metadata_TestProject : global::Forkleans.Serialization.Configuration.TypeManifestProviderBase
    {
        protected override void ConfigureInner(global::Forkleans.Serialization.Configuration.TypeManifestOptions config)
        {
            config.Serializers.Add(typeof(ForkleansCodeGen.TestProject.Codec_Invokable_IHelloGrain_GrainReference_5336307F));
            config.Serializers.Add(typeof(ForkleansCodeGen.TestProject.Codec_HelloGrain));
            config.Copiers.Add(typeof(ForkleansCodeGen.TestProject.Copier_Invokable_IHelloGrain_GrainReference_5336307F));
            config.Copiers.Add(typeof(ForkleansCodeGen.TestProject.Copier_HelloGrain));
            config.InterfaceProxies.Add(typeof(ForkleansCodeGen.TestProject.Proxy_IHelloGrain));
            config.Interfaces.Add(typeof(global::TestProject.IHelloGrain));
            config.InterfaceImplementations.Add(typeof(global::TestProject.HelloGrain));
            config.Activators.Add(typeof(ForkleansCodeGen.TestProject.Activator_HelloGrain));
            var n1 = config.CompoundTypeAliases.Add("inv");
            var n2 = n1.Add(typeof(global::Forkleans.Runtime.GrainReference));
            var n3 = n2.Add(typeof(global::TestProject.IHelloGrain));
            n3.Add("5336307F", typeof(ForkleansCodeGen.TestProject.Invokable_IHelloGrain_GrainReference_5336307F));
        }
    }
}
#pragma warning restore CS1591, RS0016, RS0041
