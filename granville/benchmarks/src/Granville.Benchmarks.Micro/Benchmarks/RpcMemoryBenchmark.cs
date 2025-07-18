using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using System.Diagnostics;
using System.Text.Json;
using System.Buffers;
using Granville.Rpc.Protocol;
using Orleans.Serialization;
using Orleans.Runtime;
using Orleans;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using BenchmarkDotNet.Loggers;

namespace Granville.Benchmarks.Micro.Benchmarks
{
    /// <summary>
    /// Focused benchmarks for memory allocation patterns in Granville RPC
    /// </summary>
    [SimpleJob(RuntimeMoniker.Net80)]
    [MemoryDiagnoser]
    [ThreadingDiagnoser]
    [MinIterationTime(100)]
    [Config(typeof(CustomConfig))]
    public class RpcMemoryBenchmark
    {
        private readonly byte[] _smallPayload = new byte[100];
        private readonly byte[] _mediumPayload = new byte[1024];
        private readonly byte[] _largePayload = new byte[10240];
        
        private RpcMessageSerializer _serializer = null!;
        private RpcRequest _smallRequest = null!;
        private RpcRequest _mediumRequest = null!;
        private RpcRequest _largeRequest = null!;
        
        public class CustomConfig : ManualConfig
        {
            public CustomConfig()
            {
                AddLogger(BenchmarkDotNet.Loggers.ConsoleLogger.Default);
                WithOption(ConfigOptions.DisableOptimizationsValidator, true);
            }
        }

        [GlobalSetup]
        public void Setup()
        {
            // Initialize payloads
            Random.Shared.NextBytes(_smallPayload);
            Random.Shared.NextBytes(_mediumPayload);
            Random.Shared.NextBytes(_largePayload);
            
            // Set up real Orleans serializer
            var services = new ServiceCollection();
            services.AddSerializer();
            services.AddLogging();
            var serviceProvider = services.BuildServiceProvider();
            var orleansSerializer = serviceProvider.GetRequiredService<Serializer>();
            var logger = serviceProvider.GetRequiredService<ILogger<RpcMessageSerializer>>();
            
            _serializer = new RpcMessageSerializer(orleansSerializer, logger);
            
            // Create test requests
            _smallRequest = CreateRequest(_smallPayload);
            _mediumRequest = CreateRequest(_mediumPayload);
            _largeRequest = CreateRequest(_largePayload);
        }

        private RpcRequest CreateRequest(byte[] payload)
        {
            return new RpcRequest
            {
                MessageId = Guid.NewGuid(),
                GrainId = GrainId.Create("test", "grain1"),
                InterfaceType = GrainInterfaceType.Create("test-interface"),
                MethodId = 1,
                Arguments = payload,
                TimeoutMs = 30000,
                ReturnTypeName = "string"
            };
        }

        [Benchmark(Baseline = true)]
        public void MessageCreation_Small()
        {
            for (int i = 0; i < 1000; i++)
            {
                var msg = CreateRequest(_smallPayload);
                // Simulate usage to prevent optimization
                _ = msg.MessageId;
            }
        }

        [Benchmark]
        public void MessageCreation_Medium()
        {
            for (int i = 0; i < 1000; i++)
            {
                var msg = CreateRequest(_mediumPayload);
                _ = msg.MessageId;
            }
        }

        [Benchmark]
        public void MessageCreation_Large()
        {
            for (int i = 0; i < 1000; i++)
            {
                var msg = CreateRequest(_largePayload);
                _ = msg.MessageId;
            }
        }

