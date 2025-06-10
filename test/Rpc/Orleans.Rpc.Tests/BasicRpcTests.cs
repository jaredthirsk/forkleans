using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using Forkleans.Rpc.TestGrainInterfaces;
using Forkleans.Rpc.Transport.LiteNetLib;

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
                })
                .Build();

            await _serverHost.StartAsync();
            _output.WriteLine($"RPC Server started on port {serverPort}");

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
                })
                .Build();

            await _clientHost.StartAsync();
            _output.WriteLine("RPC Client connected");

            _client = _clientHost.Services.GetRequiredService<IClusterClient>();
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
            // Arrange
            var grain = _client.GetGrain<IHelloGrain>(1);
            
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
            var grain = _client.GetGrain<IHelloGrain>(2);
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
            var grain = _client.GetGrain<IHelloGrain>(3);
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
                var grain = _client.GetGrain<IHelloGrain>(i);
                var result = await grain.SayHello($"User{i}");
                
                Assert.NotNull(result);
                Assert.Contains($"User{i}", result);
                Assert.Contains(i.ToString(), result); // Should contain grain ID
                
                _output.WriteLine($"Grain {i}: {result}");
            }
        }
    }
}
