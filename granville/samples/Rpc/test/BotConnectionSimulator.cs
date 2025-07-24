using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Granville.Rpc;
using Granville.Rpc.Configuration;
using Granville.Rpc.Transport.LiteNetLib;
using Orleans.Serialization;

// Simulate the exact bot RPC connection process to isolate the hanging issue
Console.WriteLine("=== Bot RPC Connection Simulator ===");

// Setup services like the bot does
var services = new ServiceCollection();

// Add logging
services.AddLogging(builder => 
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});

// Add Orleans serialization (required for RPC)
services.AddSingleton<Serializer>();

// Add RPC client options
services.Configure<RpcClientOptions>(options =>
{
    options.ClientId = "TestBot";
    options.ServerEndpoints.Add(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 12000));
});

// Add RPC transport options
services.Configure<RpcTransportOptions>(options =>
{
    options.TransportType = "LiteNetLib";
});

// Add LiteNetLib transport factory
services.AddSingleton<LiteNetLibTransportFactory>();
services.AddSingleton<Granville.Rpc.Transport.IRpcTransportFactory>(provider => 
    provider.GetRequiredService<LiteNetLibTransportFactory>());

var serviceProvider = services.BuildServiceProvider();
var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

async Task TestRpcTransportConnection()
{
    logger.LogInformation("=== Testing RPC Transport Connection ===");
    
    try
    {
        // Get the transport factory
        logger.LogDebug("Getting transport factory");
        var transportFactory = serviceProvider.GetRequiredService<Granville.Rpc.Transport.IRpcTransportFactory>();
        logger.LogDebug("Transport factory obtained: {Type}", transportFactory.GetType().Name);
        
        // Create transport
        logger.LogDebug("Creating transport");
        var transport = transportFactory.CreateTransport(serviceProvider);
        logger.LogDebug("Transport created: {Type}", transport.GetType().Name);
        
        // Set up endpoint
        var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 12000);
        logger.LogInformation("Attempting to connect to {Endpoint}", endpoint);
        
        // This is where the bot hangs - test the ConnectAsync call with timeout
        logger.LogWarning("CRITICAL: About to call transport.ConnectAsync - this is where bot hangs");
        
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        
        var connectTask = transport.ConnectAsync(endpoint, cts.Token);
        logger.LogDebug("ConnectAsync call initiated, waiting for completion...");
        
        await connectTask;
        
        logger.LogInformation("✅ SUCCESS: transport.ConnectAsync completed!");
        logger.LogInformation("Transport is now connected, disposing...");
        
        transport.Dispose();
        logger.LogInformation("Transport disposed successfully");
    }
    catch (OperationCanceledException)
    {
        logger.LogError("❌ TIMEOUT: transport.ConnectAsync timed out after 10 seconds - THIS IS THE BUG!");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "❌ ERROR: transport.ConnectAsync failed with exception: {Message}", ex.Message);
        logger.LogError("Exception type: {Type}", ex.GetType().Name);
        logger.LogError("Stack trace: {StackTrace}", ex.StackTrace);
    }
}

async Task TestBasicUdpToServer()
{
    logger.LogInformation("=== Testing Basic UDP to Server ===");
    
    try
    {
        using var udpClient = new System.Net.Sockets.UdpClient();
        var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 12000);
        
        logger.LogDebug("Sending UDP test packet to server");
        var testData = System.Text.Encoding.UTF8.GetBytes("BASIC_TEST");
        await udpClient.SendAsync(testData, testData.Length, endpoint);
        logger.LogInformation("✅ Basic UDP send successful");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "❌ Basic UDP test failed: {Message}", ex.Message);
    }
}

// Run tests
logger.LogInformation("Starting Bot RPC Connection Simulation...");

await TestBasicUdpToServer();
await TestRpcTransportConnection();

logger.LogInformation("=== Simulation Complete ===");