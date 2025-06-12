using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using Forkleans.Rpc.TestGrainInterfaces;
using Forkleans.Rpc.TestGrains;
using Forkleans.Rpc.Transport.LiteNetLib;
using Forkleans.Rpc.Hosting;
using Forkleans.Configuration;
using Forkleans.Configuration.Internal;
using Forkleans.Serialization.Configuration;
using Forkleans.Serialization;
using Forkleans.Metadata;
using Forkleans.Runtime;

namespace Forkleans.Rpc.Tests
{
    /// <summary>
    /// Basic RPC functionality tests.
    /// </summary>
    public class BasicRpcTests : IAsyncLifetime
    {
        private readonly ITestOutputHelper _output;
        private IHost _serverHost;
        private IHost _clientHost;
        private IClusterClient _client;

        public BasicRpcTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public async Task InitializeAsync()
        {
            var serverPort = 11111;

            // Ensure grain assemblies are loaded
            _ = typeof(HelloGrain).Assembly;
            _ = typeof(IHelloGrain).Assembly;

            // Build and start the RPC server
            _serverHost = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.SetMinimumLevel(LogLevel.Debug);
                })
                .UseOrleansRpc(rpcServer =>
                {
                    rpcServer.ConfigureEndpoint(serverPort);
                    rpcServer.UseLiteNetLib();
                    
                    // Add grain assemblies
                    rpcServer.AddAssemblyContaining<HelloGrain>()
                             .AddAssemblyContaining<IHelloGrain>();
                    
                    // Add logging to debug grain type discovery
                    rpcServer.Services.PostConfigure<GrainTypeOptions>(options =>
                    {
                        _output.WriteLine($"GrainTypeOptions.Classes count: {options.Classes.Count}");
                        foreach (var grainClass in options.Classes)
                        {
                            _output.WriteLine($"  - Grain class: {grainClass.FullName}");
                        }
                        _output.WriteLine($"GrainTypeOptions.Interfaces count: {options.Interfaces.Count}");
                        foreach (var grainInterface in options.Interfaces)
                        {
                            _output.WriteLine($"  - Grain interface: {grainInterface.FullName}");
                        }
                    });
                })
                .Build();

            await _serverHost.StartAsync();
            _output.WriteLine($"RPC Server started on port {serverPort}");
            
            // Debug manifest provider
            var manifestProvider = _serverHost.Services.GetRequiredService<IClusterManifestProvider>();
            _output.WriteLine($"Manifest version: {manifestProvider.Current.Version}");
            _output.WriteLine($"Manifest grain count: {manifestProvider.Current.AllGrainManifests.SelectMany(m => m.Grains).Count()}");
            foreach (var manifest in manifestProvider.Current.AllGrainManifests)
            {
                foreach (var grain in manifest.Grains)
                {
                    _output.WriteLine($"  - Grain type: {grain.Key}, Properties: {string.Join(", ", grain.Value.Properties.Select(p => $"{p.Key}={p.Value}"))}");
                }
                _output.WriteLine($"Manifest interface count: {manifest.Interfaces.Count}");
                foreach (var iface in manifest.Interfaces)
                {
                    _output.WriteLine($"  - Interface type: {iface.Key}, Properties: {string.Join(", ", iface.Value.Properties.Select(p => $"{p.Key}={p.Value}"))}");
                }
            }

            // Give server time to fully start
            await Task.Delay(1000);

