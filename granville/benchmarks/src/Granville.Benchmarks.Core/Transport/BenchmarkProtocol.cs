using System;

namespace Granville.Benchmarks.Core.Transport
{
    /// <summary>
    /// Simple protocol for benchmark request/response packets
    /// </summary>
    public static class BenchmarkProtocol
    {
        /// <summary>
        /// Magic bytes to identify benchmark packets
        /// </summary>
        public static readonly byte[] MagicBytes = { 0x42, 0x4E, 0x43, 0x48 }; // "BNCH"
        
        /// <summary>
        /// Create a benchmark request packet
        /// </summary>
        public static byte[] CreateRequest(int requestId, byte[] payload)
        {
            var packet = new byte[MagicBytes.Length + sizeof(int) + sizeof(int) + payload.Length];
            var offset = 0;
            
            // Magic bytes
            Buffer.BlockCopy(MagicBytes, 0, packet, offset, MagicBytes.Length);
            offset += MagicBytes.Length;
            
            // Request ID
            BitConverter.GetBytes(requestId).CopyTo(packet, offset);
            offset += sizeof(int);
            
            // Payload length
            BitConverter.GetBytes(payload.Length).CopyTo(packet, offset);
            offset += sizeof(int);
            
            // Payload
            Buffer.BlockCopy(payload, 0, packet, offset, payload.Length);
            
            return packet;
        }
        
        /// <summary>
        /// Create a benchmark response packet
        /// </summary>
        public static byte[] CreateResponse(int requestId, byte[] originalPayload)
        {
            var packet = new byte[MagicBytes.Length + sizeof(int) + sizeof(int) + originalPayload.Length];
            var offset = 0;
            
            // Magic bytes
            Buffer.BlockCopy(MagicBytes, 0, packet, offset, MagicBytes.Length);
            offset += MagicBytes.Length;
            
            // Request ID (same as request)
            BitConverter.GetBytes(requestId).CopyTo(packet, offset);
            offset += sizeof(int);
            
            // Payload length
            BitConverter.GetBytes(originalPayload.Length).CopyTo(packet, offset);
            offset += sizeof(int);
            
            // Original payload (echo back)
            Buffer.BlockCopy(originalPayload, 0, packet, offset, originalPayload.Length);
            
            return packet;
        }
        
        /// <summary>
        /// Parse a benchmark packet
        /// </summary>
        public static BenchmarkPacket ParsePacket(byte[] data)
        {
            if (data.Length < MagicBytes.Length + sizeof(int) + sizeof(int))
                throw new ArgumentException("Packet too short");
            
            var offset = 0;
            
            // Check magic bytes
            for (int i = 0; i < MagicBytes.Length; i++)
            {
                if (data[offset + i] != MagicBytes[i])
                    throw new ArgumentException("Invalid magic bytes");
            }
            offset += MagicBytes.Length;
            
            // Read request ID
            var requestId = BitConverter.ToInt32(data, offset);
            offset += sizeof(int);
            
            // Read payload length
            var payloadLength = BitConverter.ToInt32(data, offset);
            offset += sizeof(int);
            
            // Read payload
            var payload = new byte[payloadLength];
            Buffer.BlockCopy(data, offset, payload, 0, payloadLength);
            
            return new BenchmarkPacket
            {
                RequestId = requestId,
                Payload = payload
            };
        }
    }
    
    /// <summary>
    /// Represents a parsed benchmark packet
    /// </summary>
    public class BenchmarkPacket
    {
        public int RequestId { get; set; }
        public byte[] Payload { get; set; } = Array.Empty<byte>();
    }
}