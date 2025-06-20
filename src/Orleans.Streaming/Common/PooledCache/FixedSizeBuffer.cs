
using System;

namespace Forkleans.Providers.Streams.Common
{
    /// <summary>
    /// Manages a contiguous block of memory.
    /// Calls purge action with itself as the purge request when it's signaled to purge.
    /// </summary>
    public class FixedSizeBuffer : PooledResource<FixedSizeBuffer>
    {
        private readonly byte[] buffer;
        private int count;

        /// <summary>
        /// Buffer size in bytes.
        /// </summary>
        public readonly int SizeInByte;

        /// <summary>
        /// Unique identifier of this buffer.
        /// </summary>
        public object Id => buffer;

        /// <summary>
        /// Manages access to a fixed size byte buffer.
        /// </summary>
        /// <param name="blockSizeInByte">The block size, in bytes.</param>
        public FixedSizeBuffer(int blockSizeInByte)
        {
            if (blockSizeInByte < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(blockSizeInByte), "blockSize must be positive value.");
            }
            count = 0;
            this.SizeInByte = blockSizeInByte;
            buffer = new byte[this.SizeInByte];
        }

        /// <summary>
        /// Try to get a segment with a buffer of the specified size from this block.
        /// Fail if there is not enough space available
        /// </summary>
        /// <param name="size">The size.</param>
        /// <param name="value">The segment.</param>
        /// <returns><see langword="true"/> if the segment was retrieved; otherwise <see langword="false"/>.</returns>
        public bool TryGetSegment(int size, out ArraySegment<byte> value)
        {
            value = default;
            if (size > this.SizeInByte - count)
            {
                return false;
            }
            value = new ArraySegment<byte>(buffer, count, size);
            count += size;
            return true;
        }

        /// <inheritdoc />
        public override void OnResetState()
        {
            count = 0;
        }
    }
}
