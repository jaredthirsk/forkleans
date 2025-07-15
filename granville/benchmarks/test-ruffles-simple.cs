using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Granville.Benchmarks.Core.Transport;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Testing Ruffles Raw Transport...");
        
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<RufflesRawTransport>();
        
        var transport = new RufflesRawTransport(logger);
        var config = new RawTransportConfig
        {
            TransportType = "Ruffles",
            Host = "127.0.0.1",
            Port = 12345,
            TimeoutMs = 5000,
            Reliable = false
        };
        
        try
        {
            Console.WriteLine("Initializing transport...");
            await transport.InitializeAsync(config);
            
            Console.WriteLine("Sending test packet...");
            var testData = new byte[256];
            new Random().NextBytes(testData);
            
            var result = await transport.SendAsync(testData, false, default);
            
            Console.WriteLine($"Result: Success={result.Success}, Latency={result.LatencyMicroseconds:F2}μs");
            if (!result.Success)
            {
                Console.WriteLine($"Error: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Test failed: {ex.Message}");
        }
        finally
        {
            await transport.CloseAsync();
        }
    }
}