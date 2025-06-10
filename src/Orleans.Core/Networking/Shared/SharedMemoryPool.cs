using System.Buffers;

namespace Forkleans.Networking.Shared
{
    internal sealed class SharedMemoryPool
    {
        public MemoryPool<byte> Pool { get; } = KestrelMemoryPool.Create();
    }
}
