using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Forkleans;
using Forkleans.Rpc;
using Forkleans.Rpc.TestGrainInterfaces;
using Forkleans.Rpc.Transport.LiteNetLib;

class SimpleTest
{
    static async Task Main()
    {
        Console.WriteLine("Starting simple test...");
        
        try
        {
            // Just test the client startup
            var host = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                {
                    logging.AddConsole();
                    logging.SetMinimumLevel(LogLevel.Information);
                })
                .UseOrleansRpcClient(rpcClient =>
                {
                    rpcClient.ConnectTo("127.0.0.1", 11111);
                    rpcClient.UseLiteNetLib();
                })
                .Build();

            Console.WriteLine("Starting host...");
            await host.StartAsync();
            Console.WriteLine("Host started!");
            
            var client = host.Services.GetRequiredService<IClusterClient>();
            Console.WriteLine($"Got IClusterClient: {client.GetType().Name}");
            
            // Try to get a grain
            Console.WriteLine("Getting grain...");
            var grain = client.GetGrain<IHelloGrain>(1);
            Console.WriteLine($"Got grain: {grain != null}");
            
            await host.StopAsync();
            Console.WriteLine("Test complete!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.GetType().Name} - {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}