        [Benchmark]
        public void ArrayPool_RentReturn_Small()
        {
            for (int i = 0; i < 1000; i++)
            {
                var buffer = ArrayPool<byte>.Shared.Rent(100);
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        [Benchmark]
        public void ArrayPool_RentReturn_Medium()
        {
            for (int i = 0; i < 1000; i++)
            {
                var buffer = ArrayPool<byte>.Shared.Rent(1024);
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        [Benchmark]
        public void ArrayPool_RentReturn_Large()
        {
            for (int i = 0; i < 1000; i++)
            {
                var buffer = ArrayPool<byte>.Shared.Rent(10240);
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        [Benchmark]
        public void ArrayAllocation_Small()
        {
            for (int i = 0; i < 1000; i++)
            {
                var buffer = new byte[100];
                _ = buffer.Length;
            }
        }

        [Benchmark]
        public void ArrayAllocation_Medium()
        {
            for (int i = 0; i < 1000; i++)
            {
                var buffer = new byte[1024];
                _ = buffer.Length;
            }
        }

        [Benchmark]
        public void ArrayAllocation_Large()
        {
            for (int i = 0; i < 1000; i++)
            {
                var buffer = new byte[10240];
                _ = buffer.Length;
            }
        }

        [Benchmark]
        public byte[] Serialization_Small()
        {
            var result = _serializer.SerializeMessage(_smallRequest);
            return result;
        }

        [Benchmark]
        public byte[] Serialization_Medium()
        {
            var result = _serializer.SerializeMessage(_mediumRequest);
            return result;
        }

        [Benchmark]
        public byte[] Serialization_Large()
        {
            var result = _serializer.SerializeMessage(_largeRequest);
            return result;
        }

        [Benchmark]
        public RpcMessage Deserialization_Small()
        {
            var serialized = _serializer.SerializeMessage(_smallRequest);
            return _serializer.DeserializeMessage(new ReadOnlyMemory<byte>(serialized));
        }

        [Benchmark]
        public RpcMessage Deserialization_Medium()
        {
            var serialized = _serializer.SerializeMessage(_mediumRequest);
            return _serializer.DeserializeMessage(new ReadOnlyMemory<byte>(serialized));
        }

        [Benchmark]
        public RpcMessage Deserialization_Large()
        {
            var serialized = _serializer.SerializeMessage(_largeRequest);
            return _serializer.DeserializeMessage(new ReadOnlyMemory<byte>(serialized));
        }

        [Benchmark]
        public RpcResponse Response_Small()
        {
            return new RpcResponse
            {
                RequestId = Guid.NewGuid(),
                Success = true,
                Payload = _smallPayload
            };
        }

        [Benchmark]
        public RpcResponse Response_Medium()
        {
            return new RpcResponse
            {
                RequestId = Guid.NewGuid(),
                Success = true,
                Payload = _mediumPayload
            };
        }

        [Benchmark]
        public RpcResponse Response_Large()
        {
            return new RpcResponse
            {
                RequestId = Guid.NewGuid(),
                Success = true,
                Payload = _largePayload
            };
        }

        [Benchmark]
        public ArrayBufferWriter<byte> BufferWriter_Small()
        {
            var writer = new ArrayBufferWriter<byte>();
            writer.Write(_smallPayload);
            return writer;
        }

        [Benchmark]
        public ArrayBufferWriter<byte> BufferWriter_Medium()
        {
            var writer = new ArrayBufferWriter<byte>();
            writer.Write(_mediumPayload);
            return writer;
        }

        [Benchmark]
        public ArrayBufferWriter<byte> BufferWriter_Large()
        {
            var writer = new ArrayBufferWriter<byte>();
            writer.Write(_largePayload);
            return writer;
        }

        [Benchmark]
        public byte[] JsonSerialization_Small()
        {
            return JsonSerializer.SerializeToUtf8Bytes(_smallRequest);
        }

        [Benchmark]
        public byte[] JsonSerialization_Medium()
        {
            return JsonSerializer.SerializeToUtf8Bytes(_mediumRequest);
        }

        [Benchmark]
        public byte[] JsonSerialization_Large()
        {
            return JsonSerializer.SerializeToUtf8Bytes(_largeRequest);
        }
    }
}