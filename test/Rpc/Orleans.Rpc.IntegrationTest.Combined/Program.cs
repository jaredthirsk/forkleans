using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Forkleans;
using Forkleans.Rpc;
using Forkleans.Rpc.TestGrainInterfaces;
using Forkleans.Rpc.Transport.LiteNetLib;
using Forkleans.Rpc.Transport;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Orleans.Rpc.IntegrationTest.Combined
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var serverPort = 41111;

            // Start server in background
            var serverTask = RunServer(serverPort);

            // Give server time to start
            await Task.Delay(2000);

            // Run client
            await RunClient(serverPort);

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        static async Task RunServer(int port)
        {
            var host = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                {
                    logging.AddConsole();
                    logging.SetMinimumLevel(LogLevel.Information);
                    logging.AddFilter("Forkleans.Rpc", LogLevel.Debug);
                })
                .UseOrleansRpc(rpcServer =>
                {
                    rpcServer.ConfigureEndpoint(port);
                    rpcServer.UseLiteNetLib();
                })
                .Build();

            await host.StartAsync();
            Console.WriteLine($"[SERVER] RPC Server started on port {port}");

            // Keep server running
            await host.WaitForShutdownAsync();
        }

        static async Task RunClient(int port)
        {
            var host = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                {
                    logging.AddConsole();
                    logging.SetMinimumLevel(LogLevel.Information);
                    logging.AddFilter("Forkleans.Rpc", LogLevel.Debug);
                })
                .UseOrleansRpcClient(rpcClient =>
                {
                    rpcClient.ConnectTo("127.0.0.1", port);
                    rpcClient.UseLiteNetLib();
                })
                .Build();

            await host.StartAsync();
            Console.WriteLine("[CLIENT] RPC Client starting...");
            
            // Give the client time to establish connection
            await Task.Delay(1000);

            try
            {
                var client = host.Services.GetRequiredService<IClusterClient>();

                // Test basic call
                Console.WriteLine("\n[CLIENT] Testing SayHello...");
                var grain = client.GetGrain<IHelloGrain>("1");
                var result = await grain.SayHello("World").AsTask();
                Console.WriteLine($"[CLIENT] Response: {result}");

                // Test echo
                Console.WriteLine("\n[CLIENT] Testing Echo...");
                var echoResult = await grain.Echo("This is a test message").AsTask();
                Console.WriteLine($"[CLIENT] Echo response: {echoResult}");

                // Test complex types
                Console.WriteLine("\n[CLIENT] Testing GetDetailedGreeting...");
                var request = new HelloRequest
                {
                    Name = "Alice",
                    Age = 30,
                    Location = "Seattle"
                };
                var detailedResponse = await grain.GetDetailedGreeting(request).AsTask();
                Console.WriteLine($"[CLIENT] Greeting: {detailedResponse.Greeting}");
                Console.WriteLine($"[CLIENT] Server time: {detailedResponse.ServerTime}");
                Console.WriteLine($"[CLIENT] Process ID: {detailedResponse.ProcessId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CLIENT] Error: {ex.GetType().Name} - {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            await host.StopAsync();
        }
    }
}
