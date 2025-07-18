using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Configs;
using System.Buffers;
using Granville.Rpc.Protocol;
using Orleans.Serialization;
using Orleans.Runtime;
using Orleans;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Serialization.Buffers;
using System.Text.Json;

namespace Granville.Benchmarks.Micro.Benchmarks
{
    /// <summary>
    /// Benchmarks comparing JSON vs Orleans binary serialization for RPC messages
    /// </summary>
    [SimpleJob(RuntimeMoniker.Net80)]
    [MemoryDiagnoser]
    [Config(typeof(AntiVirusFriendlyConfig))]
    public class RpcSerializationBenchmark
    {
        private class AntiVirusFriendlyConfig : ManualConfig
        {
            public AntiVirusFriendlyConfig()
            {
                WithOptions(ConfigOptions.DisableOptimizationsValidator);
            }
        }

        private Serializer _orleansSerializer = null!;
        private JsonSerializerOptions _jsonOptions = null!;
        private RpcRequest _smallRequest = null!;
        private RpcRequest _mediumRequest = null!;
        private RpcRequest _largeRequest = null!;
        private byte[] _smallPayload = null!;
        private byte[] _mediumPayload = null!;
        private byte[] _largePayload = null!;

        [GlobalSetup]
        public void Setup()
        {
            // Set up Orleans serializer
            var services = new ServiceCollection();
            services.AddSerializer();
            services.AddLogging();
            var serviceProvider = services.BuildServiceProvider();
            _orleansSerializer = serviceProvider.GetRequiredService<Serializer>();

            // Set up JSON options
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };

            // Create test payloads
            _smallPayload = new byte[100];
            _mediumPayload = new byte[1024];
            _largePayload = new byte[10240];
            Random.Shared.NextBytes(_smallPayload);
            Random.Shared.NextBytes(_mediumPayload);
            Random.Shared.NextBytes(_largePayload);

            // Create test requests
            _smallRequest = new RpcRequest
            {
                MessageId = Guid.NewGuid(),
                GrainId = GrainId.Create("test", "123"),
                InterfaceType = GrainInterfaceType.Create("test-interface"),
                MethodId = 1,
                Arguments = _smallPayload,
                TimeoutMs = 30000,
                ReturnTypeName = "string"
            };

            _mediumRequest = new RpcRequest
            {
                MessageId = Guid.NewGuid(),
                GrainId = GrainId.Create("test", "456"),
                InterfaceType = GrainInterfaceType.Create("test-interface"),
                MethodId = 2,
                Arguments = _mediumPayload,
                TimeoutMs = 30000,
                ReturnTypeName = "string"
            };

            _largeRequest = new RpcRequest
            {
                MessageId = Guid.NewGuid(),
                GrainId = GrainId.Create("test", "789"),
                InterfaceType = GrainInterfaceType.Create("test-interface"),
                MethodId = 3,
                Arguments = _largePayload,
                TimeoutMs = 30000,
                ReturnTypeName = "string"
            };
        }

        [Benchmark(Baseline = true)]
        public byte[] Json_Serialize_Small()
        {
            return JsonSerializer.SerializeToUtf8Bytes(_smallRequest, _jsonOptions);
        }

        [Benchmark]
        public byte[] Orleans_Serialize_Small()
        {
            var writer = new ArrayBufferWriter<byte>();
            _orleansSerializer.Serialize(_smallRequest, writer);
            return writer.WrittenMemory.ToArray();
        }

        [Benchmark]
        public byte[] Json_Serialize_Medium()
        {
            return JsonSerializer.SerializeToUtf8Bytes(_mediumRequest, _jsonOptions);
        }

        [Benchmark]
        public byte[] Orleans_Serialize_Medium()
        {
            var writer = new ArrayBufferWriter<byte>();
            _orleansSerializer.Serialize(_mediumRequest, writer);
            return writer.WrittenMemory.ToArray();
        }

        [Benchmark]
        public byte[] Json_Serialize_Large()
        {
            return JsonSerializer.SerializeToUtf8Bytes(_largeRequest, _jsonOptions);
        }

        [Benchmark]
        public byte[] Orleans_Serialize_Large()
        {
            var writer = new ArrayBufferWriter<byte>();
            _orleansSerializer.Serialize(_largeRequest, writer);
            return writer.WrittenMemory.ToArray();
        }
    }
}