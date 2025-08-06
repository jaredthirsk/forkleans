using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Simple test service to avoid Orleans complexity
public interface ITestService
{
    string Name { get; }
}

public class TestService : ITestService
{
    public string Name { get; }
    public TestService(string name) => Name = name;
}

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Testing Keyed Service Resolution ===");
        
        try
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices(services =>
                {
                    services.AddLogging(logging =>
                    {
                        logging.ClearProviders();
                        logging.AddConsole();
                        logging.SetMinimumLevel(LogLevel.Debug);
                    });
                    
                    // Test basic keyed service registration
                    services.AddKeyedSingleton<TestService>("test", (sp, key) =>
                    {
                        Console.WriteLine($"Creating keyed TestService with key '{key}'");
                        return new TestService($"TestService-{key}");
                    });
                    
                    services.AddKeyedSingleton<ITestService>("test", (sp, key) =>
                    {
                        Console.WriteLine($"Creating keyed ITestService with key '{key}'");
                        return sp.GetRequiredKeyedService<TestService>("test");
                    });
                })
                .Build();
            
            var provider = host.Services;
            
            Console.WriteLine("\nTesting keyed service resolution:");
            
            // Test 1: Direct keyed service resolution
            Console.WriteLine("\n1. Testing GetKeyedService<TestService>('test')...");
            try
            {
                var keyedServices = provider.GetService<IKeyedServiceProvider>();
                if (keyedServices != null)
                {
                    Console.WriteLine("   IKeyedServiceProvider is available");
                    var testService = keyedServices.GetKeyedService<TestService>("test");
                    Console.WriteLine($"   Result: {(testService != null ? $"SUCCESS - {testService.Name}" : "NULL")}");
                }
                else
                {
                    Console.WriteLine("   IKeyedServiceProvider NOT available");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ERROR: {ex.GetType().Name} - {ex.Message}");
            }
            
            // Test 2: GetRequiredKeyedService
            Console.WriteLine("\n2. Testing GetRequiredKeyedService<ITestService>('test')...");
            try
            {
                var testService = provider.GetRequiredKeyedService<ITestService>("test");
                Console.WriteLine($"   Result: SUCCESS - {testService.GetType().Name} - {testService.Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ERROR: {ex.GetType().Name} - {ex.Message}");
            }
            
            // Test 3: Check if this is a timeout issue
            Console.WriteLine("\n3. Testing with timeout detection...");
            var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2));
            var task = Task.Run(() =>
            {
                try
                {
                    var testService = provider.GetRequiredKeyedService<ITestService>("test");
                    return $"SUCCESS - {testService.Name}";
                }
                catch (Exception ex)
                {
                    return $"ERROR: {ex.GetType().Name}";
                }
            });
            
            try
            {
                var result = await task.WaitAsync(cts.Token);
                Console.WriteLine($"   Result: {result}");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("   ERROR: Operation timed out after 2 seconds!");
            }
            
            Console.WriteLine("\n=== Test Completed ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nUnexpected error: {ex}");
        }
    }
}