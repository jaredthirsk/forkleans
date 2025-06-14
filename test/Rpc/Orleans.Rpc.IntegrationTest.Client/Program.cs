using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Forkleans;
using Forkleans.Rpc;
using Forkleans.Rpc.TestGrainInterfaces;
using Forkleans.Rpc.Transport.LiteNetLib;

namespace Forkleans.Rpc.IntegrationTest.Client
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var host = args.Length > 0 ? args[0] : "127.0.0.1";
            var port = args.Length > 1 ? int.Parse(args[1]) : 11111;
            
            Console.WriteLine($"Starting RPC client, connecting to {host}:{port}...");
            
            var clientHost = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                {
                    logging.AddConsole();
                    logging.SetMinimumLevel(LogLevel.Information);
                    logging.AddFilter("Forkleans.Rpc", LogLevel.Debug);
                })
                .UseOrleansRpcClient(rpcClient =>
                {
                    rpcClient.ConnectTo(host, port);
                    rpcClient.UseLiteNetLib();
                })
                .Build();

            await clientHost.StartAsync();
            Console.WriteLine("[CLIENT] RPC Client started");
            
            // Wait a bit for connection
            await Task.Delay(1000);

            try
            {
                var client = clientHost.Services.GetRequiredService<IClusterClient>();
                Console.WriteLine("[CLIENT] Got IClusterClient");
                
                var grain = client.GetGrain<IHelloGrain>("1");
                Console.WriteLine("[CLIENT] Got grain reference");
                
                Console.WriteLine("[CLIENT] Calling SayHello...");
                var cts = new System.Threading.CancellationTokenSource(5000);
                var result = await grain.SayHello("World").AsTask().WaitAsync(cts.Token);
                Console.WriteLine($"[CLIENT] Response: {result}");
                
                // Test echo
                Console.WriteLine("\n[CLIENT] Testing Echo...");
                var echoResult = await grain.Echo("This is a test message").AsTask().WaitAsync(cts.Token);
                Console.WriteLine($"[CLIENT] Echo response: {echoResult}");

                // Test complex types
                Console.WriteLine("\n[CLIENT] Testing GetDetailedGreeting...");
                var request = new HelloRequest
                {
                    Name = "Alice",
                    Age = 30,
                    Location = "Seattle"
                };
                var detailedResponse = await grain.GetDetailedGreeting(request).AsTask().WaitAsync(cts.Token);
                Console.WriteLine($"[CLIENT] Greeting: {detailedResponse.Greeting}");
                Console.WriteLine($"[CLIENT] Server time: {detailedResponse.ServerTime}");
                Console.WriteLine($"[CLIENT] Process ID: {detailedResponse.ProcessId}");
                
                Console.WriteLine("\n[CLIENT] All tests completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CLIENT] Error: {ex.GetType().Name} - {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            await clientHost.StopAsync();
        }
    }
}
