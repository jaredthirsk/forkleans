using System.Buffers;

namespace Forkleans.Networking.Shared
{
    internal static class KestrelMemoryPool
    {
        public static MemoryPool<byte> Create()
        {
            return CreateSlabMemoryPool();
        }

        public static MemoryPool<byte> CreateSlabMemoryPool()
        {
            return new SlabMemoryPool();
        }

        public static readonly int MinimumSegmentSize = 4096;
    }
}
