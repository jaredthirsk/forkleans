using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Forkleans.Rpc;
using Forkleans.Rpc.Transport.LiteNetLib;

namespace Forkleans.Rpc.IntegrationTest.Server
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var port = args.Length > 0 ? int.Parse(args[0]) : 11111;
            
            Console.WriteLine($"Starting RPC server on port {port}...");
            
            var serverHost = Host.CreateDefaultBuilder()
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

            await serverHost.StartAsync();
            Console.WriteLine($"[SERVER] RPC Server started on port {port}");
            Console.WriteLine("[SERVER] Press Ctrl+C to stop the server...");
            
            await serverHost.WaitForShutdownAsync();
        }
    }
}