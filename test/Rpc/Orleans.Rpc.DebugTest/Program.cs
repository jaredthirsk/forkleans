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

class Program
{
    static async Task Main(string[] args)
    {
        var serverPort = 11111;
        var cts = new CancellationTokenSource();

        // Start server
        var serverTask = Task.Run(async () =>
        {
            try
            {
                var serverHost = Host.CreateDefaultBuilder()
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

                await serverHost.RunAsync(cts.Token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SERVER ERROR] {ex}");
            }
        });

        // Wait for server to start
        await Task.Delay(2000);
        Console.WriteLine("[TEST] Server should be running");

        // Create client
        try
        {
            var clientHost = Host.CreateDefaultBuilder()
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

            await clientHost.StartAsync();
            Console.WriteLine("[TEST] Client started");
            
            // Wait for connection
            await Task.Delay(1000);

            try
            {
                var client = clientHost.Services.GetRequiredService<IClusterClient>();
                Console.WriteLine("[TEST] Got IClusterClient");
                
                Console.WriteLine("[TEST] Creating grain reference...");
                var grain = client.GetGrain<IHelloGrain>("test-grain");
                Console.WriteLine($"[TEST] Got grain reference: {grain}");
                
                Console.WriteLine("[TEST] Calling SayHello...");
                var sayHelloTask = grain.SayHello("DebugTest");
                Console.WriteLine("[TEST] Method call initiated, waiting for result...");
                
                var result = await sayHelloTask.AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                Console.WriteLine($"[TEST] Result: {result}");
                
                Console.WriteLine("[TEST] Success!");
            }
            catch (TimeoutException tex)
            {
                Console.WriteLine($"[TEST] Timeout: {tex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TEST] Error during grain call: {ex.GetType().Name} - {ex.Message}");
                Console.WriteLine($"[TEST] Stack trace: {ex.StackTrace}");
                
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"[TEST] Inner exception: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}");
                    Console.WriteLine($"[TEST] Inner stack trace: {ex.InnerException.StackTrace}");
                }
            }

            await clientHost.StopAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CLIENT ERROR] {ex}");
        }

        // Stop server
        cts.Cancel();
        try
        {
            await serverTask;
        }
        catch { }
        
        Console.WriteLine("[TEST] Complete");
    }
}