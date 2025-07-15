using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Granville.Benchmarks.Core.Transport;

class Program
{
    static async Task Main(string[] args)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var serviceProvider = services.BuildServiceProvider();
        
        Console.WriteLine("=== Granville RPC Overhead Measurement ===");
        
        var config = new RawTransportConfig
        {
            TransportType = "PureLiteNetLib",
            Host = "127.0.0.1", 
            Port = 12346,
            TimeoutMs = 5000,
            Reliable = false
        };
        
        // Test Pure LiteNetLib (baseline)
        Console.WriteLine("\n1. Testing Pure LiteNetLib (true baseline)...");
        await TestTransport("Pure LiteNetLib", () => TransportFactory.CreatePureRufflesTransport(serviceProvider), config);
        
        // Test LiteNetLib Bypass (Granville abstraction)
        config.TransportType = "LiteNetLib";
        Console.WriteLine("\n2. Testing LiteNetLib Bypass (Granville abstraction)...");
        await TestTransport("LiteNetLib Bypass", () => TransportFactory.CreateLiteNetLibTransport(serviceProvider), config);
        
        Console.WriteLine("\n=== Results ===");
        Console.WriteLine("Compare latencies to see Granville abstraction overhead");
    }
    
    static async Task TestTransport(string name, Func<IRawTransport> factory, RawTransportConfig config)
    {
        try
        {
            var transport = factory();
            
            // Start server (simplified - would need actual server)
            Console.WriteLine($"Testing {name}...");
            Console.WriteLine($"Config would connect to {config.Host}:{config.Port}");
            Console.WriteLine($"Would need benchmark server running for actual test");
            
            // Simulate what we would measure
            Console.WriteLine($"{name}: Ready for latency measurement");
            
            transport.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error testing {name}: {ex.Message}");
        }
    }
}