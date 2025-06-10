using System.Buffers;

namespace Forkleans.Serialization.TestKit
{
    public interface IOutputBuffer
    {
        ReadOnlySequence<byte> GetReadOnlySequence(int maxSegmentSize);
    }
}