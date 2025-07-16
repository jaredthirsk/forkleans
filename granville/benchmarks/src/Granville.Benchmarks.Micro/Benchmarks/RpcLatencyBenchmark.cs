using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using System.Diagnostics;

namespace Granville.Benchmarks.Micro.Benchmarks
{
    [SimpleJob(RuntimeMoniker.Net80)]
    [MemoryDiagnoser]
    [ThreadingDiagnoser]
    [MinIterationTime(100)] // Ensure minimum 100ms per iteration to avoid warnings
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
        // [OperationsPerInvoke(2000)] // Run 2,000 operations per invocation for async benchmarks
        public async Task<byte[]> SmallPayload_Simulation()
        {
            byte[] result = _smallPayload;
            for (int i = 0; i < 2000; i++)
            {
                // Simulate network round-trip with minimal overhead
                await Task.Yield();
                result = SimulateNetworkTransfer(_smallPayload);
            }
            return result;
        }
        
        [Benchmark]
        // [OperationsPerInvoke(1000)] // Run 1,000 operations per invocation for async benchmarks
        public async Task<byte[]> MediumPayload_Simulation()
        {
            byte[] result = _mediumPayload;
            for (int i = 0; i < 1000; i++)
            {
                await Task.Yield();
                result = SimulateNetworkTransfer(_mediumPayload);
            }
            return result;
        }
        
        [Benchmark]
        // [OperationsPerInvoke(1000)] // Run 1,000 operations per invocation for async benchmarks
        public async Task<byte[]> LargePayload_Simulation()
        {
            byte[] result = _largePayload;
            for (int i = 0; i < 1000; i++)
            {
                await Task.Yield();
                result = SimulateNetworkTransfer(_largePayload);
            }
            return result;
        }
        
        [Benchmark]
        // [OperationsPerInvoke(10000)] // Run 10,000 operations per invocation to increase iteration time
        public byte[] SmallPayload_SerializationOnly()
        {
            byte[] result = _smallPayload;
            for (int i = 0; i < 10000; i++)
            {
                result = SimulateSerializationRoundtrip(_smallPayload);
            }
            return result;
        }
        
        [Benchmark]
        // [OperationsPerInvoke(5000)] // Run 5,000 operations per invocation for medium payload
        public byte[] MediumPayload_SerializationOnly()
        {
            byte[] result = _mediumPayload;
            for (int i = 0; i < 5000; i++)
            {
                result = SimulateSerializationRoundtrip(_mediumPayload);
            }
            return result;
        }
        
        [Benchmark]
        // [OperationsPerInvoke(1000)] // Run 1,000 operations per invocation for large payload
        public byte[] LargePayload_SerializationOnly()
        {
            byte[] result = _largePayload;
            for (int i = 0; i < 1000; i++)
            {
                result = SimulateSerializationRoundtrip(_largePayload);
            }
            return result;
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