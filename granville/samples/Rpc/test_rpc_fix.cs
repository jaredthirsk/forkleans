using System;
using System.Threading.Tasks;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Microsoft.Extensions.Logging;
using Granville.Rpc;
using Shooter.Shared.RpcInterfaces;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Testing RPC string deserialization fix...");
        
        // Test the specific scenario: ConnectPlayer with a GUID string
        var testPlayerId = "64087c8d-f724-48b2-b70c-6268c315ab49";
        
        Console.WriteLine($"Test player ID: {testPlayerId}");
        Console.WriteLine($"String is null or empty: {string.IsNullOrEmpty(testPlayerId)}");
        Console.WriteLine($"String length: {testPlayerId.Length}");
        
        // Try to connect to the running ActionServer at RPC port 12005
        try
        {
            using var host = new OutsideRpcRuntimeClient();
            
            // Configure the client to connect to our test ActionServer
            await host.ConnectAsync("127.0.0.1", 12005);
            
            // Get the game grain
            var gameGrain = host.GetGrain<IGameRpcGrain>("test-zone");
            
            // Call ConnectPlayer with our test string
            Console.WriteLine($"Calling ConnectPlayer with: '{testPlayerId}'");
            var result = await gameGrain.ConnectPlayer(testPlayerId);
            Console.WriteLine($"Result: {result}");
            
            await host.CloseAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        
        Console.WriteLine("Test completed.");
    }
}