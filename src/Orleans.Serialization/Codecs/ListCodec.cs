using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Forkleans.Serialization.Buffers;
using Forkleans.Serialization.Cloning;
using Forkleans.Serialization.GeneratedCodeHelpers;
using Forkleans.Serialization.Serializers;
using Forkleans.Serialization.WireProtocol;

namespace Forkleans.Serialization.Codecs
{
    /// <summary>
    /// Serializer for <see cref="List{T}"/>.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    [RegisterSerializer]
    public sealed class ListCodec<T> : IFieldCodec<List<T>>, IBaseCodec<List<T>>
    {
        private readonly Type CodecElementType = typeof(T);

        private readonly IFieldCodec<T> _fieldCodec;

        /// <summary>
        /// Initializes a new instance of the <see cref="ListCodec{T}"/> class.
        /// </summary>
        /// <param name="fieldCodec">The field codec.</param>
        public ListCodec(IFieldCodec<T> fieldCodec)
        {
            _fieldCodec = ForkleansGeneratedCodeHelper.UnwrapService(this, fieldCodec);
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, Type expectedType, List<T> value) where TBufferWriter : IBufferWriter<byte>
        {
            if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
            {
                return;
            }

            writer.WriteFieldHeader(fieldIdDelta, expectedType, value.GetType(), WireType.TagDelimited);

            Serialize(ref writer, value);

            writer.WriteEndObject();
        }

        /// <inheritdoc/>
        public List<T> ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            if (field.WireType == WireType.Reference)
            {
                return ReferenceCodec.ReadReference<List<T>, TInput>(ref reader, field);
            }

            field.EnsureWireTypeTagDelimited();

            var placeholderReferenceId = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
            List<T> result = null;
            uint fieldId = 0;
            while (true)
            {
                var header = reader.ReadFieldHeader();
                if (header.IsEndBaseOrEndObject)
                {
                    break;
                }

                fieldId += header.FieldIdDelta;
                switch (fieldId)
                {
                    case 0:
                        var length = (int)UInt32Codec.ReadValue(ref reader, header);
                        if (length > 10240 && length > reader.Length)
                        {
                            ThrowInvalidSizeException(length);
                        }

                        result = new(length);
                        ReferenceCodec.RecordObject(reader.Session, result, placeholderReferenceId);
                        break;
                    case 1:
                        if (result is null)
                        {
                            ListCodec<T>.ThrowLengthFieldMissing();
                        }

                        result.Add(_fieldCodec.ReadValue(ref reader, header));
                        break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }

            if (result is null)
            {
                result = new();
                ReferenceCodec.RecordObject(reader.Session, result, placeholderReferenceId);
            }

            return result;
        }

        private static void ThrowInvalidSizeException(int length) => throw new IndexOutOfRangeException(
            $"Declared length of {typeof(List<T>)}, {length}, is greater than total length of input.");

        private static void ThrowLengthFieldMissing() => throw new RequiredFieldMissingException("Serialized array is missing its length field.");

        public void Serialize<TBufferWriter>(ref Writer<TBufferWriter> writer, List<T> value) where TBufferWriter : IBufferWriter<byte>
        {
            if (value.Count > 0)
            {
                UInt32Codec.WriteField(ref writer, 0, (uint)value.Count);
                uint innerFieldIdDelta = 1;
                foreach (var element in value)
                {
                    _fieldCodec.WriteField(ref writer, innerFieldIdDelta, CodecElementType, element);
                    innerFieldIdDelta = 0;
                }
            }
        }

        public void Deserialize<TInput>(ref Reader<TInput> reader, List<T> value)
        {
            // If the value has some values added by the constructor, clear them.
            // If those values are in the serialized payload, they will be added below.
            value.Clear();

            uint fieldId = 0;
            while (true)
            {
                var header = reader.ReadFieldHeader();
                if (header.IsEndBaseOrEndObject)
                {
                    break;
                }

                fieldId += header.FieldIdDelta;
                switch (fieldId)
                {
                    case 0:
                        var length = (int)UInt32Codec.ReadValue(ref reader, header);
                        if (length > 10240 && length > reader.Length)
                        {
                            ThrowInvalidSizeException(length);
                        }

#if NET6_0_OR_GREATER
                        value.EnsureCapacity(length);
#endif
                        break;
                    case 1:
                        value.Add(_fieldCodec.ReadValue(ref reader, header));
                        break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Copier for <see cref="List{T}"/>.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    [RegisterCopier]
    public sealed class ListCopier<T> : IDeepCopier<List<T>>, IBaseCopier<List<T>>
    {
        private readonly IDeepCopier<T> _copier;

        /// <summary>
        /// Initializes a new instance of the <see cref="ListCopier{T}"/> class.
        /// </summary>
        /// <param name="valueCopier">The value copier.</param>
        public ListCopier(IDeepCopier<T> valueCopier)
        {
            _copier = valueCopier;
        }

        /// <inheritdoc/>
        public List<T> DeepCopy(List<T> input, CopyContext context)
        {
            if (context.TryGetCopy<List<T>>(input, out var result))
            {
                return result;
            }

            if (input.GetType() != typeof(List<T>))
            {
                return context.DeepCopy(input);
            }

            result = new List<T>(input.Count);
            context.RecordCopy(input, result);
            foreach (var item in input)
            {
                result.Add(_copier.DeepCopy(item, context));
            }

            return result;
        }

        /// <inheritdoc/>
        public void DeepCopy(List<T> input, List<T> output, CopyContext context)
        {
            output.Clear();

#if NET6_0_OR_GREATER
            output.EnsureCapacity(input.Count);
#endif
            foreach (var item in input)
            {
                output.Add(_copier.DeepCopy(item, context));
            }
        }
    }
}