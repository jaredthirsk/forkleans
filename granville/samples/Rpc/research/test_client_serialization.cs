using System;
using System.Threading.Tasks;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Granville.Rpc;
using Shooter.Shared.RpcInterfaces;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Testing client-side serialization with isolated sessions...");
        
        var testPlayerId = "test-player-" + Guid.NewGuid();
        Console.WriteLine($"Test player ID: {testPlayerId}");
        
        // Create RPC client with debug logging
        var hostBuilder = Host.CreateDefaultBuilder()
            .UseOrleansRpcClient(rpcBuilder =>
            {
                rpcBuilder.ConnectTo("127.0.0.1", 12005);
                rpcBuilder.UseLiteNetLib();
            })
            .ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Debug);
                logging.AddConsole();
            })
            .ConfigureServices(services =>
            {
                services.AddSerializer(serializer =>
                {
                    serializer.AddAssembly(typeof(IGameGranule).Assembly);
                    serializer.AddAssembly(typeof(Granville.Rpc.Protocol.RpcMessage).Assembly);
                });
            });
            
        using var host = hostBuilder.Build();
        
        try
        {
            await host.StartAsync();
            
            // Wait a moment for connection
            await Task.Delay(1000);
            
            var client = host.Services.GetRequiredService<Orleans.IClusterClient>();
            var gameGrain = client.GetGrain<IGameGranule>("test");
            
            Console.WriteLine($"Calling ConnectPlayer with debug logging enabled...");
            var result = await gameGrain.ConnectPlayer(testPlayerId);
            Console.WriteLine($"Result: {result}");
            
            await host.StopAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
        
        Console.WriteLine("Test completed.");
    }
}