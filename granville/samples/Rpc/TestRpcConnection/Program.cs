using System;
using System.Net;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Granville.Rpc;
using Granville.Rpc.Hosting;
using Granville.Rpc.Transport.LiteNetLib;
using Orleans;
using Orleans.Metadata;
using Orleans.Runtime;
using Orleans.Serialization;
using Shooter.Shared.RpcInterfaces;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Starting RPC connection test...");
        
        var serverEndpoint = new IPEndPoint(IPAddress.Loopback, 12000);
        Console.WriteLine($"Connecting to RPC server at {serverEndpoint}");
        
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Debug);
                logging.AddFilter("Granville.Rpc", LogLevel.Debug);
                logging.AddFilter("Orleans", LogLevel.Debug);
            })
            .UseOrleansRpcClient(rpc =>
            {
                rpc.ConnectTo(serverEndpoint.Address.ToString(), serverEndpoint.Port);
                rpc.UseLiteNetLib();
            })
            .ConfigureServices(services =>
            {
                // Serialization is configured automatically by UseOrleansRpcClient
            })
            .Build();
            
        try
        {
            Console.WriteLine("Starting host...");
            await host.StartAsync();
            Console.WriteLine("Host started successfully");
            
            // Get the RPC client
            var rpcClient = host.Services.GetRequiredService<IRpcClient>();
            Console.WriteLine($"RPC client obtained: {rpcClient.GetType().FullName}");
            
            // Check manifest provider
            try
            {
                var manifestProvider = host.Services.GetKeyedService<IClusterManifestProvider>("rpc");
                Console.WriteLine($"Manifest provider type: {manifestProvider?.GetType().FullName ?? "NULL"}");
                
                if (manifestProvider != null)
                {
                    var manifest = manifestProvider.Current;
                    Console.WriteLine($"Current manifest version: {manifest?.Version}");
                    Console.WriteLine($"Grain count: {manifest?.AllGrainManifests.Sum(m => m.Grains.Count) ?? 0}");
                    Console.WriteLine($"Interface count: {manifest?.AllGrainManifests.Sum(m => m.Interfaces.Count) ?? 0}");
                    
                    // List available grain types
                    if (manifest?.AllGrainManifests != null)
                    {
                        foreach (var silo in manifest.AllGrainManifests)
                        {
                            foreach (var grain in silo.Grains)
                            {
                                Console.WriteLine($"  Grain: {grain.Key}");
                                foreach (var prop in grain.Value.Properties)
                                {
                                    if (prop.Key.StartsWith("interface."))
                                    {
                                        Console.WriteLine($"    Interface: {prop.Value}");
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking manifest: {ex.Message}");
            }
            
            // Try to get grain factory
            try
            {
                var grainFactory = host.Services.GetKeyedService<IGrainFactory>("rpc");
                Console.WriteLine($"Grain factory type: {grainFactory?.GetType().FullName ?? "NULL"}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting grain factory: {ex.Message}");
            }
            
            // Wait for manifest to be populated
            Console.WriteLine("\nWaiting for manifest to be populated...");
            try
            {
                await rpcClient.WaitForManifestAsync(TimeSpan.FromSeconds(5));
                Console.WriteLine("Manifest is ready!");
            }
            catch (TimeoutException ex)
            {
                Console.WriteLine($"WARNING: {ex.Message}");
            }
            
            // Try to get a grain
            Console.WriteLine("\nAttempting to get IGameRpcGrain...");
            try
            {
                var grain = rpcClient.GetGrain<IGameRpcGrain>("game");
                Console.WriteLine($"SUCCESS: Got grain reference: {grain?.GetType().FullName ?? "NULL"}");
                
                // Try to call a method
                Console.WriteLine("\nTrying to call GetAvailableZones()...");
                var zones = await grain.GetAvailableZones();
                Console.WriteLine($"Available zones: {zones?.Count ?? 0}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR getting grain: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                }
            }
            
            await host.StopAsync();
            Console.WriteLine("\nTest completed");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FATAL ERROR: {ex}");
            Environment.Exit(1);
        }
    }
}
