using System;
using System.Buffers;
using System.Threading.Tasks;
using Orleans.Serialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace Granville.Rpc.Serialization
{
    /// <summary>
    /// Codec for VoidTaskResult - the internal type returned by Task.Result for non-generic Tasks.
    /// This is a safety net to prevent serialization failures until we migrate to proper response handling.
    /// </summary>
    public sealed class VoidTaskResultCodec : IFieldCodec<object>
    {
        private static readonly Type VoidTaskResultType = typeof(Task).GetProperty("Result")?.PropertyType;

        /// <inheritdoc/>
        public bool CanHandle(Type fieldType)
        {
            return VoidTaskResultType != null && fieldType == VoidTaskResultType;
        }

        /// <inheritdoc/>
        void IFieldCodec<object>.WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, Type expectedType, object value)
        {
            // VoidTaskResult is a singleton with no data - just write a null marker
            ReferenceCodec.MarkValueField(writer.Session);
            writer.WriteFieldHeader(fieldIdDelta, expectedType, VoidTaskResultType, WireType.Reference);
            writer.WriteVarUInt32(0); // Write null reference
        }

        /// <inheritdoc/>
        object IFieldCodec<object>.ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            // Skip the field since VoidTaskResult has no data
            reader.SkipField(field);
            
            // Return null since VoidTaskResult should never be used as a real value
            return null;
        }
    }
}