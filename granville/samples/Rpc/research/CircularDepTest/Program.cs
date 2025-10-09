using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Granville.Rpc.Hosting;
using Granville.Rpc.Transport.LiteNetLib;
using Shooter.Shared;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Testing circular dependency fix...");
        
        var builder = Host.CreateDefaultBuilder(args);
        
        builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Warning);
        });
        
        builder.ConfigureServices(services =>
        {
            services.AddRpcClient(client =>
            {
                client.AddServerAddress(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 19001));
            });
            services.AddLiteNetLibRpcTransport();
        });

        var host = builder.Build();
        
        try
        {
            Console.WriteLine("Starting host...");
            await host.StartAsync();
            
            // Try to get the RPC client and call GetGrain
            Console.WriteLine("Getting RPC client from DI...");
            var rpcClient = host.Services.GetRequiredService<Granville.Rpc.IRpcClient>();
            Console.WriteLine("✓ RPC Client obtained successfully!");
            
            // Try to get a grain reference (this would previously timeout)
            Console.WriteLine("Getting grain reference...");
            var gameGrain = rpcClient.GetGrain<IGameGranule>(Guid.NewGuid());
            Console.WriteLine("✓ Grain reference obtained successfully!");
            Console.WriteLine($"  Grain type: {gameGrain.GetType().Name}");
            
            await host.StopAsync();
            Console.WriteLine("\n✓ Test passed! Circular dependency is fixed.");
            Environment.Exit(0);
        }
        catch (TimeoutException tex)
        {
            Console.WriteLine($"\n✗ Test failed with timeout: {tex.Message}");
            Console.WriteLine("This indicates the circular dependency is NOT fixed.");
            Environment.Exit(1);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n✗ Test failed: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"Stack trace:\n{ex.StackTrace}");
            Environment.Exit(1);
        }
    }
}