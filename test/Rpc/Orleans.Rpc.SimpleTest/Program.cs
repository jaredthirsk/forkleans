using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Forkleans;
using Forkleans.Rpc;
using Forkleans.Rpc.TestGrainInterfaces;
using Forkleans.Rpc.Transport.LiteNetLib;

namespace Orleans.Rpc.SimpleTest
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var serverPort = 11111;
            var cts = new CancellationTokenSource();

            // Start server
            var serverTask = Task.Run(async () =>
            {
                var serverHost = Host.CreateDefaultBuilder()
                    .ConfigureLogging(logging =>
                    {
                        logging.ClearProviders();
                        logging.AddConsole();
                        logging.SetMinimumLevel(LogLevel.Information);
                        logging.AddFilter("Forkleans.Rpc", LogLevel.Debug);
                    })
                    .UseOrleansRpc(rpcServer =>
                    {
                        rpcServer.ConfigureEndpoint(serverPort);
                        rpcServer.UseLiteNetLib();
                    })
                    .Build();

                await serverHost.RunAsync(cts.Token);
            });

            // Wait for server to start
            await Task.Delay(2000);

            // Create client
            var clientHost = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.SetMinimumLevel(LogLevel.Information);
                    logging.AddFilter("Forkleans.Rpc", LogLevel.Debug);
                    logging.AddFilter("Forkleans.Runtime", LogLevel.Debug);
                })
                .UseOrleansRpcClient(rpcClient =>
                {
                    rpcClient.ConnectTo("127.0.0.1", serverPort);
                    rpcClient.UseLiteNetLib();
                })
                .Build();

            await clientHost.StartAsync();
            
            // Wait for connection
            await Task.Delay(1000);

            try
            {
                var client = clientHost.Services.GetRequiredService<IClusterClient>();
                Console.WriteLine("[TEST] Got IClusterClient");
                
                var grain = client.GetGrain<IHelloGrain>("test-grain");
                Console.WriteLine("[TEST] Got grain reference");
                
                Console.WriteLine("[TEST] Calling SayHello...");
                var result = await grain.SayHello("SimpleTest");
                Console.WriteLine($"[TEST] Result: {result}");
                
                Console.WriteLine("[TEST] Success!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TEST] Error: {ex.GetType().Name} - {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            // Cleanup
            await clientHost.StopAsync();
            cts.Cancel();
            await serverTask;
        }
    }
}