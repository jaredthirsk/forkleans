using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using System.Diagnostics;

namespace Granville.Benchmarks.Micro.Benchmarks
{
    [SimpleJob(RuntimeMoniker.Net80)]
    [MemoryDiagnoser]
    [ThreadingDiagnoser]
    public class RpcLatencyBenchmark
    {
        private readonly byte[] _smallPayload = new byte[100];
        private readonly byte[] _mediumPayload = new byte[1024];
        private readonly byte[] _largePayload = new byte[10240];
        
        // TODO: Add actual Granville RPC implementation once we have standalone RPC server/client setup
        // For now, we'll benchmark the basic serialization and transport simulation
        
        [GlobalSetup]
        public void Setup()
        {
            // Initialize payloads with test data
            Random.Shared.NextBytes(_smallPayload);
            Random.Shared.NextBytes(_mediumPayload);
            Random.Shared.NextBytes(_largePayload);
        }
        
        [GlobalCleanup]
        public void Cleanup()
        {
            // Cleanup resources
        }
        
        [Benchmark(Baseline = true)]
        public async Task<byte[]> SmallPayload_Simulation()
        {
            // Simulate network round-trip with minimal overhead
            await Task.Yield();
            return SimulateNetworkTransfer(_smallPayload);
        }
        
        [Benchmark]
        public async Task<byte[]> MediumPayload_Simulation()
        {
            await Task.Yield();
            return SimulateNetworkTransfer(_mediumPayload);
        }
        
        [Benchmark]
        public async Task<byte[]> LargePayload_Simulation()
        {
            await Task.Yield();
            return SimulateNetworkTransfer(_largePayload);
        }
        
        [Benchmark]
        public byte[] SmallPayload_SerializationOnly()
        {
            return SimulateSerializationRoundtrip(_smallPayload);
        }
        
        [Benchmark]
        public byte[] MediumPayload_SerializationOnly()
        {
            return SimulateSerializationRoundtrip(_mediumPayload);
        }
        
        [Benchmark]
        public byte[] LargePayload_SerializationOnly()
        {
            return SimulateSerializationRoundtrip(_largePayload);
        }
        
        private byte[] SimulateNetworkTransfer(byte[] data)
        {
            // Simulate basic network transfer overhead
            var buffer = new byte[data.Length];
            Buffer.BlockCopy(data, 0, buffer, 0, data.Length);
            return buffer;
        }
        
        private byte[] SimulateSerializationRoundtrip(byte[] data)
        {
            // Simulate serialization/deserialization
            var buffer = new byte[data.Length];
            Buffer.BlockCopy(data, 0, buffer, 0, data.Length);
            return buffer;
        }
    }
}