            // Build and start the RPC client
            _clientHost = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.SetMinimumLevel(LogLevel.Debug);
                })
                .UseOrleansRpcClient(rpcClient =>
                {
                    rpcClient.ConnectTo("127.0.0.1", serverPort);
                    rpcClient.UseLiteNetLib();
                    
                    // Add grain interface assembly for client
                    rpcClient.Services.AddSerializer(serializer =>
                    {
                        serializer.AddAssembly(typeof(IHelloGrain).Assembly);
                    });
                })
                .Build();

            await _clientHost.StartAsync();
            _output.WriteLine("RPC Client connected");

            _client = _clientHost.Services.GetRequiredService<IClusterClient>();
            
            // Give time for handshake to complete
            await Task.Delay(500);
        }

        public async Task DisposeAsync()
        {
            if (_clientHost != null)
            {
                await _clientHost.StopAsync();
                _clientHost.Dispose();
            }

            if (_serverHost != null)
            {
                await _serverHost.StopAsync();
                _serverHost.Dispose();
            }
        }

        [Fact]
        public Task Can_Connect_Client_To_Server()
        {
            // If we got here without exceptions, connection was successful
            Assert.NotNull(_client);
            _output.WriteLine("Client successfully connected to server");
            return Task.CompletedTask;
        }

        [Fact]
        public async Task Can_Call_SayHello_Method()
        {
            // Debug client-side interface resolution
            var grainInterfaceTypeResolver = _clientHost.Services.GetRequiredService<GrainInterfaceTypeResolver>();
            var interfaceType = grainInterfaceTypeResolver.GetGrainInterfaceType(typeof(IHelloGrain));
            _output.WriteLine($"Client looking for interface type: {interfaceType}");
            
            // Debug manifest on client
            var clientManifestProvider = _clientHost.Services.GetRequiredService<IClusterManifestProvider>();
            _output.WriteLine($"Client manifest version: {clientManifestProvider.Current.Version}");
            _output.WriteLine($"Client manifest grain count: {clientManifestProvider.Current.AllGrainManifests.SelectMany(m => m.Grains).Count()}");
            foreach (var manifest in clientManifestProvider.Current.AllGrainManifests)
            {
                foreach (var grainEntry in manifest.Grains)
                {
                    _output.WriteLine($"  - Client grain type: {grainEntry.Key}, Properties: {string.Join(", ", grainEntry.Value.Properties.Select(p => $"{p.Key}={p.Value}"))}");
                }
            }
            
            // Arrange
            var grain = _client.GetGrain<IHelloGrain>("1");
            
            // Act
            var result = await grain.SayHello("World");
            
            // Assert
            Assert.NotNull(result);
            Assert.Contains("Hello, World!", result);
            _output.WriteLine($"Received response: {result}");
        }

        [Fact]
        public async Task Can_Call_Echo_Method()
        {
            // Arrange
            var grain = _client.GetGrain<IHelloGrain>("2");
            var message = "This is a test message";
            
            // Act
            var result = await grain.Echo(message);
            
            // Assert
            Assert.Equal(message, result);
            _output.WriteLine($"Echo successful: {result}");
        }

        [Fact]
        public async Task Can_Call_Method_With_Complex_Types()
        {
            // Arrange
            var grain = _client.GetGrain<IHelloGrain>("3");
            var request = new HelloRequest
            {
                Name = "Alice",
                Age = 30,
                Location = "Seattle"
            };
            
            // Act
            var response = await grain.GetDetailedGreeting(request);
            
            // Assert
            Assert.NotNull(response);
            Assert.Contains("Alice", response.Greeting);
            Assert.Contains("30", response.Greeting);
            Assert.Contains("Seattle", response.Greeting);
            Assert.NotNull(response.ServerTime);
            Assert.True(response.ProcessId > 0);
            
            _output.WriteLine($"Received greeting: {response.Greeting}");
            _output.WriteLine($"Server time: {response.ServerTime}");
            _output.WriteLine($"Process ID: {response.ProcessId}");
        }

        [Fact]
        public async Task Multiple_Grains_Can_Be_Called()
        {
            // Call multiple grain instances
            for (int i = 0; i < 5; i++)
            {
                var grain = _client.GetGrain<IHelloGrain>($"{i}");
                var result = await grain.SayHello($"User{i}");
                
                Assert.NotNull(result);
                Assert.Contains($"User{i}", result);
                Assert.Contains(i.ToString(), result); // Should contain grain ID
                
                _output.WriteLine($"Grain {i}: {result}");
            }
        }
    }
}
